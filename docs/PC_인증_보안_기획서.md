# RoamADB Gateway — PC 인증·보안 기획서

## 1. 문서 상태

- 상태: v0.1.2 등록·ECDSA·ADB relay 실기기 통과 + 휴대형 USB 브리지 보안 목표안
- 실제 인증 구현: 자동 통합시험과 Fold8 USB 없는 E2E PASS, 침투시험·휴대형 브리지 E2E 미실시
- GitHub 소유자: `fullmetalsonic`
- Windows 프로그램: `RoamADB Gateway`

이 문서의 기존 `PC Helper` 또는 `Helper` 표현도 확정 제품명 `RoamADB Gateway`를 뜻한다.

## 2. 목표

- 인터넷의 아무 PC나 휴대폰 ADB에 접근하지 못하게 한다.
- 같은 Tailscale 계정에 들어온 다른 장치도 휴대폰에서 허용하기 전에는 relay를 사용하지 못하게 한다.
- 일반 사용자는 최초 한 번 QR 스캔 또는 6자리 코드 확인으로 집 PC를 등록한다.
- 개인 키를 QR, 로그, GitHub와 휴대폰·PC 사이에서 복사하지 않는다.
- PC를 분실하거나 교체하면 휴대폰에서 즉시 차단할 수 있다.

## 3. 3중 인증 구조

| 계층 | 담당 | 차단 대상 |
|---|---|---|
| 1. 사설망 장치 인증 | Tailscale 노드 키·WireGuard | 인터넷의 비가입 장치 |
| 2. RoamADB PC 허용 | Tailscale 안정 장치 신원 + 휴대폰 승인 | 같은 tailnet의 미등록 장치 |
| 3. Android 기본 ADB 인증 | Android 무선 페어링·TLS 또는 ADB RSA 키 | relay를 통과했지만 Android ADB 승인이 없는 PC |

세 계층 중 하나라도 실패하면 ADB 세션을 열지 않는다.

휴대형 USB 브리지는 같은 원칙을 다음처럼 적용한다.

| 계층 | 담당 | 차단 대상 |
|---|---|---|
| 1. 사설망 장치 인증 | 브리지와 집 PC의 Tailscale node | 인터넷·tailnet 밖 장치 |
| 2. 브리지 관리 인증 | SSH 공개키 또는 후속 RoamADB Bridge Agent 등록 | 같은 tailnet의 미허용 장치 |
| 3. Android 기본 ADB 인증 | Fold8의 USB ADB RSA 승인 | 브리지를 확보했지만 폰 승인이 없는 host |

## 4. PC에 설치할 항목

### 1차 버전

1. 공식 Tailscale Windows 클라이언트
2. 공식 Android Platform-Tools의 `adb.exe`
3. RoamADB Windows Helper
4. 화면 제어가 필요하면 scrcpy

RoamADB Helper는 관리자 권한 없이 실행하고 기존 Tailscale·ADB 설치를 탐지한다. 자동으로 Windows Defender 예외를 만들거나 광범위한 방화벽 규칙을 추가하지 않는다.

### 향후 후보

PC Helper에 Tailscale userspace 네트워크를 내장하여 PC 측도 한 프로그램으로 줄일 수 있다. 1차 버전은 이미 사용 중인 공식 Tailscale을 재사용하여 위험과 빌드 범위를 줄인다.

## 5. 최초 PC 등록 흐름

```text
[집 PC Helper]                         [휴대폰 RoamADB]
등록 시작                              PC 추가
    │                                      │
    ├─ Tailscale 장치 신원 확인             │
    ├─ 2분 유효 일회용 요청 생성            │
    ├─ QR + 6자리 코드 표시 ───────────────>│
    │                                      ├─ QR 스캔 또는 코드 입력
    │<──────── tailnet 안에서 요청 확인 ────┤
    │                                      ├─ Tailscale LocalAPI로
    │                                      │  PC 장치 신원 확인
    │                                      ├─ PC 이름·계정·코드 표시
    │                                      └─ 사용자가 `이 PC 허용`
    │<──────── 등록 성공 ───────────────────┤
    └─ 로컬 ADB 연결 시험                   └─ Android ADB 페어링·승인 안내
```

### 사용자 화면

휴대폰에는 다음을 표시한다.

- PC 표시 이름
- Tailscale 호스트 이름
- Tailscale 사용자 또는 태그
- 안정 장치 ID 일부
- 양쪽에 동일한 6자리 확인 코드
- `이 PC 허용`과 `거부`

일회용 코드는 편의상 요청을 맞춰보는 용도이며 실제 네트워크 신원은 Tailscale LocalAPI가 확인한 소스 장치 신원을 기준으로 한다.

## 6. 저장 데이터

### 휴대폰

- 허용한 PC의 Tailscale 안정 장치 ID 후보
- 표시 이름
- 최초 승인 시각과 마지막 접속 시각
- 선택적 Helper 공개 정보

### PC

- 허용된 휴대폰의 Tailscale 안정 장치 ID 후보
- Helper 설정과 ADB 경로
- 일반 로그에 포함하지 않는 로컬 세션 정보

### 저장하지 않는 것

- Tailscale 개인 노드 키 사본
- ADB 개인 키의 휴대폰 복사본
- QR 일회용 코드의 영구 저장
- Tailscale 로그인 토큰
- ADB 명령·파일·화면 내용

Tailscale 안정 장치 ID의 실제 필드와 재인증 후 유지 여부는 기술 스파이크에서 LocalAPI 응답을 확인한 뒤 확정한다.

## 7. 평상시 연결 흐름

1. 사용자가 휴대폰 앱 또는 빠른 설정 타일을 ON으로 한다.
2. 휴대폰이 `원격 디버깅 준비됨` 상태가 된다.
3. 집 PC Helper가 휴대폰을 찾는다.
4. 휴대폰 relay가 접속 소스의 Tailscale 신원을 조회한다.
5. 허용 목록과 일치하면 Helper 세션을 연다.
6. Helper가 PC의 `127.0.0.1`에 ADB 접속점을 만든다.
7. 표준 PC ADB가 그 로컬 접속점으로 연결한다.
8. Android 버전과 relay 방식에 따라 최초 한 번 표준 무선 ADB 페어링 또는 ADB 키 승인을 완료한다.
9. 이후 Android Studio, scrcpy와 Codex는 표준 ADB 장치를 사용한다.

PC가 꺼져 있으면 휴대폰은 `READY`, PC가 연결되면 `PC_CONNECTED`로 표시한다.

## 8. Tailscale 접근 제어

- Tailscale 노드 키는 장치를 암호학적으로 식별하고 트래픽을 암호화한다.
- Tailscale Grants를 사용할 수 있으면 집 PC만 휴대폰 relay 포트에 접근하도록 최소 권한 정책을 제공한다.
- 앱은 접속 소스 IP만 믿지 않고 LocalAPI로 장치·사용자 신원을 조회한다.
- 개인 tailnet 정책이 넓게 열려 있어도 앱 relay 허용 목록이 두 번째 차단층으로 동작해야 한다.
- 고급 사용자는 Tailscale Device Approval 또는 Tailnet Lock을 선택할 수 있지만 1차 필수 설정으로 강제하지 않는다.

실제 Grants 예시는 휴대폰·PC의 태그와 안정 장치 식별 방식을 기술 스파이크에서 확인한 뒤 생성한다.

## 9. Android 기본 ADB 인증

- PC의 공식 `adb`가 자체 RSA 키를 사용한다.
- Android 16·17과 선택한 relay 방식에 따라 시스템 무선 페어링 또는 ADB 키 승인 흐름을 사용한다.
- RoamADB는 Android 시스템 ADB 개인 키를 복사하거나 자체 서버로 전송하지 않는다.
- RoamADB가 직접 관리하는 것은 relay 허용 PC 목록이다.
- Android ADB 승인을 전부 취소하려면 개발자 옵션의 승인 취소 화면으로 안내한다.

## 10. PC 차단·분실 대응

### 휴대폰 앱에서 PC 삭제

- 해당 Tailscale 안정 장치 ID의 relay 접속을 즉시 거부한다.
- 진행 중인 해당 PC 세션을 종료한다.

### Tailscale 관리 화면에서 장치 제거

- PC 노드 키가 tailnet에서 취소되어 사설망 접속 자체가 차단된다.

### Android ADB 승인 취소

- 개발자 옵션에서 모든 ADB 승인을 취소하면 기존 PC `adb` 키가 거부된다.

강한 회수가 필요하면 세 단계를 모두 수행한다.

## 11. 공식 Tailscale 앱과 RoamADB의 관계

현재 구현한 `기존 VPN 경유 · ADB 전용`에서는 공식 Tailscale 앱을 사용자가 먼저 연결하고 RoamADB를 동시에 사용한다.

- RoamADB는 자체 `VpnService`를 실행하지 않는다.
- Tailscale 로그인·연결·종료와 다른 앱의 tailnet 경로는 공식 Tailscale 앱이 담당한다.
- RoamADB는 활성 VPN transport를 확인하고 Gateway 인증·ADB relay만 담당한다.
- RoamADB OFF는 Tailscale을 끄지 않는다.
- split tunneling에서 RoamADB를 제외하면 Gateway 경로가 실패할 수 있으므로 제외하지 않는다.

후속 `내장형 보안망` 원툴 모드를 구현할 때는 Android의 활성 VPN 한 개 제한 때문에 공식 Tailscale과 전환 정책이 다시 필요하다. 현재 빌드에서 이 모드는 계획 상태로 실행을 차단한다.

## 12. 공격 시나리오와 차단

| 시나리오 | 예상 차단 |
|---|---|
| 인터넷에서 휴대폰 공인 IP 스캔 | relay가 tailnet 밖에 열리지 않음 |
| 같은 Wi-Fi의 낯선 PC | 물리 인터페이스 비노출 |
| 같은 tailnet의 미등록 장치 | relay 허용 목록 거부 |
| 등록 PC 이름만 위조 | LocalAPI의 실제 Tailscale 신원 불일치 |
| QR 사진 재사용 | 2분 만료·1회 사용·신원 일치 검사 |
| 허용 PC 탈취 | 휴대폰 허용 삭제 + tailnet 장치 제거 + ADB 승인 취소 |
| 앱 OFF 후 재접속 | relay·VPN·세션 종료로 거부 |
| 브리지 `5037` 핫스팟 스캔 | loopback bind와 socket 검사로 접근 불가 |
| 브리지 microSD·기기 분실 | tailnet node 제거 + SSH 키 폐기 + Fold8 ADB 승인 취소 |
| 충전 전용 케이블을 데이터 케이블로 오인 | USB 미검출로 분리 진단, 네트워크 복구로 오판하지 않음 |

## 13. 기술 스파이크 검증 항목

- Windows Tailscale LocalAPI에서 PC 안정 신원 취득
- Android 내장 libtailscale에서 접속 소스 `WhoIs` 또는 동등 조회
- 재인증·노드 키 회전 후 저장한 장치 신원 유지
- QR·6자리 코드 만료와 1회 사용
- 허용 목록 미등록 PC 거부
- 등록 PC 연결과 Android 기본 ADB 페어링·키 승인
- 허용 삭제 즉시 세션 종료
- Grants 적용 전·후 접근 차이
- 공식 Tailscale 선연결 → RoamADB 등록·ON
- Tailscale OFF·split tunneling 제외 상태의 실패 안내

## 14. 휴대형 USB 브리지 보안 경계

`portable-usb-bridge`의 상세 구조와 하드웨어 절차는 `휴대형_USB_ADB_브리지_기획서.md`를 따른다.

- Fold8 무선 디버깅은 OFF로 유지하고 USB `adbd`만 사용한다.
- 브리지는 Fold8 핫스팟을 인터넷 uplink로만 사용하고 별도 Tailscale node로 등록한다.
- 초기 원격 관리는 Tailscale로 제한된 SSH 공개키 인증을 사용한다.
- 브리지 ADB server는 loopback에만 두고 `adb -a`, 공인 `5037`, `adb tcpip 5555`를 금지한다.
- hotspot SSID·비밀번호, Tailscale state, SSH private key와 ADB private key를 Git·진단 보고서·QR에 넣지 않는다.
- microSD 재작성 시 ADB host key가 바뀐다는 점을 안내하고 Fold8에서 다시 승인한다.
- 브리지 분실 시 Tailscale node 제거, SSH·Agent 등록 회수와 Fold8 USB 디버깅 승인 취소를 함께 수행한다.
- Bridge Agent는 전원·USB·네트워크 Gate 통과 전 구현하지 않는다.

출시 전에는 브리지의 모든 listening socket을 수집해 loopback 또는 승인한 Tailscale 단일 주소 이외 bind가 0건인지 확인한다. 핫스팟의 다른 클라이언트, 미허용 tailnet node와 Tailscale 밖 장치에서 접근 실패를 실제로 확인한다.

## 15. 남은 사용자 선택

- QR 스캔을 위해 카메라 권한을 허용할지, 6자리 코드만 사용할지
- PC 등록 요청을 휴대폰에서 시작할지 PC에서 시작할지
- Tailscale Device Approval을 기본 안내에 포함할지
- 집 PC 외 추가 PC를 여러 대 허용할지
- 브리지 관리를 Tailscale SSH로 유지할지 RoamADB Bridge Agent로 전환할지
