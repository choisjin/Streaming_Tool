# Streaming_tool — Pro Micro Firmware

USB HID keyboard + mouse for the PC client to drive. To the host OS the board
is a real keyboard and mouse, so input passes anti-cheats that block software
injection (SendInput, keybd_event, PostMessage, etc.).

## Hardware

- **Pro Micro** (ATmega32U4, 5V/16MHz). SparkFun-style or generic clones both work.
- USB cable to the **gaming PC** that runs the streaming host.
- **Important**: the Pro Micro's USB plugs into the gaming PC, not the Mac mini.

## Toolchain

Arduino IDE 2.x or Arduino CLI.

Board: `Arduino Leonardo` (the Pro Micro shares the same MCU).
Libraries: `Keyboard` and `Mouse` (bundled).

```
arduino-cli core install arduino:avr
arduino-cli compile --fqbn arduino:avr:leonardo firmware/pro_micro_hid
arduino-cli upload  --fqbn arduino:avr:leonardo --port COM5 firmware/pro_micro_hid
```

After upload, open Serial Monitor at 115200 — you should see `READY` once.

## Smoke test

Type these in the serial monitor (with newline):
- `KD 97` then `KU 97` -> types `a` in any focused window
- `MC 1` -> left click at the current cursor position
- `MM 50 0` -> moves mouse 50px right
- `PING` -> `PONG` reply

## Failsafe

If no command arrives for 250 ms (SAFE_RELEASE_MS) the firmware releases all
keys and mouse buttons. This prevents a stuck key if the PC client crashes
mid-press. Tune the constant if you stream over very lossy links.

## Wiring notes

The Pro Micro can also drive an extra reset button (RST -> GND momentary) if
you need to recover a borked sketch — useful because once the device starts
spamming keystrokes you may not be able to interact with the IDE.
