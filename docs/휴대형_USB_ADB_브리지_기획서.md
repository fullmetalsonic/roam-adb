# RoamADB — 휴대형 USB ADB 브리지 기획서

## 1. 문서 상태

- 계획 승인: 2026-08-25
- 현재 단계: 1차 실기기 스파이크·기본 보안 강화 완료, H-07 장시간 신뢰성·H-08 PC 도구 통합 진행 전
- 기준 휴대폰: Samsung Galaxy Z Fold8, Android 17, One UI 9
- 1차 브리지 후보: Raspberry Pi Zero 2 W
- 공개 범위: 공개 가능한 설계·합성 시험만 GitHub Public에 기록
- 구현 경계: Pi용 재현 설치·복구·안전 종료 스크립트까지 구현했다. Bridge Agent, APK/EXE 통합과 새 Release는 별도 단계다.

이 문서는 현재 공개된 `v0.1.2-spike` 무선 ADB 경로를 대체하거나 해당 릴리스가 USB 브리지를 지원한다고 주장하지 않는다. 새 경로의 모드 식별자는 `portable-usb-bridge`이며, Pi 스파이크는 실기기 PASS지만 기존 APK/EXE에는 아직 통합되지 않았다.

## 2. 배경과 문제 정의

현재 검증된 RoamADB 경로는 휴대폰의 Android 무선 디버깅 포트와 공식 Tailscale 앱을 중계한다. Fold8에서 USB 없는 ADB shell과 파일 왕복까지 성공했지만 다음 한계가 확인됐다.

1. Android 시스템 무선 디버깅은 휴대폰이 Wi-Fi 클라이언트로 연결돼 있어야 한다.
2. LTE/5G만 사용하는 이동 상태에서는 무선 디버깅 준비 조건을 충족하지 못한다.
3. 공용·외부 Wi-Fi에서 무선 ADB listener를 유지하는 것은 불필요한 공격면과 사용자의 보안 불안을 만든다.
4. 최초 설정에 Tailscale, PC 등록, Android 무선 페어링 포트·코드와 일반 연결 포트가 함께 등장해 초보자가 순서를 혼동하기 쉽다.

공유기 관리자 권한, 포트포워딩, VPN, Bluetooth와 일반 Android 앱은 Android Settings가 검사하는 Wi-Fi 전제조건을 제거하지 못한다. 주력 Fold8 루팅은 Samsung Knox와 데이터·보안 기능에 미치는 영향 때문에 기본안에서 제외한다.

## 3. 목표와 비목표

### 3.1 목표

- Fold8은 통신사 LTE/5G를 인터넷 기본 경로로 사용한다.
- Fold8 USB 테더링이 휴대형 브리지의 주 인터넷 경로를 제공한다.
- Fold8 자체 2.4GHz 핫스팟은 USB 테더링을 사용할 수 없을 때의 예비 경로로 유지한다.
- 브리지는 USB host이자 ADB host로 Fold8의 USB `adbd`에 직접 연결한다.
- 브리지 자체가 별도 Tailscale node로 집 PC와 암호화 통신한다.
- 집 PC와 Codex가 브리지의 ADB를 통해 Fold8을 진단한다.
- Android 무선 디버깅, 공용 Wi-Fi, 폰 루팅, 집 공유기 포트포워딩과 공인 ADB 포트를 사용하지 않는다.
- 처음 사용하는 사람이 화면과 예상 결과를 한 단계씩 확인하며 설치할 수 있게 한다.

### 3.2 1차 스파이크의 비목표

- 보조배터리 없는 USB 메모리형 완제품
- 자체 PCB, USB-C Power Delivery 회로와 내장 배터리 설계
- 다수 휴대폰 동시 연결
- 무인 부팅 후 영구 원격 디버깅
- 공용 인터넷에 ADB server `5037` 또는 `adbd 5555` 노출
- 기존 `v0.1.2-spike` APK·Gateway가 이미 브리지 모드를 지원한다는 표시
- 부품 도착 전 추측만으로 BUILD·DEVICE E2E를 PASS 처리

## 4. 전체 구조

```text
                         집 또는 원격 PC
                  adb client / Codex / 진단 도구
                                │
                   Tailscale + SSH 또는 인증 터널
                                │
                                ▼
┌──────────────── 휴대형 Linux ADB 브리지 ────────────────┐
│ Raspberry Pi Zero 2 W                                   │
│                                                        │
│ USB tether client ── Fold8 LTE/5G (주 경로)            │
│ Wi-Fi client ── Fold8 2.4 GHz hotspot (예비 경로)      │
│ Tailscale node                                          │
│ SSH 또는 후속 RoamADB Bridge Agent                      │
│ adb client/server + USB udev rule                       │
└──────────────────── USB host ───────────────────────────┘
                              │ 실제 USB ADB
                              ▼
┌──────────────── Samsung Fold8 ──────────────────────────┐
│ USB debugging ON / adbd / 사용자 1회 RSA 승인           │
│ Wireless debugging OFF                                  │
│ Mobile data ON / USB tethering 자동 / Hotspot 선택      │
└──────────────────────────────────────────────────────────┘
```

### 4.1 역할 분리

| 구성요소 | 역할 | 저장하는 신뢰 정보 |
|---|---|---|
| Fold8 | USB `adbd`, LTE/5G, USB 테더링, 예비 핫스팟 | 브리지 ADB RSA 공개키 승인 |
| 휴대형 브리지 | USB host, `adb` server, Tailscale node | 로컬 `adbkey`, Tailscale node state, SSH host key |
| 집 PC | 사용자 명령, Codex, 개발 도구 | SSH/Tailscale 사용자 신뢰, 선택적 RoamADB 등록 |
| Tailscale | 브리지와 집 PC의 사설 전송망 | 제품 외부의 공식 노드 인증 상태 |

Fold8의 Tailscale 앱은 이 경로의 데이터 운반에 필수가 아니다. 브리지가 Fold8 USB 테더링 또는 예비 핫스팟을 일반 인터넷 회선으로 사용해 자기 Tailscale node를 연결한다. 폰의 Tailscale은 다른 용도가 있을 때 독립적으로 켤 수 있지만 브리지 E2E에서는 의존하지 않는다.

## 5. USB와 전원 설계

### 5.1 USB 역할

- Fold8: USB device, `adbd` daemon
- Raspberry Pi: USB host, `adb` client/server
- Raspberry Pi의 `USB` OTG 포트: Fold8 데이터 연결
- Raspberry Pi의 `PWR IN` 포트: 별도 전원 입력

브리지가 USB host이므로 일반 USB 메모리처럼 폰에서 전원만 공급받는 구성을 기본으로 가정하지 않는다. USB host는 VBUS를 공급하는 역할도 가지므로 전원 방향을 확인하지 않은 Y 케이블과 역급전 구성은 사용하지 않는다.

### 5.2 1차 전원 원칙

- 현재 Raspberry Pi 설치 문서의 Zero 계열 권장값에 따라 5V 2.5A 이상 전원 또는 보조배터리를 사용한다.
- 전원은 `PWR IN`, 데이터는 `USB` 라벨 포트에 분리한다.
- 초기 시험에서는 Fold8 데이터 케이블을 먼저 연결하고 안정된 전원을 인가하는 순서를 검증한다.
- 저전압·재부팅·USB 재열거 증상을 로그로 구분한다.
- 임의 제작 케이블을 사용하지 않고 데이터 지원 여부와 커넥터 역할이 명시된 제품을 사용한다.

## 6. 최소 준비 부품

| 구분 | 최소 사양 | 비고 |
|---|---|---|
| SBC | Raspberry Pi Zero 2 W | Pico 2 W, 구형 Zero W와 구분 |
| 저장장치 | 신뢰 가능한 microSD 32GB, A1 이상 | 16GB도 가능하지만 32GB 권장 |
| 카드리더 | microSD를 Windows PC에서 기록 가능 | PC 내장 슬롯이 있으면 불필요 |
| USB host 어댑터 | Micro USB 수 → USB-A 암 OTG | 데이터·host 지원 명시 |
| 폰 데이터 케이블 | USB-A 수 → USB-C 수 | 충전 전용 제외 |
| Pi 전원 케이블 | Micro USB | `PWR IN`용 |
| 전원 | 5V 2.5A 이상 보조배터리·어댑터 | Fold8 USB 연결 여유 포함 |

케이스, 짧은 케이블과 방열판은 선택사항이다. 1차 headless 설치에는 GPIO header, 팬, 모니터, mini-HDMI, 키보드와 마우스가 필요하지 않다.

## 7. 네트워크 설계

### 7.1 Fold8 USB 테더링 — 주 경로

- Fold8과 Pi 사이의 같은 USB 연결에서 ADB와 RNDIS USB 테더링을 함께 사용한다.
- USB 장치 추가 `udev` 이벤트가 발생하면 최대 30초로 제한된 서비스가 승인된 ADB 장치를 확인하고 USB 테더링 기능을 자동 활성화한다. 상시 3초 polling timer는 사용하지 않는다.
- Pi의 USB 네트워크 기본 경로 metric은 100으로 유지한다.
- 최초 연결 휴대폰은 ADB RSA 승인 한 번이 필요하고, 통신사·제조사 정책으로 테더링이 차단되면 예비 핫스팟을 사용한다.

### 7.2 Fold8 핫스팟 — 예비 경로

- Zero 2 W는 2.4GHz Wi-Fi만 지원하므로 Fold8 핫스팟을 2.4GHz 또는 호환 모드로 설정한다.
- 핫스팟 SSID와 비밀번호는 Raspberry Pi Imager 또는 브리지 로컬 설정에만 입력하고 Git·로그·스크린샷에 기록하지 않는다.
- 핫스팟 자동 종료, 클라이언트 격리, 데이터 절약 모드와 절전 정책을 실제 Fold8에서 시험한다.
- 브리지가 인터넷을 사용해도 Fold8의 Android 무선 디버깅은 OFF로 유지한다.
- Pi의 Wi-Fi 기본 경로 metric은 600으로 유지해 USB 경로가 없을 때 자동으로 선택한다.

### 7.3 브리지 Tailscale

- 브리지는 휴대폰과 별개의 Tailscale node로 등록한다.
- 집 PC와 브리지 사이만 허용하는 최소 권한 정책을 목표로 한다.
- node key 만료를 끈 기기는 재인증 없이 유지되지만, 분실 시 관리 화면에서 즉시 제거한다.
- 직접 연결이 불가능해 DERP relay를 사용하더라도 ADB 데이터는 Tailscale 종단간 암호화 안에 있어야 한다.
- 브리지의 핫스팟 사설 IP, 공인 IP와 Tailscale IP를 공개 문서에 기록하지 않는다.

## 8. 원격 ADB 전달 단계

### 단계 A — 스파이크 기본 경로

집 PC가 Tailscale을 통해 브리지에 SSH로 접속하고 브리지 안에서 명령을 실행한다.

```text
집 PC ── Tailscale/SSH ──> 브리지의 adb ── USB ──> Fold8
```

이 단계는 원리를 가장 적은 코드로 검증한다. 공인망과 핫스팟에 ADB server 포트를 열지 않는다.

### 단계 B — PC 표준 ADB 호환 경로

브리지의 `adb` server는 loopback에만 유지하고 SSH local forwarding으로 PC loopback 포트에 전달하는 방식을 시험한다. PC `adb`가 `-H`와 `-P`로 전달 포트를 사용할 수 있는지 실제 버전으로 검증한다.

```text
PC 127.0.0.1:<임시포트>
      │ SSH local forwarding
      ▼
Bridge 127.0.0.1:5037 ── USB ── Fold8
```

Android Studio와 scrcpy가 원격 ADB server를 일관되게 사용할 수 있는지는 별도 호환성 게이트다. 실패하면 공인 포트를 열지 않고 단계 C로 전환한다.

### 단계 C — 제품형 Bridge Agent

기존 Gateway Core의 인증·상태·진단 계약을 재사용할 수 있는 Linux `RoamADB Bridge Agent`를 검토한다. Agent는 다음 조건을 만족해야 한다.

- Tailscale 단일 인터페이스 또는 인증된 outbound session만 사용
- PC와 브리지 상호 등록
- PC loopback에만 개발 도구용 ADB 접속점 제공
- 브리지에서 USB 연결·ADB 승인·전원·네트워크 상태 보고
- OFF 시 tunnel과 PC loopback 접속점 종료
- ADB 명령·파일·화면 내용 미수집

단계 A와 B의 실기기 증거가 없으면 단계 C 구현을 시작하지 않는다.

## 9. 인증과 보안 경계

| 계층 | 통제 | 실패 시 조치 |
|---|---|---|
| 1 | Tailscale node 인증과 최소 권한 | 브리지 SSH·Agent 접근 차단 |
| 2 | SSH 공개키 또는 후속 RoamADB 상호 등록 | 원격 명령·tunnel 거부 |
| 3 | Fold8의 Android USB ADB RSA 승인 | `unauthorized` 상태, 사용자 승인 요구 |

### 금지 기본값

- `adb tcpip 5555`
- `adb -a` 또는 ADB server `0.0.0.0:5037` 공개
- 핫스팟 인터페이스에 인증 없는 관리 API 공개
- 집 공유기 DMZ·공인 포트포워딩
- SSH 비밀번호만 사용하는 광범위한 접속
- 전체 명령을 허용하는 `NOPASSWD: ALL`
- Tailscale auth key, SSH private key와 `~/.android/adbkey`의 Git 커밋

### 회수

1. Tailscale 관리 화면에서 브리지 node를 제거한다.
2. 브리지 SSH 공개키 또는 RoamADB 등록을 제거한다.
3. Fold8 개발자 옵션에서 USB 디버깅 승인을 취소한다.
4. microSD를 폐기·양도할 때 안전하게 초기화한다.

microSD를 다시 기록하면 브리지 ADB 키가 바뀌어 Fold8에서 다시 승인할 수 있다. 1차 승인에서 `이 컴퓨터에서 항상 허용`을 선택하더라도 브리지 분실 시 위 회수 절차를 수행한다.

### 현재 Pi 보안 기본값

- SSH는 공개키만 허용하고 root·비밀번호 로그인을 거부한다.
- SSH는 Tailscale 또는 직접 USB 복구 인터페이스에서만 방화벽이 허용한다.
- ADB server는 `127.0.0.1:5037`에만 listen한다.
- Avahi와 Bluetooth는 사용하지 않으므로 비활성화한다.
- 일반 관리에는 sudo 비밀번호가 필요하고, 무암호 권한은 `roamadb-poweroff` 한 명령으로 제한한다.
- 모든 설치 전 파일은 `/var/backups/roamadb/<timestamp>`에 root 전용으로 보존한다.

## 10. 초보자용 설치 진행 계획

부품 도착 뒤 한 번에 모든 명령을 제공하지 않는다. 각 단계의 화면·예상 출력과 실패 복구를 확인하고 다음 단계로 넘어간다.

### H-00 부품 검사

- 보드가 Zero 2 W인지 확인한다.
- `PWR IN`과 `USB` 포트를 구분한다.
- OTG 어댑터와 폰 케이블이 데이터 지원인지 확인한다.
- 결과: 잘못된 모델·케이블·전원 위험이 없어야 한다.

### H-01 microSD 작성

- Windows에 Raspberry Pi Imager를 설치한다.
- Raspberry Pi OS Lite 64-bit를 선택한다.
- hostname, 시간대, 사용자, Wi-Fi와 SSH 공개키를 사전 설정한다.
- 실제 비밀번호와 핫스팟 자격증명은 저장소에 기록하지 않는다.
- 결과: Imager 검증을 통과한 bootable microSD가 생성돼야 한다.

### H-02 headless 첫 부팅

- Fold8 핫스팟 또는 안전한 2.4GHz 초기 설정망을 준비한다.
- microSD와 케이블을 확인한 뒤 전원을 넣는다.
- Windows에서 브리지의 네트워크 접속과 SSH host key를 확인한다.
- 결과: HDMI·키보드 없이 SSH 명령 1개가 성공해야 한다.

### H-03 OS와 패키지 준비

- OS 패키지를 업데이트한다.
- ARM용 `adb`, USB 규칙과 Tailscale을 설치한다.
- 실제 설치 버전을 기록하되 계정·주소·키는 공개하지 않는다.
- 결과: `adb version`, `tailscale status`와 서비스 상태가 정상이어야 한다.

### H-04 브리지 Tailscale 등록

- 브리지를 사용자 tailnet의 별도 node로 승인한다.
- 집 PC에서 브리지까지 암호화 통신과 SSH를 확인한다.
- 허용하지 않은 tailnet node의 접근을 거부하는 정책을 시험한다.
- 결과: 집 PC만 브리지 관리 경로에 접속해야 한다.

### H-05 Fold8 USB ADB 최초 승인

- Fold8의 USB 디버깅을 켜고 무선 디버깅은 끈다.
- 브리지 `USB` 포트와 Fold8을 데이터 케이블로 연결한다.
- Fold8에 나타나는 RSA 지문을 확인하고 1차 시험 장치로 승인한다.
- 결과: 브리지의 `adb devices -l`에서 Fold8이 `device`여야 한다. `unauthorized`와 케이블 미인식을 구분한다.

### H-06 집 PC 원격 ADB E2E

- 집 PC에서 Tailscale을 통해 브리지의 ADB 명령을 실행한다.
- model·Android 버전 조회, 합성 파일 push/pull과 SHA-256 왕복을 확인한다.
- USB 분리 시 명령이 실패하고 다시 연결하면 복구되는지 확인한다.
- 결과: LTE/5G USB 테더링 또는 예비 핫스팟을 통한 집 PC→브리지→USB ADB 왕복이 실제로 성공해야 한다.

### H-07 자동화와 복구

- 전원 인가 뒤 USB 테더링 자동화, Wi-Fi, Tailscale과 ADB 감시를 시작한다.
- USB 테더링 OFF/ON, 핫스팟 OFF/ON, LTE 전환, USB 분리·재연결과 브리지 재부팅을 시험한다.
- 휴대폰 USB를 먼저 연결한 상태에서 브리지 전원을 켜는 콜드부팅도 별도로 시험한다.
- 사용자가 브리지를 연결·전원 공급할 때만 원격 디버깅이 준비되는 수동 사용 원칙을 유지한다.
- 결과: 반복 실패가 배터리와 로그를 소모하지 않고 현재 차단 단계를 알려야 한다.

### H-08 PC 도구 통합

- SSH local forwarding과 표준 PC ADB를 시험한다.
- logcat, APK 설치·삭제, scrcpy와 Android Studio를 단계별로 검증한다.
- 실패하면 원인을 기록하고 Bridge Agent 구현 여부를 다시 승인받는다.

## 11. 상태 모델

| 상태 | 의미 | 사용자 행동 |
|---|---|---|
| `HARDWARE_REQUIRED` | 브리지 부품 없음 | 부품 준비 |
| `SD_SETUP_REQUIRED` | OS·SSH 미설정 | SD 설치 시작 |
| `BRIDGE_OFFLINE` | 전원 또는 부팅 안 됨 | 전원·microSD 확인 |
| `UPLINK_REQUIRED` | USB 테더링과 알려진 2.4GHz 핫스팟 모두 없음 | USB 연결·모바일 데이터·예비 핫스팟 확인 |
| `TAILSCALE_REQUIRED` | 브리지 사설망 미등록·끊김 | node 인증·망 확인 |
| `USB_DEVICE_MISSING` | Fold8 USB 미검출 | 포트·OTG·데이터 케이블 확인 |
| `ADB_AUTH_REQUIRED` | Android RSA 승인 전 | Fold8 화면에서 승인 |
| `BRIDGE_READY` | 네트워크와 USB ADB 준비 | 집 PC 접속 가능 |
| `PC_CONNECTED` | 집 PC가 실제 사용 중 | 디버깅 가능 |
| `RECOVERING` | USB 테더링·핫스팟·Tailscale 복구 중 | 대기 또는 중지 |
| `ERROR` | 자동 복구 불가 | 단계별 진단 실행 |

초기 스파이크에서는 CLI 출력으로 상태를 판정한다. 실제 증거 뒤 Android 앱과 Windows Gateway가 같은 번호·상태·다음 행동을 표시하는 UI를 별도 설계한다.

## 12. 시험 게이트

### Gate 1 — 전원·USB

- 안정된 전원으로 Pi 부팅
- Fold8 USB enumeration
- ADB RSA 승인과 재연결
- 잘못된 포트·충전 전용 케이블 오류 구분

### Gate 2 — USB 테더링·핫스팟·Tailscale

- Fold8 LTE/5G 인터넷 유지
- Pi USB 테더링 자동 활성화와 주 경로 선택
- Pi 2.4GHz 핫스팟 자동 접속
- Pi Tailscale node 온라인
- 집 PC에서만 SSH 가능

### Gate 3 — 원격 ADB

- USB ADB `device`
- 원격 shell·logcat
- 합성 파일 push/pull SHA-256 일치
- USB·핫스팟 분리 뒤 접근 실패

### Gate 4 — 신뢰성·사용성

- 화면 잠금
- 핫스팟 자동 종료 정책
- USB 20회 재연결
- 브리지 10회 전원 재시작
- 30분·4시간 연결 유지, 발열·배터리·데이터 사용량 기록
- 초보자가 문서만 보고 현재 단계와 다음 행동을 찾는지 확인

### Gate 5 — PC 도구

- PC 표준 `adb` 원격 server 사용
- APK 설치·삭제
- scrcpy
- Android Studio
- Codex 실제 진단 명령과 결과 회수

실행하지 않은 Gate는 `NOT RUN`, 부품이 없어 수행할 수 없는 시험은 `HARDWARE REQUIRED`로 표시한다.

## 13. 알려진 위험과 대응

| 위험 | 영향 | 초기 대응 |
|---|---|---|
| Zero 2 W가 5GHz 핫스팟을 못 봄 | 부팅 후 오프라인 | Fold8 2.4GHz·호환 모드 사용 |
| USB host 전원 부족·역급전 | 재부팅·장치 손상 가능 | 별도 안정 전원, 임의 Y 케이블 금지 |
| USB 테더링을 지원하지 않는 폰·통신사 | 주 경로 사용 불가 | 2.4GHz 핫스팟 예비 경로 사용 |
| 핫스팟 자동 종료 | 예비 경로 사용 불가 | USB 테더링 우선, 절전 설정 확인 |
| microSD 손상 | 부팅 실패·키 손실 | 정상 종료, 설정 백업과 재작성 절차 |
| 브리지 분실 | Tailscale·ADB 신뢰 유출 | node 제거, SSH 키 폐기, ADB 승인 취소 |
| ADB server 외부 bind | 핫스팟 내부 공격면 | loopback 강제와 socket 검사 |
| Android Studio 원격 server 비호환 | IDE 사용 불가 | SSH 기본 경로 검증 뒤 Agent 게이트 |
| 보조배터리 소모·폰 충전 전류 | 휴대성 저하 | 실측 뒤 전원 설계 재평가 |
| 선연결 콜드부팅에서 USB 장치 미열거 | 재연결 전까지 오프라인 | Zero 2 W에 `dwc2,dr_mode=host` 적용, 반복 재부팅 시험 |

## 14. 공식 기술 근거

- [Raspberry Pi Zero 2 W 제품 사양](https://www.raspberrypi.com/products/raspberry-pi-zero-2-w/)
- [Raspberry Pi Zero USB·전원·headless 설치 문서](https://www.raspberrypi.com/documentation/computers/getting-started.html)
- [Raspberry Pi 공식 USB OTG 안내](https://pip-assets.raspberrypi.com/categories/685-app-notes-guides-whitepapers/documents/RP-009276-WP/Using-OTG-mode-on-Raspberry-Pi-SBCs)
- [Raspberry Pi 공식 Device Tree overlay 목록](https://github.com/raspberrypi/firmware/blob/master/boot/overlays/README)
- [Tailscale Linux 설치 문서](https://tailscale.com/docs/install/linux)
- [Debian ARM64 ADB 패키지](https://packages.debian.org/trixie/arm64/adb)
- [Android Debug Bridge 공식 문서](https://developer.android.com/tools/adb)

## 15. 현재 판정과 다음 재개점

| 항목 | 판정 |
|---|---|
| 아키텍처 타당성 | 문서 검토 PASS |
| Zero 2 W USB OTG·2.4GHz | 실기기 PASS |
| ARM용 ADB 패키지 | 설치·service PASS |
| Fold8 USB 테더링 + Pi Tailscale | 실기기 PASS |
| Fold8 핫스팟 예비 경로 | 실기기 PASS |
| Fold8 USB ADB + Pi | 실기기 PASS |
| PC의 Tailscale 주소 경유 ADB shell·파일 왕복 | 현재 시험망 PASS |
| 지리적으로 분리된 집 PC·외부 휴대폰 | NOT RUN |
| 자동 테더링·재부팅 복구 | 실기기 PASS |
| 실제 케이블 재연결 | 실기기 PASS |
| 휴대폰 케이블 선연결 콜드부팅 | `dwc2` 적용 후 3회 연속 실기기 PASS |
| 장시간·다회 신뢰성 | NOT RUN |
| Bridge Agent·PC 표준 ADB 도구 통합 | NOT IMPLEMENTED / NOT RUN |
| 새 APK·EXE·Release | NOT CREATED |

상세 증거는 `휴대형_USB_ADB_브리지_실기기_검증_2026-09-01.md`에 기록한다. 다음 재개점은 H-07 장시간·다회 신뢰성 시험이며, 그 뒤 H-08 PC 도구 통합을 별도로 승인·구현한다.

## 16. 저장소와 브랜치 운영 결정

- 현재는 Android 앱, Windows Gateway와 휴대형 USB ADB 브리지를 공개 저장소 [`fullmetalsonic/roam-adb`](https://github.com/fullmetalsonic/roam-adb) 안에서 함께 관리한다.
- 휴대형 브리지는 RoamADB의 새로운 전송 경로이며 인증, 상태 모델, PC 도구, 보안 기준과 시험 문서를 공유하므로 아직 별도 저장소로 분리하지 않는다.
- 부품 도착 후 실제 구현은 기능 브랜치 `codex/portable-usb-adb-bridge`에서 진행한다. H-00~H-08의 관련 게이트와 보안 검토를 통과하기 전에는 `main`의 기존 무선 경로나 공개 Release가 USB 브리지를 지원한다고 표시하지 않는다.
- 다음 중 하나가 현실화되면 `roam-adb-bridge` 같은 별도 저장소 분리를 다시 검토한다: 브리지 전용 OS 이미지·설치 프로그램을 독립 배포하는 경우, 앱·Gateway와 다른 버전·릴리스 주기가 필요한 경우, 브리지 단독 사용자와 유지보수 주체가 생기는 경우.
- 분리할 때에는 관련 코드만 이동하고 이 문서, 프로토콜 계약, 보안 기준과 시험 증거의 원본 위치·연결 관계를 두 저장소에 남긴다.
