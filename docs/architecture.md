# Streaming_tool Architecture

## 목적

GameGuard가 적용된 PC 게임을 모바일에서 원격 플레이.

## 컴포넌트

```
┌──────────────────────┐         ┌──────────────────────┐
│ Mobile Web (web/)    │         │ Signaling (Mac mini) │
│ Next.js + React      │ <-WSS-> │ Node.js + ws         │
│ - Toolbar            │         │ - Room/peer routing  │
│ - VirtualJoystick    │         │ - SDP/ICE relay      │
│ - VirtualMousePad    │         └──────────────────────┘
│ - StreamViewer       │                  ^
│ - useConnection      │                  | WSS
└──────────┬───────────┘                  |
           |                              |
           | WebRTC (P2P)                 |
           |  - Video track (PC->Mobile)  |
           |  - DataChannel "input"       |
           v                              v
┌──────────────────────────────────────────────────────────┐
│ PC Client (pc-client/) — Windows, C# .NET 8 + WPF        │
│ ┌──────────────┐  ┌──────────────┐  ┌─────────────────┐  │
│ │ Capture      │  │ Encoder      │  │ Signaling Conn  │  │
│ │ DXGI Desktop │->│ NVENC/AMF/   │->│ SIPSorcery      │  │
│ │ Win.Graphics │  │ QuickSync    │  │ WebRTC peer     │  │
│ │ .Capture     │  │ (H.264/HEVC) │  └─────────────────┘  │
│ └──────────────┘  └──────────────┘                       │
│ ┌──────────────────────────────────────────────────────┐ │
│ │ Input Router (DataChannel -> Serial)                 │ │
│ │ - Receives JSON input events from mobile             │ │
│ │ - Translates to Pro Micro serial protocol            │ │
│ └────────────────────────┬─────────────────────────────┘ │
└───────────────────────────┼──────────────────────────────┘
                            | USB Serial (CDC, e.g. COM5)
                            v
                  ┌────────────────────┐
                  │ Pro Micro (firmware/)│
                  │ ATmega32U4           │
                  │ Acts as USB HID:     │
                  │ - Keyboard           │
                  │ - Mouse              │
                  └────────────────────┘
```

## 왜 이 구조

### Pro Micro로 입력
- GameGuard는 SendInput / PostMessage / keybd_event 등 소프트웨어 입력 주입을 탐지/밴.
- Pro Micro(ATmega32U4)는 OS 입장에서 **진짜 USB HID 키보드/마우스**라 차단 불가.
- PC 클라이언트는 Pro Micro에 시리얼로 명령만 전달 — 게임 프로세스에 일절 손대지 않음.

### Mac mini 시그널링
- WebRTC는 P2P지만 **첫 연결 시 SDP/ICE 교환을 위한 중계**가 필요.
- Mac mini에 Node.js + nginx + Let's Encrypt → `wss://signal.{domain}` 으로 항시 운영.
- 외부망에서도 도메인으로 접속 가능.
- 영상은 P2P로 직결되므로 Mac mini의 트래픽/지연 부담 없음.

### TURN
- 일부 NAT(Symmetric NAT, CGNAT)에서는 P2P 직결 실패. TURN 서버 경유 필요.
- 옵션 A: Mac mini에 coturn 직접 설치
- 옵션 B: 무료 TURN(metered.ca, Twilio) 사용
- Phase 5에서 결정

### 모바일 웹 (Next.js)
- 추가 설치 필요 없음 (앱스토어 거치지 않음)
- WebRTC 표준 API 사용 → iOS Safari / Android Chrome 모두 지원
- 기존 입력 UI(조이스틱/키패드) 재활용

## 데이터 흐름

### 영상 (PC -> Mobile)
1. PC 클라이언트가 `GraphicsCaptureItem`(특정 창) 또는 DXGI Desktop Duplication(전체 화면)으로 프레임 획득
2. NVENC/AMF/QSV로 H.264 인코딩 (저지연 프로파일)
3. SIPSorcery WebRTC로 RTP 전송
4. 모바일 `<video>` 요소가 수신/디코딩

### 입력 (Mobile -> PC -> Pro Micro)
1. 모바일에서 사용자가 조이스틱/키 누름
2. JSON 형식으로 WebRTC DataChannel("input")에 전송
3. PC 클라이언트가 수신하여 Pro Micro 시리얼 프로토콜로 변환
4. Pro Micro가 USB HID 리포트 발사 → 게임 PC OS는 진짜 키 입력으로 인식

## Phase 계획

| Phase | 산출물 |
|---|---|
| 1+2 | 시그널링 서버 + PC 클라이언트(전체 화면 캡처) + 모바일 영상 수신 |
| 3 | 프로세스/창 리스트 + 창 단위 캡처 |
| 4 | Pro Micro 펌웨어 + 시리얼 프로토콜 + DataChannel 입력 |
| 5 | 페어링 코드, 자동 재연결, TURN, 트레이 아이콘, 배포 |

## 보안

- 시그널링은 WSS만 (TLS).
- Room 코드(짧은 코드 또는 PSK)로 PC<->모바일 페어링.
- DataChannel 입력 메시지는 ROOM 인증된 피어에서만 수락.
- 절대 입력 명령을 IPC/공유 메모리/네트워크 소켓이 아닌 곳에서 받지 않음.
