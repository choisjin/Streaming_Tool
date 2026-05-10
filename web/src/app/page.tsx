"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import StreamViewer from "@/components/StreamViewer";
import VirtualJoystick from "@/components/VirtualJoystick";
import VirtualMousePad from "@/components/VirtualMousePad";
import Toolbar from "@/components/Toolbar";
import { useWebRtcViewer } from "@/hooks/useWebRtcViewer";
import { ControlMode, Handedness, InputEvent } from "@/lib/types";

const DEFAULT_URL =
  process.env.NEXT_PUBLIC_SIGNALING_URL || "ws://192.168.0.2:8080/ws";

export default function Home() {
  const [mode, setMode] = useState<ControlMode>("keyboard");
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [activeKey, setActiveKey] = useState<string | null>(null);
  const [mouseButton, setMouseButton] = useState<0 | 2>(0); // 0 = L, 2 = R
  const [handedness, setHandedness] = useState<Handedness>("left");

  // Connection target — persisted in localStorage so phones remember the last host.
  const [signalingUrl, setSignalingUrl] = useState<string>(DEFAULT_URL);
  const [roomCode, setRoomCode] = useState<string>("");

  const containerRef = useRef<HTMLDivElement>(null);
  const { status, stream, connect, disconnect, sendInput } = useWebRtcViewer(
    signalingUrl,
    roomCode
  );

  // Restore persisted prefs on mount
  useEffect(() => {
    const h = localStorage.getItem("handedness");
    if (h === "left" || h === "right") setHandedness(h);
    const u = localStorage.getItem("signalingUrl");
    if (u) setSignalingUrl(u);
    const r = localStorage.getItem("roomCode");
    if (r) setRoomCode(r);
  }, []);

  useEffect(() => localStorage.setItem("handedness", handedness), [handedness]);
  useEffect(() => localStorage.setItem("signalingUrl", signalingUrl), [signalingUrl]);
  useEffect(() => localStorage.setItem("roomCode", roomCode), [roomCode]);

  const handleInput = useCallback(
    (event: InputEvent) => sendInput(event),
    [sendInput]
  );

  const toggleFullscreen = useCallback(async () => {
    const el = containerRef.current;
    if (!el) return;
    try {
      if (!document.fullscreenElement) {
        const req =
          el.requestFullscreen?.bind(el) ??
          (el as unknown as { webkitRequestFullscreen?: () => Promise<void> })
            .webkitRequestFullscreen?.bind(el);
        if (req) await req();
      } else {
        await document.exitFullscreen();
      }
    } catch (err) {
      console.warn("[fullscreen] toggle failed:", err);
    }
  }, []);

  useEffect(() => {
    const handleChange = () => setIsFullscreen(!!document.fullscreenElement);
    document.addEventListener("fullscreenchange", handleChange);
    return () => document.removeEventListener("fullscreenchange", handleChange);
  }, []);

  useEffect(() => {
    if (mode === "keyboard") setActiveKey(null);
  }, [mode]);

  const canConnect = roomCode.trim().length >= 4 && /^wss?:\/\//.test(signalingUrl);

  return (
    <div
      ref={containerRef}
      className="flex flex-col h-screen bg-zinc-950 text-white"
    >
      {/* Stream area */}
      <div className="flex-1 relative overflow-hidden">
        <StreamViewer
          isActive={status === "connected"}
          onInput={handleInput}
          keyboardMode={mode === "keyboard"}
          activeKey={activeKey}
          mouseButton={mouseButton}
          mediaStream={stream}
        />

        {/* Connection overlay */}
        {status !== "connected" && (
          <div className="absolute inset-0 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4">
            <div className="w-full max-w-sm space-y-4">
              <div className="text-center">
                <h2 className="text-lg font-semibold text-zinc-200">
                  Remote Desktop
                </h2>
                <p className="text-sm text-zinc-500 mt-1">
                  Enter the host URL and room code shown by the PC client.
                </p>
              </div>

              <div className="space-y-2">
                <label className="block">
                  <span className="text-xs text-zinc-400">Host URL</span>
                  <input
                    value={signalingUrl}
                    onChange={(e) => setSignalingUrl(e.target.value)}
                    placeholder="ws://192.168.0.2:8080/ws"
                    spellCheck={false}
                    autoCapitalize="none"
                    autoCorrect="off"
                    inputMode="url"
                    className="mt-1 w-full px-3 py-2 bg-zinc-900 border border-zinc-700 rounded text-sm text-zinc-100 focus:outline-none focus:border-blue-500"
                  />
                </label>
                <label className="block">
                  <span className="text-xs text-zinc-400">Room code</span>
                  <input
                    value={roomCode}
                    onChange={(e) => setRoomCode(e.target.value.toUpperCase())}
                    placeholder="ABC123"
                    spellCheck={false}
                    autoCapitalize="characters"
                    autoCorrect="off"
                    maxLength={12}
                    className="mt-1 w-full px-3 py-2 bg-zinc-900 border border-zinc-700 rounded text-sm font-mono tracking-widest text-zinc-100 focus:outline-none focus:border-blue-500"
                  />
                </label>
              </div>

              <button
                onClick={connect}
                disabled={!canConnect || status === "connecting"}
                className="w-full px-6 py-2 bg-blue-600 hover:bg-blue-500 disabled:bg-zinc-700 disabled:text-zinc-500 text-white text-sm font-medium rounded-lg transition-colors"
              >
                {status === "connecting" ? "Connecting…" : "Connect"}
              </button>

              {status === "error" && (
                <p className="text-xs text-red-400 text-center">
                  Connection failed. Check the host URL, room code, port forwarding, and Windows Firewall.
                </p>
              )}
            </div>
          </div>
        )}
      </div>

      {/* Bottom: toolbar + controls */}
      <div
        className="bg-zinc-900/95 border-t border-zinc-800"
        style={{ touchAction: "manipulation" }}
      >
        <Toolbar
          mode={mode}
          onModeChange={setMode}
          connectionStatus={status}
          onConnect={connect}
          onDisconnect={disconnect}
          onFullscreen={toggleFullscreen}
          isFullscreen={isFullscreen}
          handedness={handedness}
          onHandednessChange={setHandedness}
        />
        {mode === "joystick" ? (
          <VirtualJoystick
            onInput={handleInput}
            activeKey={activeKey}
            onActiveKeyChange={setActiveKey}
            handedness={handedness}
          />
        ) : (
          <VirtualMousePad
            onInput={handleInput}
            mouseButton={mouseButton}
            onMouseButtonChange={setMouseButton}
            handedness={handedness}
          />
        )}
      </div>
    </div>
  );
}
