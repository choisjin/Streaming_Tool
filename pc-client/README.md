# Streaming_tool — PC Client (Windows)

C# / .NET 8 / WPF host that captures the desktop or a chosen window, encodes via GPU,
and streams to mobile viewers over WebRTC. Input commands arrive on a DataChannel and
are forwarded to a Pro Micro (USB HID) so GameGuard sees only hardware input.

## Status

Phase 1 (signaling round-trip): MainWindow + `SignalingClient` connect to the WSS server
and register as `host`. Capture/encoder/WebRTC peer are scaffolded; the next pass wires
DXGI capture -> H.264 encode -> SIPSorcery peer.

## Build

Requires:
- Windows 10 19041+ (for `Windows.Graphics.Capture`)
- Visual Studio 2022 (17.8+) or `dotnet` SDK 8 with the Windows Desktop workload

```pwsh
dotnet build pc-client\StreamingHost.sln
```

## Run

```pwsh
dotnet run --project pc-client\StreamingHost\StreamingHost.csproj
```

Type the signaling WS URL (default `ws://localhost:8080/ws` for local dev) and room
code, then **Start**. Mobile viewers join the same room over `wss://signal.{domain}/ws`.

## Layout

```
StreamingHost/
├── App.xaml(.cs)
├── MainWindow.xaml(.cs)         # UI + orchestration
├── Signaling/
│   └── SignalingClient.cs       # WSS host registration, SDP/ICE relay
├── Capture/
│   ├── IFrameSource.cs
│   ├── DesktopCapture.cs        # DXGI Desktop Duplication (full screen)
│   └── WindowEnumerator.cs      # EnumWindows + per-window selection (Phase 3 will add WGC)
└── Streaming/
    └── WebRtcPeer.cs            # SIPSorcery peer per viewer
```

## Notes on GameGuard

This client never injects input into the game process. Mobile -> DataChannel ->
serial USB -> Pro Micro (HID) -> OS. The kernel sees actual USB HID reports.

DO NOT add `SendInput`, `keybd_event`, `mouse_event`, `PostMessage(WM_KEYDOWN)`,
`SetWindowsHookEx`, DLL injection, or any process attach to the game. Capture must
stay at OS layer (DXGI / WGC) — never hook the game's render thread.
