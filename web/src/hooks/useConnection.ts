"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { RemoteConnection } from "@/lib/websocket";
import { ConnectionStatus, InputEvent } from "@/lib/types";

export function useConnection(url: string) {
  const connRef = useRef<RemoteConnection | null>(null);
  const [status, setStatus] = useState<ConnectionStatus>("disconnected");

  useEffect(() => {
    const conn = new RemoteConnection(url);
    connRef.current = conn;
    const unsub = conn.onStatusChange(setStatus);
    return () => {
      unsub();
      conn.disconnect();
    };
  }, [url]);

  const connect = useCallback(() => {
    connRef.current?.connect();
  }, []);

  const disconnect = useCallback(() => {
    connRef.current?.disconnect();
  }, []);

  const sendInput = useCallback((event: InputEvent) => {
    connRef.current?.sendInput(event);
  }, []);

  return { status, connect, disconnect, sendInput };
}
