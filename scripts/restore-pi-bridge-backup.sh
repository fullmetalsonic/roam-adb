#!/bin/sh
set -eu

if [ "$(id -u)" -ne 0 ]; then
    printf '%s\n' 'Run as root.' >&2
    exit 1
fi

if [ "$#" -ne 1 ]; then
    printf 'Usage: %s /var/backups/roamadb/YYYYmmdd-HHMMSS\n' "$0" >&2
    exit 1
fi

backup_dir="$(readlink -f "$1")"
case "$backup_dir" in
    /var/backups/roamadb/*) ;;
    *)
        printf 'Refusing backup outside /var/backups/roamadb: %s\n' "$backup_dir" >&2
        exit 1
        ;;
esac

manifest="$backup_dir/manifest.tsv"
[ -f "$manifest" ] || {
    printf 'Missing manifest: %s\n' "$manifest" >&2
    exit 1
}

while IFS="$(printf '\t')" read -r state path; do
    case "$path" in
        /boot/firmware/*|/etc/*|/usr/local/sbin/*) ;;
        *)
            printf 'Refusing path outside managed roots: %s\n' "$path" >&2
            exit 1
            ;;
    esac

    if [ "$state" = present ]; then
        source_path="$backup_dir$path"
        [ -e "$source_path" ] || {
            printf 'Backup payload missing: %s\n' "$source_path" >&2
            exit 1
        }
        mkdir -p "$(dirname "$path")"
        cp -a "$source_path" "$path"
    elif [ "$state" = absent ]; then
        rm -f "$path"
    else
        printf 'Unknown manifest state: %s\n' "$state" >&2
        exit 1
    fi
done <"$manifest"

visudo -cf /etc/sudoers >/dev/null
sshd -t
systemctl daemon-reload
udevadm control --reload-rules
printf '%s\n' 'Backup restored. Reboot before field use.'
