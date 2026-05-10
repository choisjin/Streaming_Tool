"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { Handedness, InputEvent, MouseInputEvent, KeyboardInputEvent } from "@/lib/types";
import { useTap } from "@/hooks/useTap";

interface VirtualMousePadProps {
  onInput: (event: InputEvent) => void;
  mouseButton: 0 | 2;
  onMouseButtonChange: (button: 0 | 2) => void;
  handedness: Handedness;
}

let cursorX = 0.5;
let cursorY = 0.5;
const MOUSE_SPEED = 0.008;

type KBKey = { key: string; shift?: string; code: string; isAlpha?: boolean };

const ALPHA_ROWS: KBKey[][] = [
  [
    { key: "Q", code: "KeyQ", isAlpha: true },
    { key: "W", code: "KeyW", isAlpha: true },
    { key: "E", code: "KeyE", isAlpha: true },
    { key: "R", code: "KeyR", isAlpha: true },
    { key: "T", code: "KeyT", isAlpha: true },
    { key: "Y", code: "KeyY", isAlpha: true },
    { key: "U", code: "KeyU", isAlpha: true },
    { key: "I", code: "KeyI", isAlpha: true },
    { key: "O", code: "KeyO", isAlpha: true },
    { key: "P", code: "KeyP", isAlpha: true },
  ],
  [
    { key: "A", code: "KeyA", isAlpha: true },
    { key: "S", code: "KeyS", isAlpha: true },
    { key: "D", code: "KeyD", isAlpha: true },
    { key: "F", code: "KeyF", isAlpha: true },
    { key: "G", code: "KeyG", isAlpha: true },
    { key: "H", code: "KeyH", isAlpha: true },
    { key: "J", code: "KeyJ", isAlpha: true },
    { key: "K", code: "KeyK", isAlpha: true },
    { key: "L", code: "KeyL", isAlpha: true },
  ],
  [
    { key: "Z", code: "KeyZ", isAlpha: true },
    { key: "X", code: "KeyX", isAlpha: true },
    { key: "C", code: "KeyC", isAlpha: true },
    { key: "V", code: "KeyV", isAlpha: true },
    { key: "B", code: "KeyB", isAlpha: true },
    { key: "N", code: "KeyN", isAlpha: true },
    { key: "M", code: "KeyM", isAlpha: true },
  ],
];

const NUM_ROWS: KBKey[][] = [
  [
    { key: "1", shift: "!", code: "Digit1" },
    { key: "2", shift: "@", code: "Digit2" },
    { key: "3", shift: "#", code: "Digit3" },
    { key: "4", shift: "$", code: "Digit4" },
    { key: "5", shift: "%", code: "Digit5" },
    { key: "6", shift: "^", code: "Digit6" },
    { key: "7", shift: "&", code: "Digit7" },
    { key: "8", shift: "*", code: "Digit8" },
    { key: "9", shift: "(", code: "Digit9" },
    { key: "0", shift: ")", code: "Digit0" },
  ],
  [
    { key: "-", shift: "_", code: "Minus" },
    { key: "=", shift: "+", code: "Equal" },
    { key: "[", shift: "{", code: "BracketLeft" },
    { key: "]", shift: "}", code: "BracketRight" },
    { key: "\\", shift: "|", code: "Backslash" },
    { key: ";", shift: ":", code: "Semicolon" },
    { key: "'", shift: "\"", code: "Quote" },
    { key: "`", shift: "~", code: "Backquote" },
  ],
  [
    { key: ",", shift: "<", code: "Comma" },
    { key: ".", shift: ">", code: "Period" },
    { key: "/", shift: "?", code: "Slash" },
  ],
];

// 두벌식 한글 각인 매핑 (key letter → 한글, shift한글)
const HAN_MAP: Record<string, { normal: string; shift?: string }> = {
  Q: { normal: "ㅂ", shift: "ㅃ" },
  W: { normal: "ㅈ", shift: "ㅉ" },
  E: { normal: "ㄷ", shift: "ㄸ" },
  R: { normal: "ㄱ", shift: "ㄲ" },
  T: { normal: "ㅅ", shift: "ㅆ" },
  Y: { normal: "ㅛ" },
  U: { normal: "ㅕ" },
  I: { normal: "ㅑ" },
  O: { normal: "ㅐ", shift: "ㅒ" },
  P: { normal: "ㅔ", shift: "ㅖ" },
  A: { normal: "ㅁ" },
  S: { normal: "ㄴ" },
  D: { normal: "ㅇ" },
  F: { normal: "ㄹ" },
  G: { normal: "ㅎ" },
  H: { normal: "ㅗ" },
  J: { normal: "ㅓ" },
  K: { normal: "ㅏ" },
  L: { normal: "ㅣ" },
  Z: { normal: "ㅋ" },
  X: { normal: "ㅌ" },
  C: { normal: "ㅊ" },
  V: { normal: "ㅍ" },
  B: { normal: "ㅠ" },
  N: { normal: "ㅜ" },
  M: { normal: "ㅡ" },
};

function makeKeyEvent(key: string, code: string, action: "down" | "up"): KeyboardInputEvent {
  return {
    type: "keyboard",
    action,
    key,
    code,
    modifiers: { ctrl: false, alt: false, shift: false, meta: false },
  };
}

export default function VirtualMousePad({
  onInput,
  mouseButton,
  onMouseButtonChange,
  handedness,
}: VirtualMousePadProps) {
  const stickRef = useRef<HTMLDivElement>(null);
  const [stickActive, setStickActive] = useState(false);
  const [stickPos, setStickPos] = useState({ x: 0, y: 0 });
  const [capsLock, setCapsLock] = useState(false);
  const [shifted, setShifted] = useState(false);
  const [hanMode, setHanMode] = useState(false);
  const [numLayer, setNumLayer] = useState(false);
  const moveIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const dirRef = useRef({ x: 0, y: 0 });

  const isUpper = capsLock !== shifted;

  useEffect(() => {
    if (stickActive) {
      moveIntervalRef.current = setInterval(() => {
        const { x, y } = dirRef.current;
        if (x === 0 && y === 0) return;
        cursorX = Math.max(0, Math.min(1, cursorX + x * MOUSE_SPEED));
        cursorY = Math.max(0, Math.min(1, cursorY + y * MOUSE_SPEED));
        onInput({ type: "mouse", action: "move", x: cursorX, y: cursorY } as MouseInputEvent);
      }, 16);
    }
    return () => {
      if (moveIntervalRef.current) {
        clearInterval(moveIntervalRef.current);
        moveIntervalRef.current = null;
      }
    };
  }, [stickActive, onInput]);

  const getStickOffset = useCallback((clientX: number, clientY: number) => {
    const rect = stickRef.current?.getBoundingClientRect();
    if (!rect) return { x: 0, y: 0 };
    const cx = rect.left + rect.width / 2;
    const cy = rect.top + rect.height / 2;
    const maxR = rect.width / 2 - 16;
    let dx = clientX - cx;
    let dy = clientY - cy;
    const dist = Math.sqrt(dx * dx + dy * dy);
    if (dist > maxR) { dx = (dx / dist) * maxR; dy = (dy / dist) * maxR; }
    return { x: dx / maxR, y: dy / maxR };
  }, []);

  const onStickStart = useCallback((cx: number, cy: number) => {
    const off = getStickOffset(cx, cy);
    setStickPos(off); dirRef.current = off; setStickActive(true);
  }, [getStickOffset]);

  const onStickMove = useCallback((cx: number, cy: number) => {
    if (!stickActive) return;
    const off = getStickOffset(cx, cy);
    setStickPos(off); dirRef.current = off;
  }, [stickActive, getStickOffset]);

  const onStickEnd = useCallback(() => {
    setStickPos({ x: 0, y: 0 }); dirRef.current = { x: 0, y: 0 }; setStickActive(false);
  }, []);

  const handleClick = useCallback((button: number) => {
    onInput({ type: "mouse", action: "click", x: cursorX, y: cursorY, button } as MouseInputEvent);
  }, [onInput]);

  const selectButton = useCallback(
    (btn: 0 | 2) => {
      onMouseButtonChange(btn);
      handleClick(btn);
    },
    [onMouseButtonChange, handleClick]
  );

  const pressKey = useCallback((key: string, code: string) => {
    onInput(makeKeyEvent(key, code, "down"));
    setTimeout(() => onInput(makeKeyEvent(key, code, "up")), 60);
  }, [onInput]);

  // Toggle handlers — onClick only (prevents double-fire on touch devices)
  const toggleCapsLock = useCallback(() => {
    setCapsLock((p) => !p);
    pressKey("CapsLock", "CapsLock");
  }, [pressKey]);

  const toggleShift = useCallback(() => setShifted((p) => !p), []);

  const toggleNumLayer = useCallback(() => setNumLayer((p) => !p), []);

  // 한/영: toggle local state + send Right Alt (standard Korean IME toggle)
  const toggleHanEng = useCallback(() => {
    setHanMode((p) => !p);
    // Right Alt is the standard 한/영 key on Korean keyboards
    onInput(makeKeyEvent("HangulMode", "AltRight", "down"));
    setTimeout(() => onInput(makeKeyEvent("HangulMode", "AltRight", "up")), 60);
  }, [onInput]);

  const pressKBKey = useCallback((k: KBKey) => {
    if (k.isAlpha) {
      const sendKey = isUpper ? k.key : k.key.toLowerCase();
      if (shifted) {
        onInput(makeKeyEvent("Shift", "ShiftLeft", "down"));
        setTimeout(() => {
          onInput(makeKeyEvent(sendKey, k.code, "down"));
          setTimeout(() => {
            onInput(makeKeyEvent(sendKey, k.code, "up"));
            onInput(makeKeyEvent("Shift", "ShiftLeft", "up"));
          }, 40);
        }, 20);
      } else {
        pressKey(sendKey, k.code);
      }
    } else if (shifted && k.shift) {
      onInput(makeKeyEvent("Shift", "ShiftLeft", "down"));
      setTimeout(() => {
        onInput(makeKeyEvent(k.shift!, k.code, "down"));
        setTimeout(() => {
          onInput(makeKeyEvent(k.shift!, k.code, "up"));
          onInput(makeKeyEvent("Shift", "ShiftLeft", "up"));
        }, 40);
      }, 20);
    } else {
      pressKey(k.key, k.code);
    }
  }, [isUpper, shifted, onInput, pressKey]);

  const getLabel = useCallback((k: KBKey) => {
    if (k.isAlpha && hanMode) {
      const han = HAN_MAP[k.key];
      if (han) return shifted && han.shift ? han.shift : han.normal;
    }
    if (k.isAlpha) return isUpper ? k.key : k.key.toLowerCase();
    if (shifted && k.shift) return k.shift;
    return k.key;
  }, [isUpper, shifted, hanMode]);

  const tap = useTap();
  const rows = numLayer ? NUM_ROWS : ALPHA_ROWS;

  const kbBtn = "flex-1 h-9 bg-zinc-800 active:bg-zinc-600 rounded-md text-[13px] font-medium text-zinc-200 select-none transition-colors border border-zinc-700/50";
  const fnBtn = "h-9 rounded-md text-[11px] font-medium select-none transition-colors border";
  const fnOff = "bg-zinc-800 text-zinc-400 border-zinc-700/50 active:bg-zinc-600";
  const fnCaps = capsLock ? "bg-blue-600 text-white border-blue-400 active:bg-blue-500" : fnOff;
  const fnShift = shifted ? "bg-amber-600 text-white border-amber-400 active:bg-amber-500" : fnOff;
  const fnNum = numLayer ? "bg-indigo-600 text-white border-indigo-400 active:bg-indigo-500" : fnOff;
  const fnHan = hanMode ? "bg-teal-600 text-white border-teal-400 active:bg-teal-500" : fnOff;

  return (
    <div
      className={`flex gap-2 px-2 py-2 select-none ${
        handedness === "right" ? "flex-row-reverse" : ""
      }`}
    >
      {/* Left: Mouse joystick + L/R click */}
      <div className="flex flex-col items-center gap-1 shrink-0">
        <div
          ref={stickRef}
          className="relative w-[72px] h-[72px] rounded-full bg-zinc-800/80 border border-zinc-700 touch-none select-none"
          style={{ touchAction: "none" }}
          onPointerDown={(e) => {
            e.currentTarget.setPointerCapture(e.pointerId);
            onStickStart(e.clientX, e.clientY);
          }}
          onPointerMove={(e) => {
            if (e.currentTarget.hasPointerCapture(e.pointerId)) {
              onStickMove(e.clientX, e.clientY);
            }
          }}
          onPointerUp={(e) => {
            e.currentTarget.releasePointerCapture(e.pointerId);
            onStickEnd();
          }}
          onPointerCancel={(e) => {
            e.currentTarget.releasePointerCapture(e.pointerId);
            onStickEnd();
          }}
        >
          <div
            className="absolute w-7 h-7 rounded-full bg-zinc-500 border-2 border-zinc-400 shadow-md transition-transform duration-75"
            style={{
              left: "50%", top: "50%",
              transform: `translate(calc(-50% + ${stickPos.x * 16}px), calc(-50% + ${stickPos.y * 16}px))`,
            }}
          />
        </div>
        <div className="flex gap-1">
          <button
            {...tap(() => selectButton(0))}
            className={`px-3 py-1 rounded text-[10px] font-medium select-none transition-colors ${
              mouseButton === 0
                ? "bg-blue-600 text-white ring-1 ring-blue-300"
                : "bg-zinc-700 text-zinc-300 active:bg-zinc-500"
            }`}
          >
            L
          </button>
          <button
            {...tap(() => selectButton(2))}
            className={`px-3 py-1 rounded text-[10px] font-medium select-none transition-colors ${
              mouseButton === 2
                ? "bg-blue-600 text-white ring-1 ring-blue-300"
                : "bg-zinc-700 text-zinc-300 active:bg-zinc-500"
            }`}
          >
            R
          </button>
        </div>
      </div>

      {/* Right: Virtual keyboard */}
      <div className="flex-1 flex flex-col gap-0.5 min-w-0">
        {rows.map((row, ri) => (
          <div key={ri} className="flex gap-0.5">
            {ri === 1 && !numLayer && (
              <button {...tap(toggleCapsLock)} className={`${fnBtn} w-10 shrink-0 ${fnCaps}`}>Caps</button>
            )}
            {ri === 2 && (
              <button {...tap(toggleShift)} className={`${fnBtn} w-12 shrink-0 ${fnShift}`}>Shift</button>
            )}
            {row.map((k) => (
              <button key={k.code} {...tap(() => pressKBKey(k))} className={kbBtn}>{getLabel(k)}</button>
            ))}
            {ri === 2 && (
              <button {...tap(() => pressKey("Backspace", "Backspace"))} className={`${fnBtn} w-12 shrink-0 ${fnOff}`}>Bksp</button>
            )}
          </div>
        ))}

        {/* Bottom row */}
        <div className="flex gap-0.5">
          <button {...tap(toggleNumLayer)} className={`${fnBtn} w-10 shrink-0 ${fnNum}`}>{numLayer ? "ABC" : "123"}</button>
          <button {...tap(toggleHanEng)} className={`${fnBtn} w-10 shrink-0 ${fnHan}`}>{hanMode ? "한" : "영"}</button>
          <button {...tap(() => pressKey("Control", "ControlLeft"))} className={`${fnBtn} w-9 shrink-0 ${fnOff}`}>Ctrl</button>
          <button {...tap(() => pressKey(" ", "Space"))} className={`${fnBtn} flex-1 ${fnOff}`}>Space</button>
          <button {...tap(() => pressKey("Enter", "Enter"))} className={`${fnBtn} w-12 shrink-0 ${fnOff}`}>Enter</button>
          <button {...tap(() => pressKey("Escape", "Escape"))} className={`${fnBtn} w-9 shrink-0 ${fnOff}`}>Esc</button>
        </div>
      </div>
    </div>
  );
}
