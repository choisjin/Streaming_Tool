import { ConnectionStatus, InputEvent } from "./types";

type StatusCallback = (status: ConnectionStatus) => void;
type StreamCallback = (stream: MediaStream | null) => void;

interface SignalingMessage {
  type: string;
  [key: string]: unknown;
}

const DEFAULT_ICE_SERVERS: RTCIceServer[] = [
  { urls: "stun:stun.l.google.com:19302" },
];

/**
 * WebRTC viewer that connects to the signaling server, joins a room as a
 * `viewer`, completes offer/answer with the host (PC), and exposes the
 * received MediaStream + a DataChannel for sending input events.
 */
export class WebRtcViewer {
  private ws: WebSocket | null = null;
  private pc: RTCPeerConnection | null = null;
  private inputChannel: RTCDataChannel | null = null;
  private viewerId: string | null = null;
  private _status: ConnectionStatus = "disconnected";
  private _stream: MediaStream | null = null;
  private statusCallbacks = new Set<StatusCallback>();
  private streamCallbacks = new Set<StreamCallback>();
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private wantConnected = false;

  constructor(
    private signalingUrl: string,
    private roomCode: string,
    private iceServers: RTCIceServer[] = DEFAULT_ICE_SERVERS
  ) {}

  get status() {
    return this._status;
  }

  onStatusChange(cb: StatusCallback): () => void {
    this.statusCallbacks.add(cb);
    return () => this.statusCallbacks.delete(cb);
  }

  onStreamChange(cb: StreamCallback): () => void {
    this.streamCallbacks.add(cb);
    return () => this.streamCallbacks.delete(cb);
  }

  connect() {
    this.wantConnected = true;
    this.openSignaling();
  }

  disconnect() {
    this.wantConnected = false;
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
    this.closeAll();
    this.setStatus("disconnected");
  }

  sendInput(event: InputEvent) {
    if (this.inputChannel?.readyState === "open") {
      this.inputChannel.send(JSON.stringify(event));
    }
  }

  // --- internals ---

  private openSignaling() {
    if (this.ws) this.ws.close();
    this.setStatus("connecting");

    let ws: WebSocket;
    try {
      ws = new WebSocket(this.signalingUrl);
    } catch {
      this.setStatus("error");
      this.scheduleReconnect();
      return;
    }
    this.ws = ws;

    ws.onopen = () => {
      this.send({ type: "join", role: "viewer", room: this.roomCode });
    };
    ws.onmessage = (ev) => {
      let msg: SignalingMessage;
      try {
        msg = JSON.parse(ev.data);
      } catch {
        return;
      }
      this.handleSignalingMessage(msg);
    };
    ws.onerror = () => {
      this.setStatus("error");
    };
    ws.onclose = () => {
      this.closePeer();
      this.setStatus("disconnected");
      if (this.wantConnected) this.scheduleReconnect();
    };
  }

  private async handleSignalingMessage(msg: SignalingMessage) {
    switch (msg.type) {
      case "joined": {
        this.viewerId = msg.viewerId as string;
        await this.createPeer();
        break;
      }
      case "offer": {
        if (!this.pc) await this.createPeer();
        await this.pc!.setRemoteDescription({
          type: "offer",
          sdp: msg.sdp as string,
        });
        const answer = await this.pc!.createAnswer();
        await this.pc!.setLocalDescription(answer);
        this.send({ type: "answer", sdp: answer.sdp });
        break;
      }
      case "ice": {
        const cand = msg.candidate as RTCIceCandidateInit | null;
        if (cand && this.pc) {
          try {
            await this.pc.addIceCandidate(cand);
          } catch (err) {
            console.warn("[webrtc] addIceCandidate failed", err);
          }
        }
        break;
      }
      case "host-left":
      case "peer-left": {
        this.closePeer();
        this.setStatus("disconnected");
        break;
      }
      case "error": {
        console.warn("[signaling] error:", msg.code);
        this.setStatus("error");
        break;
      }
    }
  }

  private async createPeer() {
    const pc = new RTCPeerConnection({ iceServers: this.iceServers });
    this.pc = pc;

    pc.ontrack = (e) => {
      this._stream = e.streams[0] ?? new MediaStream([e.track]);
      // Tell the browser we want minimum playout latency. Without this hint
      // Chrome buffers ~80-150 ms to absorb network jitter, which is fine for
      // a video call but is exactly the lag we're trying to remove for remote
      // play. 0 means "render the frame as soon as it's decodable".
      const recv = e.receiver as RTCRtpReceiver & { playoutDelayHint?: number };
      try { recv.playoutDelayHint = 0; } catch { /* not supported on this browser */ }
      this.streamCallbacks.forEach((cb) => cb(this._stream));
    };

    pc.onicecandidate = (e) => {
      if (e.candidate) {
        this.send({ type: "ice", candidate: e.candidate.toJSON() });
      }
    };

    pc.onconnectionstatechange = () => {
      const s = pc.connectionState;
      if (s === "connected") this.setStatus("connected");
      else if (s === "failed" || s === "disconnected") this.setStatus("error");
      else if (s === "closed") this.setStatus("disconnected");
    };

    // Pre-create input data channel; PC will see it via ondatachannel.
    const dc = pc.createDataChannel("input", { ordered: true });
    this.inputChannel = dc;
  }

  private send(msg: SignalingMessage) {
    if (this.ws?.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify(msg));
    }
  }

  private closePeer() {
    this.inputChannel?.close();
    this.inputChannel = null;
    this.pc?.close();
    this.pc = null;
    if (this._stream) {
      this._stream = null;
      this.streamCallbacks.forEach((cb) => cb(null));
    }
  }

  private closeAll() {
    this.closePeer();
    if (this.ws) {
      this.ws.onclose = null;
      this.ws.close();
      this.ws = null;
    }
  }

  private scheduleReconnect() {
    if (this.reconnectTimer || !this.wantConnected) return;
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      if (this.wantConnected) this.openSignaling();
    }, 3000);
  }

  private setStatus(s: ConnectionStatus) {
    if (this._status === s) return;
    this._status = s;
    this.statusCallbacks.forEach((cb) => cb(s));
  }
}
