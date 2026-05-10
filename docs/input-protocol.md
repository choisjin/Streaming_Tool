# Input Protocol

모바일 -> PC -> Pro Micro 입력 명령 흐름.

## 1단계: Mobile -> PC (WebRTC DataChannel)

DataChannel label: `"input"`. ordered + reliable.

기존 모바일 코드의 `InputEvent`(src/lib/types.ts)를 그대로 직렬화하여 전송:

### Keyboard event
```json
{
  "type": "keyboard",
  "action": "down" | "up",
  "key": "a",
  "code": "KeyA",
  "modifiers": { "ctrl": false, "alt": false, "shift": false, "meta": false }
}
```

### Mouse event
```json
{
  "type": "mouse",
  "action": "move" | "down" | "up" | "click" | "dblclick" | "scroll",
  "x": 0.5,         // normalized 0-1 (캡처 영역 기준)
  "y": 0.3,
  "button": 0,      // 0=L, 1=M, 2=R (있을 때만)
  "deltaX": 0,      // scroll 시
  "deltaY": -120
}
```

## 2단계: PC -> Pro Micro (Serial USB-CDC)

### 포트 설정
- baud: **115200**
- 8N1, no flow control
- LF 종료 (`\n`)

### 명령 포맷

ASCII 한 줄 = 한 명령. 짧고 빠르게 처리되도록 단순 토큰 기반.

#### Keyboard
```
KD <code>      // key down (Arduino Keyboard.press)
KU <code>      // key up   (Arduino Keyboard.release)
KA             // releaseAll
```

`<code>` 는 [Arduino Keyboard 라이브러리 modifier/key 정의](https://www.arduino.cc/reference/en/language/functions/usb/keyboard/keyboardmodifiers/)와 일치하는 정수값(0–255).

예:
- `KD 97` → 'a' down
- `KU 97` → 'a' up
- `KD 218` → ↑ (KEY_UP_ARROW = 0xDA = 218)

#### Mouse
```
MM <dx> <dy>           // 상대 이동 (signed int16, -32768..32767)
MD <button>            // mouse down (1=L, 2=R, 4=M  ← Arduino Mouse 마스크)
MU <button>            // mouse up
MC <button>            // click (down+up)
MS <dx> <dy>           // scroll (대부분 dx=0; dy: 양수=up)
```

#### 기타
```
PING                   // 헬스체크 → "PONG"
RESET                  // releaseAll + 모든 버튼 release
```

### 좌표 변환 (정규화 -> 상대 이동)

모바일에서 보내는 mouse는 **정규화된 절대 좌표**(0–1)지만, Pro Micro의 USB HID 마우스는 **상대 이동**만 지원(절대 좌표는 ABS_X 디스크립터 필요, 게임 호환성 떨어짐).

PC 클라이언트가 변환:
```
가상 커서 위치 (vx, vy)를 PC 클라이언트가 추적.
새 이벤트 (nx, ny) 도착:
  dx = (nx - vx) * SCREEN_W
  dy = (ny - vy) * SCREEN_H
  Pro Micro에 "MM dx dy" 전송 (큰 값은 여러 번 나눠 전송, int16 범위)
  vx, vy 갱신
```

게임 입장에서는 하드웨어 마우스로 `dx, dy`만큼 움직인 것과 동일.

### 응답

Pro Micro는 평소 응답 안 보냄(지연 최소화). 단:
- `PING` -> `PONG\n`
- 잘못된 명령 -> `ERR <line>\n`
- 부팅 직후 `READY\n`

## 안정성

- PC 클라이언트는 시리얼 큐를 두고 1ms 단위로 flush.
- WebRTC DataChannel이 끊기면 즉시 `KA`(release all) + Mouse `MU 7`(모든 버튼 up) 전송 → 게임에서 키가 눌린 채로 남는 것 방지.
- Pro Micro 펌웨어도 250ms 동안 명령 없으면 자체 release all (안전 장치).

## Phase 4 작업 시 결정해야 할 것

- 한글 IME: Pro Micro는 자국 언어 키 그대로 보냄. PC측 IME 상태 동기화 어려움 — Phase 4에서 별도 검토.
- 마우스 정확도: 게임이 raw input을 받으면 OS pointer ballistic이 영향 안 줌 → 좋음. 단, PC측 윈도우 마우스 가속이 켜져 있으면 절대 좌표 어긋남 → 캡쳐 영역 + 게임 해상도 매핑 필요.
