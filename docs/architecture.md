# Streaming_tool Architecture

## 목적

GameGuard가 적용된 PC 게임을 모바일에서 원격 플레이.

## 컴포넌트 (외부 서버 없음)

```
┌──────────────────────┐
│ Mobile Web (web/)    │
│ Next.js + React      │
│ - Toolbar            │
│ - VirtualJoystick    │
│ - VirtualMousePad    │
│ - StreamViewer       │
│ - useWebRtcViewer    │
└──────────┬───────────┘
           │
           │ ws://<PC IP>:<port>/ws  (signaling — same socket also relays SDP/ICE)
           │ + WebRTC P2P (video track + DataChannel "input")
           │
           ▼
┌──────────────────────────────────────────────────────────┐
│ PC Client (pc-client/) — Windows, C# .NET 8 + WPF        │
│ ┌──────────────────────────────────────────────────────┐ │
│ │ EmbeddedSignalingServer (Kestrel, /ws + /health)     │ │
│ │ - Validates room code, accepts viewer sockets        │ │
│ │ - Hands ViewerSession to the WebRTC peer in-process  │ │
│ └──────────────────────────────────────────────────────┘ │
│ ┌──────────────┐  ┌──────────────┐  ┌─────────────────┐  │
│ │ Capture      │  │ Encoder      │  │ WebRTC Peer     │  │
│ │ DXGI Desktop │->│ NVENC/AMF/   │->│ SIPSorcery       │ │
│ │ Win.Graphics │  │ QuickSync    │  │ (per viewer)    │  │
│ │ .Capture     │  │ (H.264/HEVC) │  └─────────────────┘  │
│ └──────────────┘  └──────────────┘                       │
│ ┌──────────────────────────────────────────────────────┐ │
│ │ Input Router (DataChannel -> Serial)                 │ │
│ │ - Receives JSON input events from mobile             │ │
│ │ - Translates to Pro Micro serial protocol            │ │
│ └────────────────────────┬─────────────────────────────┘ │
└───────────────────────────┼──────────────────────────────┘
                            │ USB Serial (CDC, e.g. COM5)
                            ▼
                  ┌────────────────────┐
                  │ Pro Micro            │
                  │ (firmware/)          │
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

### 임베디드 시그널링 (외부 서버 없음)
- 모바일 viewer가 PC에 직접 `ws://...` 접속해서 SDP/ICE 교환.
- 시그널링과 WebRTC peer가 같은 프로세스에 살기 때문에 메시지 라우팅이 직결되어 단순.
- 외부 서비스/도메인/TLS 의존성 없음 — 누구나 그냥 실행만 하면 됨.

### 외부 접속 (포트포워딩 + IP)
- 같은 Wi-Fi에서는 PC의 LAN IP로 즉시 접속.
- 외부망(LTE 등)에서는 공유기에서 listen 포트(기본 8080)를 PC IP로 forward.
- 모바일에선 `ws://<공인 IP>:8080/ws` 형태로 접속.
- 공인 IP가 자주 바뀌면 추후 DDNS(DuckDNS 등)를 옵션으로 추가.

### TURN
- 일부 NAT(Symmetric NAT, CGNAT)에서는 P2P 직결 실패.
- 시그널링 서버가 같은 PC라 영향 없지만, **WebRTC media 자체**가 직결 안 될 수 있음.
- 그 경우 TURN 서버 경유 필요. 추후 무료 TURN(metered.ca) 또는 자체 coturn 옵션 추가.

### 모바일 웹 (Next.js)
- 추가 설치 필요 없음 (앱스토어 거치지 않음).
- WebRTC 표준 API 사용 → iOS Safari / Android Chrome 모두 지원.
- 기존 입력 UI(조이스틱/키패드) 재활용.

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
| 1 | 임베디드 시그널링 + 모바일 join/round-trip 검증 *(현재)* |
| 2 | 캡처(전체 화면) + 인코더(NVENC) + WebRTC peer 결선 |
| 3 | 프로세스/창 리스트 + 창 단위 캡처 (Windows.Graphics.Capture) |
| 4 | Pro Micro 펌웨어 + 시리얼 프로토콜 + DataChannel 입력 |
| 5 | 페어링 코드, 자동 재연결, TURN, 트레이 아이콘, DDNS 옵션 |

## 보안

- room code (6자리)로 viewer 검증. 추후 옵션으로 PSK 강화.
- 같은 프로세스 안에서 시그널링 ↔ WebRTC peer가 직결 → 외부 인증/권한 누수 표면적 최소.
- 입력 명령은 ROOM 인증된 viewer의 DataChannel에서만 수락. IPC/공유 메모리/네트워크 소켓 경로는 받지 않음.
- TLS 미적용 단계에서는 같은 LAN 또는 신뢰 가능 네트워크에서만 사용 권장. Phase 5에서 옵셔널 self-signed 또는 Let's Encrypt(도메인 보유 시) 추가.
