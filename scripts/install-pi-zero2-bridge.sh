#!/bin/sh
set -eu

TARGET_USER="${ROAMADB_USER:-roamadb}"
BACKUP_ROOT="/var/backups/roamadb"
STAMP="$(date +%Y%m%d-%H%M%S)"
BACKUP_DIR="$BACKUP_ROOT/$STAMP"
MANIFEST="$BACKUP_DIR/manifest.tsv"

if [ "$(id -u)" -ne 0 ]; then
    printf '%s\n' 'Run as root: sudo sh scripts/install-pi-zero2-bridge.sh' >&2
    exit 1
fi

case "$TARGET_USER" in
    ''|*[!A-Za-z0-9_-]*)
        printf 'Invalid ROAMADB_USER: %s\n' "$TARGET_USER" >&2
        exit 1
        ;;
esac

if ! id "$TARGET_USER" >/dev/null 2>&1; then
    printf 'User does not exist: %s\n' "$TARGET_USER" >&2
    exit 1
fi

mkdir -p "$BACKUP_DIR"
chmod 0700 "$BACKUP_ROOT" "$BACKUP_DIR"
: >"$MANIFEST"
chmod 0600 "$MANIFEST"

backup_once() {
    path="$1"
    if grep -Fqx "$path" "$BACKUP_DIR/backed-up-paths" 2>/dev/null; then
        return
    fi
    printf '%s\n' "$path" >>"$BACKUP_DIR/backed-up-paths"
    if [ -e "$path" ] || [ -L "$path" ]; then
        printf 'present\t%s\n' "$path" >>"$MANIFEST"
        mkdir -p "$BACKUP_DIR$(dirname "$path")"
        cp -a "$path" "$BACKUP_DIR$path"
    else
        printf 'absent\t%s\n' "$path" >>"$MANIFEST"
    fi
}

install_managed() {
    target="$1"
    mode="$2"
    owner="$3"
    group="$4"
    staged="$(mktemp)"
    cat >"$staged"
    backup_once "$target"
    mkdir -p "$(dirname "$target")"
    install -o "$owner" -g "$group" -m "$mode" "$staged" "$target"
    rm -f "$staged"
}

remove_managed() {
    target="$1"
    backup_once "$target"
    rm -f "$target"
}

export DEBIAN_FRONTEND=noninteractive
apt-get update
if [ "${ROAMADB_SKIP_OS_UPGRADE:-0}" != 1 ]; then
    apt-get full-upgrade -y
fi
apt-get install -y adb nftables openssh-server

if ! command -v tailscale >/dev/null 2>&1; then
    printf '%s\n' 'Tailscale is not installed. Install and authenticate it before field use.' >&2
    exit 1
fi

# Raspberry Pi Zero 2 W: keep the USB data port in host mode even when a phone
# is connected before power-on.
BOOT_CONFIG=/boot/firmware/config.txt
if ! awk '
    /^\[/ { section = $0 }
    section == "[pi02]" && $0 == "dtoverlay=dwc2,dr_mode=host" { found = 1 }
    END { exit(found ? 0 : 1) }
' "$BOOT_CONFIG"; then
    backup_once "$BOOT_CONFIG"
    cat >>"$BOOT_CONFIG" <<'EOF'

# RoamADB: force the Zero 2 W USB data port into host mode.
[pi02]
dtoverlay=dwc2,dr_mode=host

[all]
EOF
fi

install_managed /usr/local/sbin/roamadb-auto-tether 0755 root root <<'EOF'
#!/bin/sh
set -u

ADB=/usr/bin/adb
LOCK=/run/lock/roamadb-auto-tether.lock

exec 9>"$LOCK"
flock -n 9 || exit 0

attempt=0
while [ "$attempt" -lt 30 ]; do
    state="$($ADB get-state 2>/dev/null || true)"
    if [ "$state" = "device" ]; then
        functions="$($ADB shell getprop sys.usb.state 2>/dev/null | tr -d '\r' || true)"
        case "$functions" in
            *rndis*|*ncm*) exit 0 ;;
        esac
        logger -t roamadb-auto-tether 'Authorized ADB device detected; enabling USB tethering'
        $ADB shell svc usb setFunctions rndis >/dev/null 2>&1 || true
        exit 0
    fi
    attempt=$((attempt + 1))
    sleep 1
done

logger -t roamadb-auto-tether 'No authorized ADB device became ready within 30 seconds'
exit 0
EOF

install_managed /usr/local/sbin/roamadb-status 0755 root root <<'EOF'
#!/bin/sh
set -u

echo '== RoamADB bridge =='
echo "hostname: $(hostname)"
echo "time: $(date -Is)"
echo "uptime: $(uptime -p)"
echo
echo '== Network =='
ip -brief address show wlan0 2>/dev/null || true
ip -brief address show tailscale0 2>/dev/null || true
ip -brief address show usb0 2>/dev/null || true
echo
echo '== Services =='
for unit in ssh tailscaled roamadb-ssh-firewall roamadb-adb; do
    printf '%-24s enabled=%-8s active=%s\n' "$unit" \
        "$(systemctl is-enabled "$unit" 2>/dev/null || true)" \
        "$(systemctl is-active "$unit" 2>/dev/null || true)"
done
echo
echo '== ADB =='
adb devices -l 2>/dev/null || true
echo
echo '== Thermal =='
vcgencmd measure_temp 2>/dev/null || true
vcgencmd get_throttled 2>/dev/null || true
EOF

install_managed /usr/local/sbin/roamadb-poweroff 0755 root root <<'EOF'
#!/bin/sh
set -eu
logger -t roamadb-poweroff "Safe poweroff requested by ${SUDO_USER:-root}"
sync
exec /usr/bin/systemctl poweroff
EOF

install_managed /usr/local/sbin/roamadb-maintain 0755 root root <<'EOF'
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
        exec /usr/local/sbin/roamadb-poweroff
        ;;
    *)
        printf 'Unknown maintenance action: %s\n' "$1" >&2
        exit 2
        ;;
esac
EOF

install_managed /etc/systemd/system/roamadb-adb.service 0644 root root <<EOF
[Unit]
Description=RoamADB local-only ADB server
After=systemd-udev-settle.service
Wants=systemd-udev-settle.service

[Service]
Type=simple
User=$TARGET_USER
Group=$TARGET_USER
SupplementaryGroups=plugdev
Environment=HOME=/home/$TARGET_USER
Environment=ADB_VENDOR_KEYS=/home/$TARGET_USER/.android
ExecStart=/usr/bin/adb nodaemon server
Restart=on-failure
RestartSec=3
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=full
ProtectHome=read-only
ReadWritePaths=/home/$TARGET_USER/.android
ProtectClock=true
ProtectControlGroups=true
ProtectKernelModules=true
ProtectKernelTunables=true
LockPersonality=true
RestrictSUIDSGID=true
RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6 AF_NETLINK

[Install]
WantedBy=multi-user.target
EOF

install_managed /etc/systemd/system/roamadb-auto-tether.service 0644 root root <<EOF
[Unit]
Description=RoamADB automatic Android USB tethering
After=roamadb-adb.service
Wants=roamadb-adb.service

[Service]
Type=oneshot
User=$TARGET_USER
Group=$TARGET_USER
SupplementaryGroups=plugdev
Environment=HOME=/home/$TARGET_USER
ExecStart=/usr/local/sbin/roamadb-auto-tether
TimeoutStartSec=35s
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=read-only
ReadWritePaths=/run/lock
ProtectClock=true
ProtectControlGroups=true
ProtectKernelModules=true
ProtectKernelTunables=true
LockPersonality=true
RestrictSUIDSGID=true
RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6 AF_NETLINK
EOF

install_managed /etc/udev/rules.d/90-roamadb-android-usb.rules 0644 root root <<'EOF'
# Start the bounded readiness check for a newly attached USB device. The script
# changes Android USB functions only after adb reports an authorized device.
ACTION=="add", SUBSYSTEM=="usb", ENV{DEVTYPE}=="usb_device", TAG+="systemd", ENV{SYSTEMD_WANTS}+="roamadb-auto-tether.service"
EOF

install_managed /etc/nftables.d/roamadb-ssh.nft 0600 root root <<'EOF'
table inet roamadb_ssh {
    chain input {
        type filter hook input priority filter + 10; policy accept;
        iifname "lo" tcp dport 22 accept
        iifname "tailscale0" tcp dport 22 accept
        iifname "usb0" tcp dport 22 accept
        tcp dport 22 drop
    }
}
EOF

install_managed /etc/systemd/system/roamadb-ssh-firewall.service 0644 root root <<'EOF'
[Unit]
Description=Allow SSH only on Tailscale and the direct USB recovery interface
DefaultDependencies=no
After=local-fs.target
Before=network-pre.target ssh.service
Wants=network-pre.target

[Service]
Type=oneshot
ExecStartPre=-/usr/sbin/nft delete table inet roamadb_ssh
ExecStart=/usr/sbin/nft -f /etc/nftables.d/roamadb-ssh.nft
ExecStop=-/usr/sbin/nft delete table inet roamadb_ssh
RemainAfterExit=yes
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
ProtectClock=true
ProtectControlGroups=true
ProtectKernelModules=true
ProtectKernelTunables=true
LockPersonality=true
RestrictSUIDSGID=true
CapabilityBoundingSet=CAP_NET_ADMIN
AmbientCapabilities=CAP_NET_ADMIN
RestrictAddressFamilies=AF_NETLINK

[Install]
WantedBy=multi-user.target
EOF

install_managed /etc/NetworkManager/dispatcher.d/90-roamadb-route-metric 0755 root root <<'EOF'
#!/bin/sh
interface="$1"
event="$2"

case "$event" in
    up|dhcp4-change) ;;
    *) exit 0 ;;
esac

case "$interface" in
    usb*) metric=100 ;;
    wlan0) metric=600 ;;
    *) exit 0 ;;
esac

[ -n "${CONNECTION_ID:-}" ] || exit 0
/usr/bin/nmcli connection modify "$CONNECTION_ID" ipv4.route-metric "$metric" ipv6.route-metric "$metric" >/dev/null 2>&1 || true
exit 0
EOF

install_managed /etc/ssh/sshd_config.d/90-roamadb-hardening.conf 0644 root root <<EOF
PermitRootLogin no
PasswordAuthentication no
KbdInteractiveAuthentication no
PubkeyAuthentication yes
AllowUsers $TARGET_USER
EOF

# Provide only fixed, root-owned appliance maintenance actions. The wrapper
# rejects additional arguments and never executes a user-supplied path.
install_managed /etc/sudoers.d/90-roamadb-poweroff 0440 root root <<EOF
$TARGET_USER ALL=(root) NOPASSWD: /usr/local/sbin/roamadb-poweroff
$TARGET_USER ALL=(root) NOPASSWD: /usr/local/sbin/roamadb-maintain
EOF

# Remove Raspberry Pi Imager/cloud-init's broad passwordless sudo grants for
# this account without changing grants that belong to other accounts.
for sudo_file in /etc/sudoers.d/010_${TARGET_USER}-nopasswd /etc/sudoers.d/010_${TARGET_USER}-nopasswd-*; do
    [ -e "$sudo_file" ] || continue
    remove_managed "$sudo_file"
done
remove_managed /etc/sudoers.d/99-roamadb-recovery
remove_managed /etc/sudoers.d/91-roamadb-maintain

if [ -f /etc/sudoers.d/90-cloud-init-users ]; then
    backup_once /etc/sudoers.d/90-cloud-init-users
    staged_sudo="$(mktemp)"
    awk -v user="$TARGET_USER" '
        $1 == user && $0 ~ /NOPASSWD:[[:space:]]*ALL/ { next }
        { print }
    ' /etc/sudoers.d/90-cloud-init-users >"$staged_sudo"
    install -o root -g root -m 0440 "$staged_sudo" /etc/sudoers.d/90-cloud-init-users
    rm -f "$staged_sudo"
fi

visudo -cf /etc/sudoers >/dev/null
sshd -t
nft -c -f /etc/nftables.d/roamadb-ssh.nft

if [ -f /lib/netplan/00-network-manager-all.yaml ]; then
    backup_once /lib/netplan/00-network-manager-all.yaml
    chmod 0600 /lib/netplan/00-network-manager-all.yaml
fi

systemctl disable --now roamadb-auto-tether.timer 2>/dev/null || true
remove_managed /etc/systemd/system/roamadb-auto-tether.timer
systemctl disable --now avahi-daemon.service avahi-daemon.socket bluetooth.service 2>/dev/null || true

systemctl daemon-reload
udevadm control --reload-rules
systemctl enable roamadb-ssh-firewall.service roamadb-adb.service ssh.service tailscaled.service
systemctl restart roamadb-ssh-firewall.service
systemctl restart roamadb-adb.service
systemctl try-restart ssh.service
systemctl start roamadb-auto-tether.service || true

printf 'BACKUP_DIR=%s\n' "$BACKUP_DIR"
printf '%s\n' 'RoamADB bridge configuration installed. Reboot once, then run roamadb-status.'
