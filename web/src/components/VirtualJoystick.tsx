"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { Handedness, InputEvent, KeyboardInputEvent } from "@/lib/types";
import { useTap } from "@/hooks/useTap";

interface VirtualJoystickProps {
  onInput: (event: InputEvent) => void;
  activeKey: string | null;
  onActiveKeyChange: (key: string | null) => void;
  handedness: Handedness;
}

interface StickState {
  x: number;
  y: number;
  active: boolean;
}

const DEAD_ZONE = 0.3;

function makeKeyEvent(key: string, action: "down" | "up"): KeyboardInputEvent {
  const codeMap: Record<string, string> = {
    ArrowUp: "ArrowUp",
    ArrowDown: "ArrowDown",
    ArrowLeft: "ArrowLeft",
    ArrowRight: "ArrowRight",
    q: "KeyQ",
    w: "KeyW",
    e: "KeyE",
    Escape: "Escape",
  };
  return {
    type: "keyboard",
    action,
    key,
    code: codeMap[key] || key,
    modifiers: { ctrl: false, alt: false, shift: false, meta: false },
  };
}

const SKILL_BUTTONS = [
  { key: "q", label: "Q" },
  { key: "w", label: "W" },
  { key: "e", label: "E" },
];

export default function VirtualJoystick({
  onInput,
  activeKey,
  onActiveKeyChange,
  handedness,
}: VirtualJoystickProps) {
  const stickRef = useRef<HTMLDivElement>(null);
  const [stick, setStick] = useState<StickState>({ x: 0, y: 0, active: false });
  const animRef = useRef<number>(0);
  const pressedRef = useRef<Set<string>>(new Set());

  const getOffset = useCallback((clientX: number, clientY: number) => {
    const rect = stickRef.current?.getBoundingClientRect();
    if (!rect) return { x: 0, y: 0 };
    const centerX = rect.left + rect.width / 2;
    const centerY = rect.top + rect.height / 2;
    const maxRadius = rect.width / 2 - 20;
    let dx = clientX - centerX;
    let dy = clientY - centerY;
    const dist = Math.sqrt(dx * dx + dy * dy);
    if (dist > maxRadius) {
      dx = (dx / dist) * maxRadius;
      dy = (dy / dist) * maxRadius;
    }
    return { x: dx / maxRadius, y: dy / maxRadius };
  }, []);

  const updateArrowKeys = useCallback(
    (x: number, y: number) => {
      const pressed = pressedRef.current;
      const directions: [string, boolean][] = [
        ["ArrowUp", y < -DEAD_ZONE],
        ["ArrowDown", y > DEAD_ZONE],
        ["ArrowLeft", x < -DEAD_ZONE],
        ["ArrowRight", x > DEAD_ZONE],
      ];
      for (const [key, shouldPress] of directions) {
        if (shouldPress && !pressed.has(key)) {
          pressed.add(key);
          onInput(makeKeyEvent(key, "down"));
        } else if (!shouldPress && pressed.has(key)) {
          pressed.delete(key);
          onInput(makeKeyEvent(key, "up"));
        }
      }
    },
    [onInput]
  );

  const handleStart = useCallback(
    (clientX: number, clientY: number) => {
      const offset = getOffset(clientX, clientY);
      setStick({ ...offset, active: true });
      updateArrowKeys(offset.x, offset.y);
    },
    [getOffset, updateArrowKeys]
  );

  const handleMove = useCallback(
    (clientX: number, clientY: number) => {
      if (!stick.active) return;
      const offset = getOffset(clientX, clientY);
      setStick((prev) => ({ ...prev, ...offset }));
      cancelAnimationFrame(animRef.current);
      animRef.current = requestAnimationFrame(() => {
        updateArrowKeys(offset.x, offset.y);
      });
    },
    [stick.active, getOffset, updateArrowKeys]
  );

  const handleEnd = useCallback(() => {
    setStick({ x: 0, y: 0, active: false });
    const pressed = pressedRef.current;
    for (const key of pressed) {
      onInput(makeKeyEvent(key, "up"));
    }
    pressed.clear();
  }, [onInput]);

  useEffect(() => {
    return () => {
      const pressed = pressedRef.current;
      for (const key of pressed) {
        onInput(makeKeyEvent(key, "up"));
      }
      pressed.clear();
    };
  }, [onInput]);

  const toggleKey = useCallback(
    (key: string) => {
      onActiveKeyChange(activeKey === key ? null : key);
    },
    [activeKey, onActiveKeyChange]
  );

  // ESC: immediate press, not toggle
  const handleEsc = useCallback(() => {
    onInput(makeKeyEvent("Escape", "down"));
    setTimeout(() => {
      onInput(makeKeyEvent("Escape", "up"));
    }, 80);
  }, [onInput]);

  const tap = useTap();

  return (
    <div
      className={`flex items-center w-full px-3 py-3 select-none ${
        handedness === "right" ? "flex-row-reverse" : ""
      }`}
    >
      {/* ===== Left hand zone: Q W E + Joystick ===== */}
      <div className="flex items-center gap-3">
        {/* Skill buttons (Q, W, E) - vertical stack */}
        <div className="flex flex-col gap-2">
          {SKILL_BUTTONS.map((btn) => (
            <button
              key={btn.key}
              {...tap(() => toggleKey(btn.key))}
              className={`w-12 h-12 rounded-lg font-bold text-sm select-none transition-colors border ${
                activeKey === btn.key
                  ? "bg-blue-600 text-white border-blue-400 ring-1 ring-blue-300"
                  : "bg-zinc-800 text-zinc-300 border-zinc-700/60 active:bg-zinc-600"
              }`}
            >
              {btn.label}
            </button>
          ))}
        </div>

        {/* D-Pad joystick */}
        <div className="flex flex-col items-center">
          <div className="relative">
            <div
              ref={stickRef}
              className="relative w-28 h-28 rounded-full bg-zinc-800/80 border border-zinc-700 touch-none select-none"
              style={{ touchAction: "none" }}
              onPointerDown={(e) => {
                e.currentTarget.setPointerCapture(e.pointerId);
                handleStart(e.clientX, e.clientY);
              }}
              onPointerMove={(e) => {
                if (e.currentTarget.hasPointerCapture(e.pointerId)) {
                  handleMove(e.clientX, e.clientY);
                }
              }}
              onPointerUp={(e) => {
                e.currentTarget.releasePointerCapture(e.pointerId);
                handleEnd();
              }}
              onPointerCancel={(e) => {
                e.currentTarget.releasePointerCapture(e.pointerId);
                handleEnd();
              }}
            >
              <div
                className="absolute w-10 h-10 rounded-full bg-zinc-600 border-2 border-zinc-500 shadow-lg transition-transform duration-75"
                style={{
                  left: "50%",
                  top: "50%",
                  transform: `translate(calc(-50% + ${stick.x * 24}px), calc(-50% + ${stick.y * 24}px))`,
                }}
              />
            </div>
          </div>
        </div>
      </div>

      {/* ===== Center: Active key indicator ===== */}
      <div className="flex-1 flex justify-center">
        <div className="flex flex-col items-center gap-1 min-w-[60px]">
          {activeKey ? (
            <>
              <span className="text-[10px] text-zinc-500 uppercase tracking-wider">Active</span>
              <div className="px-3 py-1.5 bg-zinc-700 rounded-lg text-sm font-bold text-white animate-pulse">
                {activeKey.toUpperCase()}
              </div>
              <span className="text-[9px] text-zinc-600">Tap screen to send</span>
            </>
          ) : (
            <span className="text-[10px] text-zinc-600">Select a key</span>
          )}
        </div>
      </div>

      {/* ===== Right: ESC (immediate send, not toggle) ===== */}
      <div className="flex items-center pr-1">
        <button
          {...tap(handleEsc)}
          className="w-14 h-14 rounded-xl font-bold text-xs select-none transition-colors border bg-zinc-800 text-zinc-300 border-zinc-700/60 active:bg-blue-600 active:text-white active:border-blue-400"
        >
          ESC
        </button>
      </div>
    </div>
  );
}
