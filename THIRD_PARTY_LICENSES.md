# Third-party components

The current technical spike resolves AndroidX, Jetpack Compose, Kotlin, Kotlin coroutines, Gradle, Android build tools, and .NET SDK/runtime components through their normal package/toolchain channels. Their license metadata remains in the downloaded packages and build outputs where provided.

## Linked runtime packages

- QRCoder 1.8.0 — MIT License — https://github.com/Shane32/QRCoder
- Google Play services Code Scanner 16.1.0 — Google Play services SDK terms; the scanner implementation is supplied by Google Play services and is not vendored in this repository — https://developers.google.com/ml-kit/vision/barcode-scanning/code-scanner

QRCoder is linked into the Windows Gateway binary to render the registration QR. The Android app calls Google Code Scanner only after the user presses the scan button and does not request Android camera permission.

### QRCoder MIT License

```text
The MIT License (MIT)

Copyright (c) 2013-2025 Raffael Herrmann
Copyright (c) 2024-2025 Shane Krueger

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## Build-only packaging tool

- Inno Setup 6.7.3 — Inno Setup License — https://jrsoftware.org/

Inno Setup is used only to build the Windows Setup executable; its IDE and compiler are not redistributed by this repository. The generated installer retains the compiler-provided copyright and website strings. This public spike is non-commercial; commercial builders must review Inno Setup's current commercial-license policy.

The following researched projects are **not yet copied, vendored, or linked into this source tree**:

- Tailscale Android / libtailscale
- LADB
- AOSP adb native sources
- scrcpy

Before any of those components is added, its exact upstream commit, license hash, copied files, modifications, and binary notice location must be recorded in `docs/오픈소스_라이선스_대장.md`.
