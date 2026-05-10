"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { WebRtcViewer } from "@/lib/webrtcViewer";
import { ConnectionStatus, InputEvent } from "@/lib/types";

/**
 * Connect to a Mac mini signaling server, join a room, and exchange WebRTC
 * offer/answer with the PC host. Returns the live MediaStream + helpers to
 * connect, disconnect, and send input events.
 */
export function useWebRtcViewer(signalingUrl: string, roomCode: string) {
  const viewerRef = useRef<WebRtcViewer | null>(null);
  const [status, setStatus] = useState<ConnectionStatus>("disconnected");
  const [stream, setStream] = useState<MediaStream | null>(null);

  useEffect(() => {
    if (!roomCode) return;
    const v = new WebRtcViewer(signalingUrl, roomCode);
    viewerRef.current = v;
    const u1 = v.onStatusChange(setStatus);
    const u2 = v.onStreamChange(setStream);
    return () => {
      u1();
      u2();
      v.disconnect();
      viewerRef.current = null;
    };
  }, [signalingUrl, roomCode]);

  const connect = useCallback(() => viewerRef.current?.connect(), []);
  const disconnect = useCallback(() => viewerRef.current?.disconnect(), []);
  const sendInput = useCallback(
    (event: InputEvent) => viewerRef.current?.sendInput(event),
    []
  );

  return { status, stream, connect, disconnect, sendInput };
}
