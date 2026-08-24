# 기존 Tailscale 경유 · ADB 전용 모드

## 1. 현재 판정

이 모드는 2026-08-25 코드와 현재 Windows PC에서 구현·검증됐다. 휴대폰과 PC의 공식 Tailscale 앱이 이미 같은 tailnet에 연결됐다는 전제에서 RoamADB는 다음만 담당한다.

- Gateway 인증서 SHA-256 고정
- 1회용 코드로 휴대폰 등록
- 휴대폰 ECDSA challenge-response 인증
- PC `adb.exe`와 휴대폰 loopback 무선 ADB 사이의 투명 TCP 중계

RoamADB Android 앱은 `VpnService`를 실행하거나 Tailscale을 끄고 켜지 않는다. 따라서 공식 Tailscale 앱과 VPN 자리를 두고 충돌하지 않는다.

## 2. 보안 경계

```text
휴대폰 RoamADB
    │ TLS + 인증서 지문 고정 + 휴대폰 공개키 인증
    │ Tailscale 사설 경로
    ▼
PC Tailscale 단일 IPv4:47156  ← Gateway 인증 포트
    │
    └─ 127.0.0.1:47157       ← 인증된 휴대폰이 게시한 동안만 ADB connect
```

- `--tailnet`은 Windows Program Files에 설치된 공식 Tailscale CLI의 `tailscale ip -4` 결과 한 개를 읽고 그 주소에만 수신한다. Gateway 옆의 동명 실행파일이나 shell 경로는 사용하지 않는다.
- `0.0.0.0`, `::`, 일반 LAN 주소, 공인 주소는 tailnet 모드 보안 검사에서 거부한다.
- 기본 모드는 계속 `127.0.0.1` 전용이다.
- 공유기 포트포워딩·DMZ가 필요 없다.
- Windows 방화벽은 자동 변경하지 않는다. 다른 tailnet 장치에서 연결이 막히면 사용자 승인 아래 실행 파일·포트 최소 규칙을 별도로 검토한다.
- Tailscale 경로만 믿지 않고 Gateway 인증서 지문, 등록 휴대폰 키, Android 기본 ADB 승인을 함께 사용한다.

Tailscale 공식 CLI는 `tailscale ip -4`로 현재 장치의 tailnet IPv4를 반환한다. Tailscale 장치 주소는 안정적으로 유지되며 MagicDNS 이름도 사용할 수 있다. 앱 분할 터널링에서 RoamADB를 제외하면 tailnet 경로를 우회할 수 있으므로 제외하지 않아야 한다.

공식 근거:

- [Tailscale CLI와 `tailscale ip`](https://tailscale.com/docs/reference/tailscale-cli)
- [tailnet 장치 연결](https://tailscale.com/kb/1452/connect-to-devices)
- [MagicDNS](https://tailscale.com/docs/features/magicdns)
- [Android 앱 분할 터널링](https://tailscale.com/docs/features/client/android-app-split-tunneling)
- [Tailscale IPv4 대역](https://tailscale.com/docs/reference/ip-pool)

## 3. PC 최초 등록 실행

PC Tailscale이 연결된 상태에서 다음을 실행한다.

```powershell
.\RoamADBGateway.exe doctor
.\RoamADBGateway.exe register --tailnet
```

Gateway 화면에 다음이 표시된다.

- PC의 Tailscale IPv4와 포트 `47156`
- Gateway SHA-256 지문
- 2분 동안 한 번만 쓸 수 있는 6자리 등록 코드

Android 앱에서 `기존 VPN 경유 · ADB 전용 (권장)`을 선택하고 위 값을 입력한다. 등록 중 휴대폰 Tailscale을 켜 둔다. 앱은 활성 Android VPN 전송을 찾지 못하면 등록·연결을 중단하고 Tailscale 연결 및 split tunneling 제외 여부를 안내한다.

## 4. 평상시 사용

1. PC에서 Tailscale을 연결한다.
2. PC에서 `.\RoamADBGateway.exe run --tailnet`을 실행한다.
3. 휴대폰에서 Tailscale을 연결한다.
4. 휴대폰 개발자 옵션의 무선 디버깅 화면에서 현재 connect 포트를 RoamADB에 저장한다.
5. RoamADB 앱 또는 빠른 설정 타일로 ON 한다.
6. PC Gateway가 connect relay 준비를 표시하면 `adb connect 127.0.0.1:47157`을 실행한다.
7. 끝나면 RoamADB를 OFF하고 Gateway를 `Ctrl+C`로 종료한다.

Gateway 자체 상태는 다른 PC 창에서 확인할 수 있다.

```powershell
.\RoamADBGateway.exe status --tailnet
```

## 5. 아직 필요한 실기기 검증

- Fold8에서 외부 Wi-Fi/LTE를 사용한 휴대폰 앱 ↔ PC Gateway 등록·인증
- Fold8 local adbd connect 포트 연결
- PC `adb devices`, `adb shell`, `logcat`, APK 설치, 파일 왕복
- Android Studio·scrcpy·Codex ADB 사용
- 화면 잠금, Tailscale 재연결, Wi-Fi↔LTE 전환, 장시간 유지
- Windows 방화벽이 다른 tailnet 장치의 47156 연결을 허용하는지 확인

현재 PC 안에서 self-contained Gateway의 정확한 tailnet 주소 수신, pinned-TLS `status --tailnet`, 미인증 상태의 ADB relay 포트 폐쇄와 종료 후 전체 포트 폐쇄는 PASS다. 실제 휴대폰이 연결되지 않아 위 항목은 `현장 검증 필요`다.
