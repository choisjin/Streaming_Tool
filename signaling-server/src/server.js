import { WebSocketServer } from "ws";
import http from "node:http";
import { randomBytes } from "node:crypto";

const PORT = Number(process.env.PORT) || 8080;

/**
 * In-memory room registry.
 *
 * room := {
 *   code: string,            // 6-char room code, also used as map key
 *   host: WebSocket | null,
 *   viewers: Map<string, WebSocket>   // viewerId -> ws
 * }
 */
const rooms = new Map();

const httpServer = http.createServer((req, res) => {
  if (req.url === "/health") {
    res.writeHead(200, { "content-type": "text/plain" });
    res.end("ok");
    return;
  }
  res.writeHead(404);
  res.end();
});

const wss = new WebSocketServer({ server: httpServer, path: "/ws" });

function send(ws, msg) {
  if (ws.readyState === ws.OPEN) {
    ws.send(JSON.stringify(msg));
  }
}

function newViewerId() {
  return "v_" + randomBytes(4).toString("hex");
}

function cleanupSocket(ws) {
  const meta = ws._meta;
  if (!meta) return;
  const room = rooms.get(meta.room);
  if (!room) return;

  if (meta.role === "host") {
    // Host gone -> notify viewers + drop room
    for (const [vid, vws] of room.viewers) {
      send(vws, { type: "host-left" });
      try {
        vws.close();
      } catch {}
    }
    rooms.delete(meta.room);
    log(`[room ${meta.room}] host left, room destroyed`);
  } else if (meta.role === "viewer") {
    room.viewers.delete(meta.viewerId);
    if (room.host) {
      send(room.host, { type: "viewer-left", viewerId: meta.viewerId });
    }
    log(`[room ${meta.room}] viewer ${meta.viewerId} left`);
  }
}

function log(...args) {
  console.log(new Date().toISOString(), ...args);
}

wss.on("connection", (ws, req) => {
  log(`connection from ${req.socket.remoteAddress}`);

  ws.on("message", (data) => {
    let msg;
    try {
      msg = JSON.parse(data.toString());
    } catch {
      send(ws, { type: "error", code: "bad-json" });
      return;
    }

    switch (msg.type) {
      case "register": {
        // host
        if (msg.role !== "host" || !msg.room) {
          send(ws, { type: "error", code: "bad-register" });
          return;
        }
        const existing = rooms.get(msg.room);
        if (existing && existing.host && existing.host !== ws) {
          send(ws, { type: "error", code: "room-occupied" });
          return;
        }
        const room = existing ?? {
          code: msg.room,
          host: null,
          viewers: new Map(),
        };
        room.host = ws;
        rooms.set(msg.room, room);
        ws._meta = { role: "host", room: msg.room };
        send(ws, { type: "registered", role: "host", room: msg.room });
        log(`[room ${msg.room}] host registered`);
        break;
      }

      case "join": {
        // viewer
        if (msg.role !== "viewer" || !msg.room) {
          send(ws, { type: "error", code: "bad-join" });
          return;
        }
        const room = rooms.get(msg.room);
        if (!room || !room.host) {
          send(ws, { type: "error", code: "no-host" });
          return;
        }
        const viewerId = newViewerId();
        room.viewers.set(viewerId, ws);
        ws._meta = { role: "viewer", room: msg.room, viewerId };
        send(ws, {
          type: "joined",
          room: msg.room,
          viewerId,
          hostReady: true,
        });
        send(room.host, { type: "viewer-joined", viewerId });
        log(`[room ${msg.room}] viewer ${viewerId} joined`);
        break;
      }

      case "offer":
      case "answer":
      case "ice": {
        // relay between peers
        const meta = ws._meta;
        if (!meta) {
          send(ws, { type: "error", code: "not-registered" });
          return;
        }
        const room = rooms.get(meta.room);
        if (!room) {
          send(ws, { type: "error", code: "no-room" });
          return;
        }
        // From host -> viewer (msg.to = viewerId)
        // From viewer -> host
        let target;
        if (meta.role === "host") {
          target = room.viewers.get(msg.to);
        } else {
          target = room.host;
        }
        if (!target) {
          send(ws, { type: "error", code: "no-peer" });
          return;
        }
        // tag origin
        const out = { ...msg, from: meta.role === "host" ? "host" : meta.viewerId };
        send(target, out);
        break;
      }

      default:
        send(ws, { type: "error", code: "unknown-type" });
    }
  });

  ws.on("close", () => {
    cleanupSocket(ws);
  });

  ws.on("error", (err) => {
    log("ws error:", err.message);
  });
});

httpServer.listen(PORT, () => {
  log(`signaling server listening on :${PORT} (path=/ws, health=/health)`);
});
