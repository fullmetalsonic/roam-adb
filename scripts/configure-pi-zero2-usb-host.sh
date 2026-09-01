#!/bin/sh
set -eu

config=/boot/firmware/config.txt
stamp="$(date +%Y%m%d-%H%M%S)"
backup="${config}.roamadb-${stamp}.bak"
staged="$(mktemp)"

cleanup() {
    rm -f "$staged"
}
trap cleanup EXIT

if awk '
    /^\[/ { section = $0 }
    section == "[pi02]" && $0 == "dtoverlay=dwc2,dr_mode=host" { found = 1 }
    END { exit(found ? 0 : 1) }
' "$config"; then
    printf '%s\n' 'RoamADB Zero 2 W DWC2 host configuration is already present.'
    exit 0
fi

cp --preserve=all "$config" "$backup"
cp "$config" "$staged"

cat >>"$staged" <<'EOF'

# RoamADB: force the Zero 2 W USB data port into host mode.
[pi02]
dtoverlay=dwc2,dr_mode=host

[all]
EOF

install -o root -g root -m 0644 "$staged" "$config"

printf 'BACKUP=%s\n' "$backup"
printf '%s\n' 'CONFIGURED=dwc2,dr_mode=host for [pi02]'
