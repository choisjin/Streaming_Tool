"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import StreamViewer from "@/components/StreamViewer";
import VirtualJoystick from "@/components/VirtualJoystick";
import VirtualMousePad from "@/components/VirtualMousePad";
import Toolbar from "@/components/Toolbar";
import { useConnection } from "@/hooks/useConnection";
import { ControlMode, Handedness, InputEvent } from "@/lib/types";

const WS_URL = process.env.NEXT_PUBLIC_WS_URL || "ws://localhost:8080";

export default function Home() {
  const [mode, setMode] = useState<ControlMode>("keyboard");
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [activeKey, setActiveKey] = useState<string | null>(null);
  const [mouseButton, setMouseButton] = useState<0 | 2>(0); // 0 = L, 2 = R
  const [handedness, setHandedness] = useState<Handedness>("left");
  const containerRef = useRef<HTMLDivElement>(null);
  const { status, connect, disconnect, sendInput } = useConnection(WS_URL);

  // Load persisted handedness on mount (client-only to avoid hydration mismatch)
  useEffect(() => {
    const saved = localStorage.getItem("handedness");
    if (saved === "left" || saved === "right") setHandedness(saved);
  }, []);

  useEffect(() => {
    localStorage.setItem("handedness", handedness);
  }, [handedness]);

  const handleInput = useCallback(
    (event: InputEvent) => {
      sendInput(event);
    },
    [sendInput]
  );

  const toggleFullscreen = useCallback(async () => {
    const el = containerRef.current;
    if (!el) return;
    try {
      if (!document.fullscreenElement) {
        // Some iOS Safari versions only expose webkitRequestFullscreen on <video>
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
    const handleChange = () => {
      setIsFullscreen(!!document.fullscreenElement);
    };
    document.addEventListener("fullscreenchange", handleChange);
    return () => document.removeEventListener("fullscreenchange", handleChange);
  }, []);

  // Clear activeKey when switching to keyboard mode
  useEffect(() => {
    if (mode === "keyboard") setActiveKey(null);
  }, [mode]);

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
        />

        {/* Connection overlay */}
        {status !== "connected" && (
          <div className="absolute inset-0 flex items-center justify-center bg-black/60 backdrop-blur-sm">
            <div className="text-center space-y-4">
              <div className="w-16 h-16 mx-auto rounded-2xl bg-zinc-800 flex items-center justify-center">
                <svg
                  className="w-8 h-8 text-zinc-400"
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth={1.5}
                    d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17h14a2 2 0 002-2V5a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z"
                  />
                </svg>
              </div>
              <div>
                <h2 className="text-lg font-semibold text-zinc-200">
                  Remote Desktop
                </h2>
                <p className="text-sm text-zinc-500 mt-1">
                  Connect to start controlling the remote PC
                </p>
              </div>
              <button
                onClick={connect}
                disabled={status === "connecting"}
                className="px-6 py-2 bg-blue-600 hover:bg-blue-500 disabled:bg-zinc-700 disabled:text-zinc-500 text-white text-sm font-medium rounded-lg transition-colors"
              >
                {status === "connecting" ? "Connecting..." : "Connect"}
              </button>
              {status === "error" && (
                <p className="text-xs text-red-400">
                  Connection failed. Check the server address and try again.
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
