# RoamADB

RoamADB is a secure remote Android Debug Bridge project for connecting an Android phone away from home to the user's own Windows PC.

## Components

- **RoamADB**: Android application that the phone owner explicitly turns on when remote debugging is needed.
- **RoamADB Gateway**: Windows program that authenticates the phone and exposes authenticated local-only ADB relay ports to development tools.

## Current status

This repository is in the third technical-spike phase. The implemented scope is intentionally narrow:

- loopback-only Gateway listener by default, plus an explicit exact-Tailscale-IP listener with `--tailnet`;
- self-signed Gateway identity stored in the Windows user certificate store;
- one-time registration code;
- registered phone public-key verification;
- challenge-response authentication;
- Android connection state, manually entered Wireless ADB connect port, and user-controlled ON/OFF;
- a Quick Settings tile and user-requested foreground service;
- an adaptive connection-bridge icon and backup-disabled app storage.
- an authenticated, byte-transparent phone-loopback ↔ Gateway-loopback TCP relay;
- loopback ADB endpoints that exist only while an authenticated phone publishes them.
- three Android connection choices, with the existing-Tailscale / ADB-only path as the current recommended mode;
- an Android VPN-transport preflight that never starts or replaces the external Tailscale app.

The raw relay passes its automated binary round-trip test. The self-contained Gateway has also passed an exact-tailnet bind and pinned-TLS status probe on the current PC. The app does not embed Tailscale yet: the current mode assumes the official Tailscale apps are already connected on phone and PC. Router port forwarding, automatic ADB discovery, first-pairing UI, and real-device ADB validation are not complete. Do not expose ADB port 5555 or place the PC in router DMZ.

## Download and try the spike

The [`v0.1.0-spike` pre-release](https://github.com/fullmetalsonic/roam-adb/releases/tag/v0.1.0-spike) contains:

- `RoamADB-0.1.0-spike-debug.apk` for Android 15 / API 35 or newer;
- `RoamADBGateway-0.1.0-spike-win-x64.exe`, a self-contained Windows x64 executable;
- `SHA256SUMS.txt` for download verification.

This is a technical-spike build, not a finished consumer release. On both devices, connect the official Tailscale app to the same tailnet first. Run `RoamADBGateway.exe register --tailnet` for initial registration, then use `run --tailnet` for normal sessions. Follow the detailed Korean guide in [`docs/기존_Tailscale_ADB_전용_모드.md`](docs/기존_Tailscale_ADB_전용_모드.md). The APK is debug-signed and the Fold8 external-network ADB path still requires real-device validation.

## Repository layout

```text
android/                  Android application and libraries
src/gateway/              RoamADB Gateway production projects
tests/gateway/            Gateway automated test runner
docs/                     Product, security, test, and build documents
protocol/                 Cross-platform wire-protocol specification
```

## Local build

The project-local .NET SDK is ignored by Git. See `docs/개발_빌드_가이드.md` and `docs/기존_Tailscale_ADB_전용_모드.md` for reproducible setup and verification commands. The current verified commands pass the Gateway 13/13 test suite and the Android `test assembleDebug lintDebug` build.

## License

Original RoamADB code is licensed under Apache License 2.0. Third-party components retain their own notices and license obligations.
