# RoamADB

RoamADB connects an Android phone away from home to the owner's Windows PC for on-demand remote ADB. The current recommended path uses the official Tailscale apps as the private network; RoamADB adds its own PC identity, phone registration, and local-only ADB relay.

## What is included

- **RoamADB for Android**: QR/manual PC registration, first-time Wireless ADB pairing relay, normal ADB relay, foreground notification, and Quick Settings tile.
- **RoamADB Gateway for Windows**: normal desktop window with diagnostics, start/stop, two-minute QR and six-digit registration code, registered-phone removal, `adb pair/connect/disconnect/devices`, and optional scrcpy launch.
- **Security boundary**: the Gateway listens on one exact Tailscale IPv4. ADB relay ports are exposed only on PC loopback (`127.0.0.1:47157` and `127.0.0.1:47158`) after phone-key authentication.

The program does not change router port forwarding, Windows Firewall, DMZ, Tailscale accounts, or Android's system Wireless debugging approvals.

## Download

The [`v0.1.1-spike` prerelease](https://github.com/fullmetalsonic/roam-adb/releases/tag/v0.1.1-spike) contains:

- `RoamADB-Gateway-Setup-0.1.1-spike.exe` — recommended per-user Windows installer;
- `RoamADB-Gateway-Portable-0.1.1-spike.exe` — no-install Windows x64 program;
- `RoamADB-0.1.1-spike-debug.apk` — Android 16 / API 36 or newer;
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

## Verification status

- Gateway .NET build and 14 unit/integration tests: PASS.
- Android unit tests, debug APK build, and lint: PASS.
- Actual Windows installer → installed GUI launch → normal close → uninstall: PASS.
- Actual local Tailscale address, registration countdown, and QR rendering in the Windows GUI: PASS.
- Fold8 Android 17 / One UI 9 external-network ADB end-to-end test: **field validation required**.

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
