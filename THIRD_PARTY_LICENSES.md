# Third-party components

The current technical spike resolves AndroidX, Jetpack Compose, Kotlin, Kotlin coroutines, Gradle, Android build tools, and .NET SDK/runtime components through their normal package/toolchain channels. Their license metadata remains in the downloaded packages and build outputs where provided.

The following researched projects are **not yet copied, vendored, or linked into this source tree**:

- Tailscale Android / libtailscale
- LADB
- AOSP adb native sources
- scrcpy

Before any of those components is added, its exact upstream commit, license hash, copied files, modifications, and binary notice location must be recorded in `docs/오픈소스_라이선스_대장.md`.
