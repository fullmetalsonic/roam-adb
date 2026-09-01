#!/bin/sh
set -eu

# Emergency one-shot payload for a Pi whose target account is password-locked
# after broad NOPASSWD sudo was removed. Copy this LF-encoded file to the FAT
# boot partition, then add these kernel arguments to cmdline.txt:
# systemd.run=/boot/firmware/recover-locked-pi-admin.sh systemd.run_success_action=poweroff
#
# systemd-run-generator executes this payload as root once. The payload removes
# both arguments before poweroff so the next boot is normal.

BOOT_CMDLINE=/boot/firmware/cmdline.txt
SELF=/boot/firmware/recover-locked-pi-admin.sh

[ "$(id -u)" -eq 0 ] || {
    printf '%s\n' 'This recovery payload must be started by systemd-run-generator.' >&2
    exit 1
}

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

cat >/etc/sudoers.d/91-roamadb-maintain <<'EOF'
roamadb ALL=(root) NOPASSWD: /usr/local/sbin/roamadb-maintain
EOF
chown root:root /etc/sudoers.d/91-roamadb-maintain
chmod 0440 /etc/sudoers.d/91-roamadb-maintain
/usr/sbin/visudo -cf /etc/sudoers >/dev/null

staged="$(mktemp)"
sed \
    -e 's# systemd.run=/boot/firmware/recover-locked-pi-admin.sh##g' \
    -e 's# systemd.run_success_action=poweroff##g' \
    -e 's/[[:space:]][[:space:]]*/ /g' \
    -e 's/[[:space:]]*$//' \
    "$BOOT_CMDLINE" >"$staged"
install -o root -g root -m 0755 "$staged" "$BOOT_CMDLINE"
rm -f "$staged"

date -Is >"${SELF}.done"
sync
exit 0
