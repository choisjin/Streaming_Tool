# Streaming_tool — PC Client (Windows)

C# / .NET 8 / WPF host that captures the desktop or a chosen window, encodes via GPU,
streams to mobile viewers over WebRTC, and forwards their input commands to a Pro Micro
(USB HID) so GameGuard sees only hardware input.

The same process also hosts the WebSocket signaling endpoint, so no external server is
required — viewers connect directly to `ws://<PC IP>:<port>/ws`.

## Status

Phase 1 complete: `EmbeddedSignalingServer` accepts viewer joins and relays answer/ice.
MainWindow shows LAN + WAN connect URLs and the room code.

Capture, encoder and WebRTC peer wiring land in Phase 2.

## Build

Requires:
- Windows 10 19041+ (for `Windows.Graphics.Capture` — Phase 3 only)
- Visual Studio 2022 (17.8+) **or** `dotnet` SDK 8 with the Windows Desktop workload

```pwsh
dotnet build pc-client\StreamingHost.sln
```

## Run

```pwsh
dotnet run --project pc-client\StreamingHost\StreamingHost.csproj
```

In the UI:
1. Pick a listen port (default 8080) and room code.
2. Click **Start**. The LAN URL is shown immediately; the WAN URL appears once the
   public IP is resolved.
3. To accept viewers from cellular, forward TCP `<port>` on your router to this PC.
4. Allow the port through Windows Firewall (one-time):

```pwsh
New-NetFirewallRule -DisplayName "StreamingHost 8080" -Direction Inbound -Protocol TCP -LocalPort 8080 -Action Allow
```

## Layout

```
StreamingHost/
├── App.xaml(.cs)
├── MainWindow.xaml(.cs)              # UI + orchestration
├── Signaling/
│   └── EmbeddedSignalingServer.cs    # Kestrel /ws + /health, ViewerSession
├── Capture/
│   ├── IFrameSource.cs
│   ├── DesktopCapture.cs             # DXGI Desktop Duplication (full screen)
│   └── WindowEnumerator.cs           # Per-window selection (Phase 3 will add WGC)
└── Streaming/
    └── WebRtcPeer.cs                 # SIPSorcery peer per viewer
```

## Notes on GameGuard

This client never injects input into the game process. Mobile -> DataChannel ->
serial USB -> Pro Micro (HID) -> OS. The kernel sees actual USB HID reports.

DO NOT add `SendInput`, `keybd_event`, `mouse_event`, `PostMessage(WM_KEYDOWN)`,
`SetWindowsHookEx`, DLL injection, or any process attach to the game. Capture must
stay at OS layer (DXGI / WGC) — never hook the game's render thread.
