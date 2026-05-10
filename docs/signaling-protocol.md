# Signaling Protocol

WebSocket 기반. 모든 메시지는 JSON.

## Endpoint

`wss://signal.{domain}/ws`

## Roles

- **host**: PC client (1 per room)
- **viewer**: Mobile web (1+ per room, 보통 1)

## Connection Flow

### 1) Host 등록

```
host -> server: { "type": "register", "role": "host", "room": "ABC123" }
server -> host: { "type": "registered", "role": "host", "room": "ABC123" }
```

room 코드는 사용자가 PC client UI에서 생성/지정. (예: 6자리 영숫자)

### 2) Viewer 입장

```
viewer -> server: { "type": "join", "role": "viewer", "room": "ABC123" }
server -> viewer: { "type": "joined", "room": "ABC123", "hostReady": true }
server -> host:   { "type": "viewer-joined", "viewerId": "v_abc" }
```

room이 없거나 host가 없으면:
```
server -> viewer: { "type": "error", "code": "no-host", "message": "..." }
```

### 3) WebRTC Offer/Answer (host가 offerer)

```
host -> server -> viewer: { "type": "offer", "from": "host", "to": "v_abc", "sdp": "..." }
viewer -> server -> host: { "type": "answer", "from": "v_abc", "to": "host", "sdp": "..." }
```

### 4) ICE candidate trickle

```
{ "type": "ice", "from": "...", "to": "...", "candidate": { ... } }
```

### 5) 연결 완료 후

시그널링 서버는 더 이상 트래픽을 중계하지 않음. (P2P)
연결 종료 시 한쪽이 close하면 서버가 상대에게:

```
{ "type": "peer-left", "peerId": "..." }
```

## Reconnection

- Viewer가 떨어지면 host는 대기 상태로.
- Viewer가 같은 room으로 다시 join하면 시그널링부터 재개.
- Host가 떨어지면 모든 viewer에게 `host-left` 전송 후 room 폐기.

## 보안 메모

- 단순 room code만으로는 제3자 도청/하이재킹 가능.
- Phase 5에서 PSK(host 생성 시 함께 설정한 비밀키)를 도입 — viewer가 join 시 함께 제출하여 검증.
- WSS 필수.

## 메시지 타입 요약

| type | 방향 | 설명 |
|---|---|---|
| register | host -> server | 호스트 등록 |
| registered | server -> host | 등록 완료 |
| join | viewer -> server | 시청자 입장 요청 |
| joined | server -> viewer | 입장 완료 |
| viewer-joined | server -> host | 새 시청자 알림 |
| offer / answer | peer -> peer (relay) | SDP 교환 |
| ice | peer -> peer (relay) | ICE candidate trickle |
| peer-left | server -> peer | 상대 끊김 |
| host-left | server -> viewer | 호스트 종료 |
| error | server -> any | 오류 |
