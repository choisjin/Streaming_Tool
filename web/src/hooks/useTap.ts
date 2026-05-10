"use client";

import { useCallback } from "react";

/**
 * Returns event handlers for a "tap" action that works on mouse, touch, and pen
 * via Pointer Events — fires once per tap, no double-fire on mobile.
 *
 * Usage: <button {...tap(myAction)}>Click</button>
 */
export function useTap() {
  const tap = useCallback((action: () => void) => {
    return {
      onPointerDown: (e: React.PointerEvent) => {
        // Only respond to primary button / primary touch
        if (e.button !== undefined && e.button !== 0) return;
        action();
      },
      // Keep style consistent: prevent context menu on long-press for touch
      onContextMenu: (e: React.MouseEvent) => {
        e.preventDefault();
      },
      style: { touchAction: "manipulation" as const },
    };
  }, []);

  return tap;
}
