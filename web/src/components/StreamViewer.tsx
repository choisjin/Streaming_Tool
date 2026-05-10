"use client";

import { useCallback, useEffect, useRef } from "react";
import { InputEvent, MouseInputEvent, KeyboardInputEvent } from "@/lib/types";

interface StreamViewerProps {
  streamUrl?: string;
  /** Live MediaStream from the WebRTC peer (preferred over streamUrl). */
  mediaStream?: MediaStream | null;
  isActive: boolean;
  onInput: (event: InputEvent) => void;
  keyboardMode: boolean;
  activeKey?: string | null;
  mouseButton?: 0 | 2;
}

export default function StreamViewer({
  streamUrl,
  mediaStream,
  isActive,
  onInput,
  keyboardMode,
  activeKey,
  mouseButton = 0,
}: StreamViewerProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const videoRef = useRef<HTMLVideoElement>(null);

  const getNormalizedPosition = useCallback(
    (clientX: number, clientY: number): { x: number; y: number } => {
      const rect = containerRef.current?.getBoundingClientRect();
      if (!rect) return { x: 0, y: 0 };
      return {
        x: (clientX - rect.left) / rect.width,
        y: (clientY - rect.top) / rect.height,
      };
    },
    []
  );

  const handlePointerMove = useCallback(
    (e: React.PointerEvent) => {
      if (!isActive || !keyboardMode) return;
      const pos = getNormalizedPosition(e.clientX, e.clientY);
      const event: MouseInputEvent = {
        type: "mouse",
        action: "move",
        ...pos,
      };
      onInput(event);
    },
    [isActive, keyboardMode, getNormalizedPosition, onInput]
  );

  const handlePointerDown = useCallback(
    (e: React.PointerEvent) => {
      if (!isActive) return;

      // Joystick mode: send active key at touch position
      if (!keyboardMode && activeKey) {
        const pos = getNormalizedPosition(e.clientX, e.clientY);
        const keyEvent: KeyboardInputEvent = {
          type: "keyboard",
          action: "down",
          key: activeKey,
          code: activeKey === "Escape" ? "Escape" : `Key${activeKey.toUpperCase()}`,
          modifiers: { ctrl: false, alt: false, shift: false, meta: false },
        };
        const posEvent: MouseInputEvent = {
          type: "mouse",
          action: "click",
          ...pos,
        };
        onInput(posEvent);
        onInput(keyEvent);
        setTimeout(() => {
          onInput({ ...keyEvent, action: "up" });
        }, 50);
        return;
      }

      if (!keyboardMode) return;
      e.currentTarget.setPointerCapture(e.pointerId);
      const pos = getNormalizedPosition(e.clientX, e.clientY);
      // Mouse uses native button; touch/pen uses the toggle-selected mouseButton
      const button = e.pointerType === "mouse" ? e.button : mouseButton;
      const event: MouseInputEvent = {
        type: "mouse",
        action: "down",
        ...pos,
        button,
      };
      onInput(event);
    },
    [isActive, keyboardMode, activeKey, mouseButton, getNormalizedPosition, onInput]
  );

  const handlePointerUp = useCallback(
    (e: React.PointerEvent) => {
      if (!isActive || !keyboardMode) return;
      if (e.currentTarget.hasPointerCapture(e.pointerId)) {
        e.currentTarget.releasePointerCapture(e.pointerId);
      }
      const pos = getNormalizedPosition(e.clientX, e.clientY);
      const button = e.pointerType === "mouse" ? e.button : mouseButton;
      const event: MouseInputEvent = {
        type: "mouse",
        action: "up",
        ...pos,
        button,
      };
      onInput(event);
    },
    [isActive, keyboardMode, mouseButton, getNormalizedPosition, onInput]
  );

  const handleWheel = useCallback(
    (e: WheelEvent) => {
      if (!isActive || !keyboardMode) return;
      e.preventDefault();
      const rect = containerRef.current?.getBoundingClientRect();
      if (!rect) return;
      const event: MouseInputEvent = {
        type: "mouse",
        action: "scroll",
        x: (e.clientX - rect.left) / rect.width,
        y: (e.clientY - rect.top) / rect.height,
        deltaX: e.deltaX,
        deltaY: e.deltaY,
      };
      onInput(event);
    },
    [isActive, keyboardMode, onInput]
  );

  const handleContextMenu = useCallback(
    (e: React.MouseEvent) => {
      if (isActive && keyboardMode) {
        e.preventDefault();
      }
    },
    [isActive, keyboardMode]
  );

  // Keyboard event handler
  useEffect(() => {
    if (!isActive || !keyboardMode) return;

    const handleKey = (e: KeyboardEvent) => {
      e.preventDefault();
      const event: KeyboardInputEvent = {
        type: "keyboard",
        action: e.type === "keydown" ? "down" : "up",
        key: e.key,
        code: e.code,
        modifiers: {
          ctrl: e.ctrlKey,
          alt: e.altKey,
          shift: e.shiftKey,
          meta: e.metaKey,
        },
      };
      onInput(event);
    };

    window.addEventListener("keydown", handleKey);
    window.addEventListener("keyup", handleKey);
    return () => {
      window.removeEventListener("keydown", handleKey);
      window.removeEventListener("keyup", handleKey);
    };
  }, [isActive, keyboardMode, onInput]);

  // Wheel event (passive: false)
  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    el.addEventListener("wheel", handleWheel, { passive: false });
    return () => el.removeEventListener("wheel", handleWheel);
  }, [handleWheel]);

  // Attach incoming MediaStream to the <video> element
  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    if (mediaStream) {
      if (video.srcObject !== mediaStream) video.srcObject = mediaStream;
    } else {
      video.srcObject = null;
    }
  }, [mediaStream]);

  return (
    <div
      ref={containerRef}
      className={`relative w-full h-full bg-black select-none ${
        !keyboardMode && activeKey ? "cursor-crosshair" : "cursor-none"
      }`}
      style={{ touchAction: "none" }}
      onPointerMove={handlePointerMove}
      onPointerDown={handlePointerDown}
      onPointerUp={handlePointerUp}
      onPointerCancel={handlePointerUp}
      onContextMenu={handleContextMenu}
      tabIndex={0}
    >
      <video
        ref={videoRef}
        className="w-full h-full object-contain"
        autoPlay
        playsInline
        muted
      />

      {/* No stream placeholder */}
      {!streamUrl && !mediaStream && (
        <div className="absolute inset-0 flex items-center justify-center text-zinc-500">
          <div className="text-center">
            <svg
              className="w-16 h-16 mx-auto mb-4 opacity-30"
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
            <p className="text-sm">Waiting for stream connection...</p>
          </div>
        </div>
      )}
    </div>
  );
}
