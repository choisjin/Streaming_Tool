# Streaming_tool

GameGuard 호환 PC 게임 원격 플레이 도구. 모바일에서 보고 조작, PC 한 대만 있으면 동작.

## Repo layout (monorepo)

```
.
├── web/        # Mobile web app — Next.js 16 + React 19, WebRTC viewer
├── pc-client/  # Windows host — C# .NET 8 + WPF, embedded signaling, capture, NVENC, SIPSorcery WebRTC
├── firmware/   # Pro Micro (ATmega32U4) — USB HID keyboard + mouse driven by PC client
└── docs/       # architecture / signaling-protocol / input-protocol
```

> 외부 시그널링 서버는 사용하지 않습니다. PC 클라이언트 자체가 시그널링까지 호스팅합니다.

## High-level flow

1. PC 클라이언트 실행 → `ws://0.0.0.0:8080/ws`로 listen + room code 발급
2. 모바일 웹앱이 `ws://<PC IP>:8080/ws` 와 room code 입력 → join
3. WebRTC P2P offer/answer 교환
4. PC -> 모바일: H.264 비디오 트랙
5. 모바일 -> PC: DataChannel "input" (조이스틱/키패드 이벤트)
6. PC가 입력을 시리얼로 Pro Micro에 전달 → Pro Micro가 진짜 USB HID로 게임 PC에 발사

## 외부망에서 접속하려면

PC가 외부에서 접근 가능해야 합니다. 가장 단순한 경로:

1. 공유기 관리자 페이지 → **포트포워딩**: 외부 TCP 8080 → PC LAN IP:8080
2. PC 방화벽이 8080 인바운드를 허용 (`netsh advfirewall ...`)
3. PC 클라이언트 UI에 표시되는 **WAN URL**(공인 IP)을 모바일에 입력
4. 공인 IP가 자주 바뀌면 추후 DDNS(DuckDNS 등) 옵션을 사용

같은 Wi-Fi 안에서는 LAN URL 그대로 사용.

## 왜 Pro Micro

`SendInput`, `PostMessage(WM_KEYDOWN)`, `keybd_event` 등 소프트웨어 입력 주입은 GameGuard가 탐지/밴. Pro Micro는 OS 입장에서 **진짜 USB HID 키보드/마우스**라 사실상 차단 불가능.

## Phase 진행 상황

- [x] **Phase 0**: 모바일 웹앱 UI (Toolbar, VirtualJoystick, VirtualMousePad, StreamViewer)
- [x] **Phase 1**: PC 클라이언트 임베디드 시그널링 서버 + 모바일 join/round-trip 검증
- [ ] **Phase 2**: 캡처(전체 화면) + 인코더(NVENC) + WebRTC peer 결선
- [ ] **Phase 3**: 프로세스/창 리스트 + 창 단위 캡처 (Windows.Graphics.Capture)
- [ ] **Phase 4**: Pro Micro 펌웨어 + 시리얼 프로토콜 + DataChannel 입력
- [ ] **Phase 5**: 자동 재연결, TURN, 트레이 아이콘, DDNS 옵션, TLS 옵션

## 개발용 실행

### Mobile web
```bash
cd web
npm install
npm run dev      # http://localhost:3000 또는 휴대폰에서 PC LAN IP:3000
```

### PC client
```pwsh
dotnet run --project pc-client/StreamingHost/StreamingHost.csproj
# 또는 Visual Studio 2022로 pc-client/StreamingHost.sln 열기
```

(.NET 8 SDK + Windows Desktop workload 필요)

### Firmware
Arduino IDE 또는 `arduino-cli`로 `firmware/pro_micro_hid` 업로드 (board: Arduino Leonardo)

## 자세한 설명

- [Architecture](docs/architecture.md)
- [Signaling protocol](docs/signaling-protocol.md)
- [Input protocol](docs/input-protocol.md)
