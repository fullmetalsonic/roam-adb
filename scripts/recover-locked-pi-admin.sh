#!/bin/sh
set -eu

# Emergency one-shot payload for a Pi whose target account is password-locked
# after broad NOPASSWD sudo was removed. Copy this LF-encoded file to the FAT
# boot partition, then add these kernel arguments to cmdline.txt:
# systemd.unit=kernel-command-line.target systemd.run=/boot/firmware/recover-locked-pi-admin.sh systemd.run_success_action=poweroff
#
# systemd-run-generator executes this payload as root once. The payload removes
# all three recovery arguments before poweroff so the next boot is normal.

BOOT_CMDLINE=/boot/firmware/cmdline.txt
SELF=/boot/firmware/recover-locked-pi-admin.sh
PASSWORD_HASH_FILE=/boot/firmware/roamadb-password.hash
TARGET_USER=roamadb

[ "$(id -u)" -eq 0 ] || {
    printf '%s\n' 'This recovery payload must be started by systemd-run-generator.' >&2
    exit 1
}

[ -s "$PASSWORD_HASH_FILE" ] || {
    printf 'Missing password hash: %s\n' "$PASSWORD_HASH_FILE" >&2
    exit 1
}

password_hash="$(tr -d '\r\n' <"$PASSWORD_HASH_FILE")"
if ! printf '%s\n' "$password_hash" | /usr/bin/grep -Eq '^[$]6[$][^$]+[$][./0-9A-Za-z]+$'; then
    printf '%s\n' 'Refusing password hash that is not SHA-512 crypt.' >&2
    exit 1
fi

/usr/sbin/usermod --password "$password_hash" "$TARGET_USER"
unset password_hash

if ! /usr/bin/passwd -S "$TARGET_USER" | /usr/bin/awk '$2 == "P" { ok = 1 } END { exit(ok ? 0 : 1) }'; then
    printf '%s\n' 'Password activation verification failed.' >&2
    exit 1
fi

rm -f "$PASSWORD_HASH_FILE"

staged_maintain="$(mktemp)"
cat >"$staged_maintain" <<'EOF'
#!/bin/sh
set -eu

if [ "$#" -ne 1 ]; then
    printf 'Usage: %s {update|reboot|poweroff}\n' "$0" >&2
    exit 2
fi

case "$1" in
    update)
        export DEBIAN_FRONTEND=noninteractive
        /usr/bin/apt-get update
        /usr/bin/apt-get full-upgrade -y
        ;;
    reboot)
        /usr/bin/systemctl reboot
        ;;
    poweroff)
        sync
        exec /usr/bin/systemctl poweroff
        ;;
    *)
        printf 'Unknown maintenance action: %s\n' "$1" >&2
        exit 2
        ;;
esac
EOF
install -o root -g root -m 0755 "$staged_maintain" /usr/local/sbin/roamadb-maintain
rm -f "$staged_maintain"

staged_sudoers="$(mktemp)"
cat >"$staged_sudoers" <<'EOF'
roamadb ALL=(root) NOPASSWD: /usr/local/sbin/roamadb-maintain
EOF
/usr/sbin/visudo -cf "$staged_sudoers" >/dev/null
install -o root -g root -m 0440 "$staged_sudoers" /etc/sudoers.d/91-roamadb-maintain
rm -f "$staged_sudoers"
/usr/sbin/visudo -cf /etc/sudoers >/dev/null

staged="$(mktemp)"
sed \
    -e 's# systemd.unit=kernel-command-line.target##g' \
    -e 's# systemd.run=/boot/firmware/recover-locked-pi-admin.sh##g' \
    -e 's# systemd.run_success_action=poweroff##g' \
    -e 's/[[:space:]][[:space:]]*/ /g' \
    -e 's/[[:space:]]*$//' \
    "$BOOT_CMDLINE" >"$staged"
install -o root -g root -m 0644 "$staged" "$BOOT_CMDLINE"
rm -f "$staged"

date -Is >"${SELF}.done"
sync
exit 0
