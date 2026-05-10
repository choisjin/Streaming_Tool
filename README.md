# Streaming_tool

GameGuard 호환 PC 게임 원격 플레이 도구. 모바일에서 보고 조작, PC에서 캡처/스트리밍, 입력은 Pro Micro USB HID로 주입.

## Repo layout (monorepo)

```
.
├── web/                # Mobile web app — Next.js 16 + React 19, WebRTC viewer
├── pc-client/          # Windows host — C# .NET 8 + WPF, DXGI/WGC capture, NVENC, SIPSorcery WebRTC
├── signaling-server/   # Mac mini — Node.js + ws, SDP/ICE relay over WSS
├── firmware/           # Pro Micro (ATmega32U4) — USB HID keyboard + mouse driven by PC client
└── docs/               # architecture / signaling-protocol / input-protocol
```

## High-level flow

1. PC 클라이언트가 Mac mini 시그널링 서버에 host로 등록 (room code 생성)
2. 모바일 웹앱이 같은 room code로 join → WebRTC P2P offer/answer 교환
3. PC -> 모바일: H.264 비디오 트랙
4. 모바일 -> PC: DataChannel "input" (조이스틱/키패드 이벤트)
5. PC가 입력을 시리얼로 Pro Micro에 전달 → Pro Micro가 진짜 USB HID로 게임 PC에 발사

## 왜 Pro Micro

`SendInput`, `PostMessage(WM_KEYDOWN)`, `keybd_event`, `mouse_event` 등 소프트웨어 입력 주입은 GameGuard가 탐지/밴. Pro Micro는 OS 입장에서 **진짜 USB HID 키보드/마우스**라 사실상 차단 불가능.

## Phase 진행 상황

- [x] **Phase 0**: 모바일 웹앱 UI (Toolbar, VirtualJoystick, VirtualMousePad, StreamViewer)
- [ ] **Phase 1+2**: 시그널링 서버 + PC 클라이언트(전체 화면 캡처) + 모바일 영상 수신 *(진행 중)*
- [ ] **Phase 3**: 프로세스/창 리스트 + 창 단위 캡처 (Windows.Graphics.Capture)
- [ ] **Phase 4**: Pro Micro 펌웨어 + 시리얼 프로토콜 + DataChannel 입력
- [ ] **Phase 5**: 페어링 코드, 자동 재연결, TURN, 트레이 아이콘, 배포

## 시작하기 (개발)

### Mobile web
```bash
cd web
npm install
npm run dev
```

### Signaling server (local)
```bash
cd signaling-server
npm install
npm run dev      # ws://localhost:8080/ws
```

### PC client
Visual Studio 2022 또는 `dotnet build pc-client/StreamingHost.sln` (.NET 8 SDK + Windows Desktop workload 필요)

### Firmware
Arduino IDE 또는 `arduino-cli`로 `firmware/pro_micro_hid` 업로드 (board: Arduino Leonardo)

## 자세한 설명

- [Architecture](docs/architecture.md)
- [Signaling protocol](docs/signaling-protocol.md)
- [Input protocol](docs/input-protocol.md)
