/*
 * Streaming_tool - Pro Micro HID firmware
 *
 * Receives a tiny ASCII protocol over USB serial (115200 8N1) from the PC
 * client and emits real USB HID keyboard / mouse reports. To the OS this
 * board is indistinguishable from a physical keyboard + mouse, so anti-cheats
 * that block software input injection (SendInput etc.) cannot block it.
 *
 * Protocol (one command per line, '\n' terminator):
 *   KD <code>          key down   (0..255, Arduino Keyboard.h key codes)
 *   KU <code>          key up
 *   KA                 release all keys
 *   MM <dx> <dy>       mouse move, signed int16 each axis
 *   MD <buttons>       mouse buttons down (bitmask: 1=L 2=R 4=M)
 *   MU <buttons>       mouse buttons up
 *   MC <buttons>       click (down + up)
 *   MS <dx> <dy>       scroll (typically dx=0, dy positive=up)
 *   PING               heartbeat
 *   RESET              release all keys + buttons
 *
 * Safety: if no command arrives for SAFE_RELEASE_MS milliseconds, all keys
 * and buttons are released (failsafe in case the PC client crashes).
 */

#include <Keyboard.h>
#include <Mouse.h>

static const unsigned long SAFE_RELEASE_MS = 250;
static unsigned long g_lastCmdMs = 0;
static char buf[64];
static uint8_t buflen = 0;

void setup() {
  Serial.begin(115200);
  Keyboard.begin();
  Mouse.begin();
  // Wait briefly for the host to open the port; don't block forever.
  unsigned long deadline = millis() + 2000;
  while (!Serial && millis() < deadline) { /* spin */ }
  Serial.println("READY");
}

static void releaseAll() {
  Keyboard.releaseAll();
  Mouse.release(MOUSE_LEFT | MOUSE_RIGHT | MOUSE_MIDDLE);
}

static void handleLine(char* line) {
  g_lastCmdMs = millis();

  if (line[0] == 'K' && line[1] == 'A') { releaseAll(); return; }
  if (line[0] == 'K' && line[1] == 'D') {
    int code = atoi(line + 3);
    if (code > 0 && code < 256) Keyboard.press((uint8_t)code);
    return;
  }
  if (line[0] == 'K' && line[1] == 'U') {
    int code = atoi(line + 3);
    if (code > 0 && code < 256) Keyboard.release((uint8_t)code);
    return;
  }
  if (line[0] == 'M' && line[1] == 'M') {
    int dx = 0, dy = 0;
    sscanf(line + 3, "%d %d", &dx, &dy);
    // Arduino Mouse.move takes signed char; chunk large deltas
    while (dx > 127 || dx < -127 || dy > 127 || dy < -127) {
      int8_t cx = (dx > 127) ? 127 : (dx < -127 ? -127 : dx);
      int8_t cy = (dy > 127) ? 127 : (dy < -127 ? -127 : dy);
      Mouse.move(cx, cy, 0);
      dx -= cx; dy -= cy;
    }
    Mouse.move((int8_t)dx, (int8_t)dy, 0);
    return;
  }
  if (line[0] == 'M' && (line[1] == 'D' || line[1] == 'U' || line[1] == 'C')) {
    int btn = atoi(line + 3);
    uint8_t mask = (uint8_t)btn;
    if (line[1] == 'D') Mouse.press(mask);
    else if (line[1] == 'U') Mouse.release(mask);
    else { Mouse.press(mask); delay(8); Mouse.release(mask); }
    return;
  }
  if (line[0] == 'M' && line[1] == 'S') {
    int dx = 0, dy = 0;
    sscanf(line + 3, "%d %d", &dx, &dy);
    Mouse.move(0, 0, (int8_t)dy);
    return;
  }
  if (line[0] == 'P' && line[1] == 'I') { Serial.println("PONG"); return; }
  if (line[0] == 'R' && line[1] == 'E') { releaseAll(); return; }

  // Unknown
  Serial.print("ERR ");
  Serial.println(line);
}

void loop() {
  while (Serial.available()) {
    char c = (char)Serial.read();
    if (c == '\r') continue;
    if (c == '\n') {
      buf[buflen] = 0;
      if (buflen > 0) handleLine(buf);
      buflen = 0;
      continue;
    }
    if (buflen < sizeof(buf) - 1) buf[buflen++] = c;
    else buflen = 0; // overflow: drop line
  }

  // Failsafe
  if (g_lastCmdMs != 0 && (millis() - g_lastCmdMs) > SAFE_RELEASE_MS) {
    releaseAll();
    g_lastCmdMs = 0; // arm only on next command
  }
}
