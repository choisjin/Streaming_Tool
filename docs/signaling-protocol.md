# Signaling Protocol

WebSocket. 모든 메시지는 JSON. 서버는 PC 클라이언트 자체에 내장(외부 릴레이 없음).

## Endpoint

```
ws://<PC IP>:<port>/ws        # default port 8080
http://<PC IP>:<port>/health  # returns "ok"
```

LAN: PC의 사설 IP (예: 192.168.0.10)
WAN: 공인 IP + 공유기 포트포워딩.

## 메시지

### 1) Viewer 입장

```
viewer -> server: { "type": "join", "room": "ABC123" }
```

room이 일치하지 않으면:
```
server -> viewer: { "type": "error", "code": "bad-room" }
```

OK인 경우 viewer 등록 + 호스트(WebRTC peer)가 즉시 offer 생성:
```
server -> viewer: { "type": "joined", "viewerId": "v_xxxxxxxx", "hostReady": true }
server -> viewer: { "type": "offer",  "sdp": "..." }
```

### 2) Answer

```
viewer -> server: { "type": "answer", "sdp": "..." }
```

### 3) ICE candidate trickle (양방향)

```
{ "type": "ice", "candidate": { ... } }
```

### 4) 종료

viewer 측 close → 서버는 해당 ViewerSession을 정리.
host 측 종료 → 모든 viewer 소켓 close.

## 보안 메모

- room code(6자) 단독으로는 단순 페어링용. 같은 LAN/포트에 접근 가능한 누구나 시도 가능.
- Phase 5에서 PSK(공유 비밀키) 또는 짧은 OTP 추가로 강화.
- TLS 미적용 단계에서는 같은 LAN 또는 신뢰 가능한 네트워크에서만 사용 권장.

## 메시지 타입 요약

| type | 방향 | 설명 |
|---|---|---|
| join | viewer → server | room 코드와 함께 입장 |
| joined | server → viewer | 입장 완료 (viewerId 부여) |
| offer | server → viewer | host가 생성한 SDP offer |
| answer | viewer → server | viewer SDP answer |
| ice | both | ICE candidate trickle |
| error | server → viewer | 오류 (`bad-room`, `not-joined`, …) |
