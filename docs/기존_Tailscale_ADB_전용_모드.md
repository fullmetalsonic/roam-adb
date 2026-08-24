# 기존 Tailscale 경유 · ADB 전용 모드

## 1. 사용 전제

PC와 휴대폰의 공식 Tailscale 앱이 같은 tailnet에 이미 연결돼 있어야 한다. RoamADB는 Tailscale을 켜거나 계정에 로그인하지 않고, 그 사설 경로 위에서 다음을 담당한다.

- Gateway 인증서 SHA-256 고정
- 2분·1회용 코드와 QR로 휴대폰 등록
- 휴대폰 ECDSA challenge-response 인증
- PC loopback과 휴대폰 loopback 무선 ADB 사이의 투명 TCP 중계

```text
휴대폰 RoamADB
    │ TLS + 인증서 지문 고정 + 휴대폰 공개키 인증
    │ 공식 Tailscale 사설 경로
    ▼
PC의 정확한 Tailscale IPv4:47156
    ├─ 127.0.0.1:47157  ADB connect 중계
    └─ 127.0.0.1:47158  최초 pairing 중계
```

두 ADB 포트는 인증된 휴대폰이 중계를 게시한 동안만 PC loopback에 열린다. 공유기 포트포워딩·DMZ는 필요 없고 Windows 방화벽은 자동 변경하지 않는다.

## 2. PC와 휴대폰 등록

1. PC Tailscale을 연결한다.
2. Windows에서 **RoamADB Gateway**를 연다. 콘솔이 아니라 상태 창이 계속 보여야 한다.
3. **Gateway 켜기**를 누른다.
4. 화면에 `100.64.0.0/10` 범위의 PC 주소가 표시되는지 확인한다.
5. **새 등록 코드와 QR 만들기**를 누른다.
6. 휴대폰 Tailscale을 연결한 뒤 RoamADB를 연다.
7. 연결 방법은 **기존 VPN 경유 · ADB 전용 (권장)**을 선택한다.
8. **PC 등록 QR 스캔**을 누른다. RoamADB 자체는 카메라 권한을 요청하지 않고 Google Play 서비스 스캐너를 그때만 연다.
9. 스캐너를 못 쓰면 Windows 화면의 주소, 포트, 지문, 6자리 코드를 수동 입력한다.
10. 등록이 끝나면 Windows의 코드와 QR이 폐기되고 등록된 휴대폰 탭에 기기가 나타난다.

QR은 주소, 공개 인증서 지문, 일회용 코드, 만료 시각만 포함한다. 개인 키와 Tailscale 토큰은 포함하지 않는다.

## 3. 최초 1회 ADB 페어링

Android가 이 PC의 ADB 키를 아직 승인하지 않았다면 한 번 수행한다.

1. 휴대폰 **개발자 옵션 → 무선 디버깅 → 페어링 코드로 기기 페어링**을 연다.
2. 화면의 일시 페어링 포트를 RoamADB **최초 1회 무선 ADB 페어링** 카드에 입력한다.
3. **페어링 중계 열기**를 누른다.
4. PC Gateway의 최근 상태가 `페어링 중계 준비 ... 127.0.0.1:47158`인지 확인한다.
5. Windows **ADB 작업** 탭에서 Android 화면의 6자리 페어링 코드를 입력하고 **ADB 페어링**을 누른다.
6. 성공 결과를 확인한 뒤 휴대폰의 페어링 중계를 중지한다.

일시 페어링 포트와 6자리 Android 페어링 코드는 저장하지 않는다. 이 단계는 RoamADB PC 등록 코드와 다른 Android 시스템 절차다.

## 4. 평상시 원격 ADB

1. 휴대폰 무선 디버깅 화면의 일반 연결 포트를 RoamADB **무선 ADB 연결점** 카드에 저장한다. 페어링 포트가 아니다.
2. PC와 휴대폰 Tailscale을 연결한다.
3. PC Gateway를 켠다.
4. 휴대폰 RoamADB 또는 빠른 설정 타일로 원격 디버깅을 켠다.
5. PC Gateway 최근 상태가 `연결 중계 준비 ... 127.0.0.1:47157`인지 확인한다.
6. Windows **ADB 작업 → ADB 연결**을 누른다.
7. **기기 목록 새로고침**으로 `127.0.0.1:47157 device`를 확인한다.
8. 필요하면 **scrcpy 열기**를 누른다. scrcpy가 PATH나 Gateway 옆 `scrcpy` 폴더에 있어야 한다.
9. 끝나면 **ADB 연결 해제**, 휴대폰 RoamADB OFF, PC Gateway OFF 순으로 끈다.

## 5. 오류 확인

- **Tailscale을 찾지 못함**: PC 공식 Tailscale 설치와 연결을 확인한다.
- **활성 VPN 없음**: 휴대폰 Tailscale을 먼저 연결하고 split tunneling에서 RoamADB를 제외하지 않는다.
- **등록 코드 만료**: PC에서 새 코드와 QR을 만든다.
- **adb.exe를 찾지 못함**: Android SDK Platform-Tools를 설치한다.
- **pair/connect 포트 연결 실패**: Android 무선 디버깅 화면을 다시 열고 현재 포트를 확인한다. Android가 포트를 바꿀 수 있다.
- **Gateway 47156 접근 차단**: 자동 방화벽 변경은 하지 않는다. 같은 tailnet의 휴대폰 실기기 시험 후 필요한 최소 Windows 규칙만 별도로 검토한다.

## 6. 아직 필요한 현장 검증

- Fold8 Android 17 / One UI 9의 실제 QR 스캔과 등록
- LTE/외부 Wi-Fi에서 pairing과 `adb connect`
- `adb shell`, `logcat`, APK 설치, 파일 왕복, Android Studio, scrcpy
- 화면 잠금, Wi-Fi↔LTE, Tailscale 재연결, 장시간 유지
- Android 16 / One UI 8 태블릿 호환성

현재 Windows GUI와 설치본, 로컬 Tailscale 주소, QR, 프로토콜 자동시험은 PASS다. 위 실기기 항목은 검증 전이므로 완료로 보지 않는다.
