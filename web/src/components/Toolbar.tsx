"use client";

import { ControlMode, ConnectionStatus, Handedness } from "@/lib/types";
import { useTap } from "@/hooks/useTap";

interface ToolbarProps {
  mode: ControlMode;
  onModeChange: (mode: ControlMode) => void;
  connectionStatus: ConnectionStatus;
  onConnect: () => void;
  onDisconnect: () => void;
  onFullscreen: () => void;
  isFullscreen: boolean;
  handedness: Handedness;
  onHandednessChange: (h: Handedness) => void;
}

const statusColors: Record<ConnectionStatus, string> = {
  disconnected: "bg-zinc-500",
  connecting: "bg-yellow-500 animate-pulse",
  connected: "bg-green-500",
  error: "bg-red-500",
};

export default function Toolbar({
  mode,
  onModeChange,
  connectionStatus,
  onConnect,
  onDisconnect,
  onFullscreen,
  isFullscreen,
  handedness,
  onHandednessChange,
}: ToolbarProps) {
  const tap = useTap();
  const isConnected = connectionStatus === "connected";

  return (
    <div
      className="flex items-center justify-between px-4 py-2 bg-zinc-900 border-b border-zinc-800/70"
      style={{ touchAction: "manipulation" }}
    >
      {/* Left: Connection */}
      <div className="flex items-center gap-3">
        <div
          className={`w-2.5 h-2.5 rounded-full ${statusColors[connectionStatus]}`}
          title={connectionStatus}
        />
        {isConnected ? (
          <button
            {...tap(onDisconnect)}
            className="px-3 py-1 text-xs bg-red-600/20 text-red-400 rounded active:bg-red-600/30 transition-colors"
          >
            Disconnect
          </button>
        ) : (
          <button
            {...tap(onConnect)}
            className="px-3 py-1 text-xs bg-blue-600/20 text-blue-400 rounded active:bg-blue-600/30 transition-colors"
            disabled={connectionStatus === "connecting"}
          >
            Connect
          </button>
        )}
      </div>

      {/* Center: Mode switch */}
      <div className="flex items-center bg-zinc-800 rounded-lg p-0.5">
        <button
          {...tap(() => onModeChange("keyboard"))}
          className={`px-3 py-1.5 rounded-md text-xs font-medium transition-colors ${
            mode === "keyboard"
              ? "bg-zinc-600 text-white"
              : "text-zinc-400"
          }`}
        >
          Manual
        </button>
        <button
          {...tap(() => onModeChange("joystick"))}
          className={`px-3 py-1.5 rounded-md text-xs font-medium transition-colors ${
            mode === "joystick"
              ? "bg-zinc-600 text-white"
              : "text-zinc-400"
          }`}
        >
          Joystick
        </button>
      </div>

      {/* Right: Handedness + Fullscreen */}
      <div className="flex items-center gap-2">
        <button
          {...tap(() =>
            onHandednessChange(handedness === "left" ? "right" : "left")
          )}
          className="px-2 py-1 rounded text-[11px] font-medium text-zinc-300 bg-zinc-800 active:bg-zinc-700 transition-colors border border-zinc-700/60"
          title={
            handedness === "left"
              ? "Left-handed (joystick on left)"
              : "Right-handed (joystick on right)"
          }
        >
          {handedness === "left" ? "L←" : "→R"}
        </button>
        <button
          onClick={onFullscreen}
          style={{ touchAction: "manipulation" }}
          className="p-1.5 text-zinc-400 rounded active:bg-zinc-800 transition-colors"
          title={isFullscreen ? "Exit fullscreen" : "Fullscreen"}
        >
        {isFullscreen ? (
          <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 9L4 4m0 0v4m0-4h4m7 9l5 5m0 0v-4m0 4h-4M9 15l-5 5m0 0h4m-4 0v-4m11-7l5-5m0 0h-4m4 0v4" />
          </svg>
        ) : (
          <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 8V4m0 0h4M4 4l5 5m11-1V4m0 0h-4m4 0l-5 5M4 16v4m0 0h4m-4 0l5-5m11 5v-4m0 4h-4m4 0l-5-5" />
          </svg>
        )}
        </button>
      </div>
    </div>
  );
}
