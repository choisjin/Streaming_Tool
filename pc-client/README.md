# Streaming_tool — PC Client (Windows)

C# / .NET 8 / WPF host that captures the desktop or a chosen window, encodes via GPU,
streams to mobile viewers over WebRTC, and forwards their input commands to a Pro Micro
(USB HID) so GameGuard sees only hardware input.

The same process also hosts the WebSocket signaling endpoint, so no external server is
required — viewers connect directly to `ws://<PC IP>:<port>/ws`.

## Status

Phase 2 wired:
- `EmbeddedSignalingServer` accepts viewer joins and exposes a `ViewerSession`.
- `WebRtcPeer` drives offer/answer + ICE against that session and sends an H.264 video track.
- `DesktopCapture` (DXGI Desktop Duplication) feeds raw BGRA frames.
- `H264Encoder` (SIPSorceryMedia.FFmpeg) encodes to H.264 NAL units.
- `StreamingPipeline` fans encoded frames out to all active peers.
- `MainWindow` orchestrates Start/Stop and shows LAN/WAN URLs + room code.

Per-window capture (Phase 3) and Pro Micro serial input forwarding (Phase 4)
are next.

## Prerequisites

1. **Windows 10 19041+** (1909 should work for Phase 1+2; per-window capture later requires WGC)
2. **.NET 8 SDK** — https://dotnet.microsoft.com/download/dotnet/8.0
   - Includes the Windows Desktop workload
3. **FFmpeg shared libraries** on PATH or next to the EXE:
   `avcodec-*.dll`, `avformat-*.dll`, `avutil-*.dll`, `swscale-*.dll`, `swresample-*.dll`
   - Easiest: install with winget — `winget install Gyan.FFmpeg.Shared`
   - Or grab the **shared** build from https://www.gyan.dev/ffmpeg/builds/ and copy
     the DLLs from `bin\` into the StreamingHost output directory.
4. (Optional) GPU encoder — NVENC (NVIDIA), AMF (AMD), QuickSync (Intel). FFmpeg picks
   automatically; libx264 is the software fallback.

## Build & run

```pwsh
dotnet build pc-client\StreamingHost.sln
dotnet run --project pc-client\StreamingHost\StreamingHost.csproj
```

Or open `pc-client\StreamingHost.sln` in Visual Studio 2022 and run `StreamingHost`.

## In the UI

1. Pick a listen port (default 8080) and room code (auto-generated; click ↻ for a new one).
2. Click **Start**. The LAN URL appears immediately; the WAN URL appears once the public IP is resolved (api.ipify.org).
3. Allow the port through Windows Firewall (one-time):

```pwsh
New-NetFirewallRule -DisplayName "StreamingHost 8080" -Direction Inbound -Protocol TCP -LocalPort 8080 -Action Allow
```

4. To accept viewers from cellular, forward TCP `<port>` on your router to this PC's LAN IP.

## Layout

```
StreamingHost/
├── App.xaml(.cs)
├── MainWindow.xaml(.cs)              # UI + orchestration (capture + pipeline + signaling)
├── Signaling/
│   └── EmbeddedSignalingServer.cs    # Kestrel /ws + /health, ViewerSession
├── Capture/
│   ├── IFrameSource.cs
│   ├── DesktopCapture.cs             # DXGI Desktop Duplication (full screen)
│   └── WindowEnumerator.cs           # Top-level window list (Phase 3 hooks WGC here)
└── Streaming/
    ├── H264Encoder.cs                # SIPSorceryMedia.FFmpeg — BGRA -> H.264 NALU
    ├── StreamingPipeline.cs          # Capture -> encoder -> fan out to peers
    └── WebRtcPeer.cs                 # SIPSorcery peer per viewer
```

## Notes on GameGuard

This client never injects input into the game process. Mobile -> DataChannel ->
serial USB -> Pro Micro (HID) -> OS. The kernel sees actual USB HID reports.

DO NOT add `SendInput`, `keybd_event`, `mouse_event`, `PostMessage(WM_KEYDOWN)`,
`SetWindowsHookEx`, DLL injection, or any process attach to the game. Capture must
stay at OS layer (DXGI / WGC) — never hook the game's render thread.

## Known caveats (will tighten in later phases)

- `DesktopCapture` always picks the **primary** monitor at index 0. Multi-monitor selection lands with WGC.
- Encoder is fixed at the capture resolution (no downscaling yet) — large 4K monitors will eat CPU/GPU.
- Force-keyframe interval is hard-coded to 2s. RTCP PLI / FIR handling will trigger keyframes on demand later.
- `H264Encoder.Encode` copies BGRA to a managed `byte[]` per frame; we'll move to pinned/native buffers when we squeeze latency.
