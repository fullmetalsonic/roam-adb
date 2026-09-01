# RoamADB

RoamADB connects an Android phone away from home to the owner's Windows PC for on-demand remote ADB. The current recommended path uses the official Tailscale apps as the private network; RoamADB adds its own PC identity, phone registration, and local-only ADB relay.

The published `v0.1.2-spike` still uses Android Wireless debugging. A separate **portable USB ADB bridge** has now passed its first Raspberry Pi Zero 2 W and Fold8 hardware E2E for LTE/5G travel. The hardware path is not yet integrated into the current APK/EXE or published as a new Release.

## What is included

- **RoamADB for Android**: QR/manual PC registration, first-time Wireless ADB pairing relay, normal ADB relay, foreground notification, and Quick Settings tile.
- **RoamADB Gateway for Windows**: normal desktop window with diagnostics, start/stop, two-minute QR and six-digit registration code, registered-phone removal, `adb pair/connect/disconnect/devices`, and optional scrcpy launch.
- **Security boundary**: the Gateway listens on one exact Tailscale IPv4. ADB relay ports are exposed only on PC loopback (`127.0.0.1:47157` and `127.0.0.1:47158`) after phone-key authentication.

The program does not change router port forwarding, Windows Firewall, DMZ, Tailscale accounts, or Android's system Wireless debugging approvals.

## Download

The [`v0.1.2-spike` prerelease](https://github.com/fullmetalsonic/roam-adb/releases/tag/v0.1.2-spike) contains:

- `RoamADB-Gateway-Setup-0.1.2-spike.exe` — recommended per-user Windows installer;
- `RoamADB-Gateway-Portable-0.1.2-spike.exe` — no-install Windows x64 program;
- `RoamADB-0.1.2-spike-debug.apk` — Android 16 / API 36 or newer;
- `SHA256SUMS.txt` — download integrity values.

The Windows files are not code-signed and the APK is debug-signed, so Windows SmartScreen and Android sideload warnings are expected. This is a GitHub technical prerelease, not a Play Store release.

## First setup

1. Connect the official Tailscale app on the PC and phone to the same tailnet.
2. Install and open **RoamADB Gateway**. A normal Windows window must remain visible.
3. Press **Gateway 켜기**, then **새 등록 코드와 QR 만들기**.
4. Install/open RoamADB on Android and keep **기존 VPN 경유 · ADB 전용 (권장)** selected.
5. Press **PC 등록 QR 스캔**. Manual address, fingerprint, and code fields remain available if scanning is unavailable.
6. For the first ADB pairing only, open Android **Developer options → Wireless debugging → Pair device with pairing code**. Enter its temporary pairing port in RoamADB and open the pairing relay. Enter Android's six-digit pairing code in Windows **ADB 작업 → ADB 페어링**.
7. Save the normal Wireless debugging connect port in RoamADB. Turn on remote debugging, then press **ADB 연결** in the Windows Gateway.

Later sessions need only Tailscale on both devices, Gateway on the PC, RoamADB ON on the phone, and **ADB 연결** on the PC. See the detailed Korean guide: [`docs/기존_Tailscale_ADB_전용_모드.md`](docs/기존_Tailscale_ADB_전용_모드.md).

## Portable USB bridge plan

The planned `portable-usb-bridge` mode uses a small Linux ADB host, initially a Raspberry Pi Zero 2 W:

```text
Fold8 USB adbd ───────────────┐
Fold8 USB tethering ──────────┼─ USB ── portable bridge adb/Tailscale ── home PC
Fold8 2.4 GHz hotspot(fallback)┘
```

This path keeps Android Wireless debugging off and avoids phone root, public ADB ports, router port forwarding, and dependence on public Wi-Fi. The same phone-to-Pi USB link carries both wired ADB and USB tethering; a saved 2.4 GHz phone hotspot remains the fallback uplink. The bridge is a separate Tailscale node and needs its own power source. The Zero 2 W uses the supported `dwc2` fixed-host configuration so a phone connected before bridge power-on is enumerated during cold boot. Initial remote commands use Tailscale-restricted SSH.

Current status: initial Fold8 USB ADB, event-driven automatic USB tethering, hotspot fallback, Tailscale-address SSH, cable-preconnected cold boot, physical cable reconnect, and shell/file round trip **PASS**. A hotspot-OFF test also passed with the PC on independent home Internet and the Pi using only the phone's cellular USB tethering. Phone-only power **FAILED**, so a separate Pi power source is mandatory. The Pi configuration now has a timestamped backup, a restore script, a narrow safe-poweroff command, public-key-only SSH, a pre-SSH firewall, and no broad passwordless sudo. The first hardening pass also exposed a locked-account maintenance failure; it is recorded as open, with a one-shot SD recovery prepared but not yet physically tested. A geographically separated home-PC/remote-phone field test, long-duration reliability, standard PC ADB forwarding, scrcpy, Android Studio, and APK/EXE integration remain. See the [hardware validation record](docs/휴대형_USB_ADB_브리지_실기기_검증_2026-09-01.md), the [full reproducible bridge installer](scripts/install-pi-zero2-bridge.sh), the [locked-admin recovery guide](docs/라즈베리파이_관리자_복구.md), the [backup restore script](scripts/restore-pi-bridge-backup.sh), the [Windows safe-poweroff helper](scripts/roamadb-pi-poweroff.ps1), and the [detailed plan](docs/휴대형_USB_ADB_브리지_기획서.md).

## Verification status

- Gateway .NET build and 15 unit/integration tests: PASS.
- Android unit tests, debug APK build, and lint: PASS.
- Actual Windows installer → installed GUI launch → normal close → uninstall: PASS.
- Actual local Tailscale address, registration countdown, and QR rendering in the Windows GUI: PASS.
- Fold8 Android 17 / One UI 9 QR registration, phone authentication, Tailscale relay, USB-free ADB shell, and file round trip: PASS.
- LTE/5G-only operation, another external Wi-Fi, screen lock, long-duration recovery, and Android 16 / One UI 8: **field validation required**.
- Portable USB bridge separate power, USB ADB, automatic USB tethering, hotspot fallback, Tailscale-address access, reboot recovery, and independent-network PC shell/file E2E: **PASS**.
- Portable bridge powered only by the connected phone: **FAIL**; separate `PWR IN` power is required.
- Portable bridge physical cable reconnect: **PASS**.
- Portable bridge cold boot with the phone cable already connected: **PASS** after applying the Zero 2 W `dwc2` fixed-host configuration.
- Portable bridge event-driven tether recovery with hotspot off: **PASS**; `rndis,adb`, Tailscale SSH, and USB ADB returned in about three seconds without a polling timer.
- Portable bridge hardening: broad passwordless sudo removed; SSH public-key-only; SSH firewall starts before SSH and accepts only Tailscale/direct-USB recovery interfaces; Avahi and Bluetooth disabled.
- Portable bridge geographically separated field test, long-duration reliability, PC standard ADB forwarding, scrcpy, Android Studio, and APK/EXE integration: **not run / follow-up**.

Android may change the Wireless debugging connect port after Wi-Fi or debugging-state changes. In this spike, reopen Wireless debugging and save the new normal connect port in RoamADB before turning the relay on. Automatic mDNS port refresh remains a follow-up item.

Do not expose ADB port 5555, forward the loopback relay ports, or place the PC in router DMZ.

## Repository layout

```text
android/                  Android application and libraries
assets/                   Shared app icon source and generated Windows assets
installer/                Per-user Inno Setup definition
src/gateway/              Windows desktop, CLI, and Gateway Core projects
tests/gateway/            Gateway unit/integration test runner
docs/                     Product, security, test, release, and handover documents
protocol/                 Cross-platform wire-protocol specification
scripts/                  Reproducible build and packaging scripts
```

## Local build

Run `scripts/test-gateway.ps1`, `scripts/build-android.ps1`, `scripts/build-installer.ps1`, and `scripts/package-release.ps1`. Full prerequisites and output paths are in [`docs/개발_빌드_가이드.md`](docs/개발_빌드_가이드.md).

## License

Original RoamADB code is licensed under Apache License 2.0. QRCoder is MIT-licensed. Other package and SDK notices are recorded in [`THIRD_PARTY_LICENSES.md`](THIRD_PARTY_LICENSES.md).
