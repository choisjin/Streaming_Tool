# Streaming_tool — Arduino Micro / Pro Micro HID Firmware

USB HID keyboard + mouse for the PC client to drive. To the host OS the board
is a real keyboard and mouse, so input passes anti-cheats that block software
injection (SendInput, keybd_event, PostMessage, etc.).

## Hardware

Any **ATmega32U4** board works — the firmware uses Arduino's stock `Keyboard`
and `Mouse` libraries which are present on every 32U4 variant:

- **Arduino Micro** (the official one) — what we ship for. FQBN `arduino:avr:micro`.
- **SparkFun Pro Micro** — pin-compatible clone. FQBN `SparkFun:avr:promicro`
  (needs the SparkFun core installed) or `arduino:avr:leonardo` works too.
- **Arduino Leonardo** — same MCU, larger form factor.

Plug the board's USB cable into the **gaming PC**.

## Toolchain

Arduino IDE 2.x or Arduino CLI.

Pick the board that matches your hardware:
- Arduino Micro    → board "Arduino Micro" (FQBN `arduino:avr:micro`)
- Pro Micro        → board "SparkFun Pro Micro" (FQBN `SparkFun:avr:promicro`)
- Leonardo         → board "Arduino Leonardo" (FQBN `arduino:avr:leonardo`)

Libraries: `Keyboard` and `Mouse` (bundled with the AVR core).

```
arduino-cli core install arduino:avr
arduino-cli compile --fqbn arduino:avr:micro firmware/pro_micro_hid
arduino-cli upload  --fqbn arduino:avr:micro --port COM3 firmware/pro_micro_hid
```

After upload, open Serial Monitor at 115200 — you should see `READY` once.

> The folder name `pro_micro_hid` is historical; the sketch itself is
> board-agnostic and works on Micro / Pro Micro / Leonardo.

## USB device name (so the OS reports a neutral "HID Keyboard")

By default the board reports as "Arduino Micro" / "Arduino Leonardo", which
is fine but recognizable. To make it appear as a generic `HID Keyboard`
composite device, copy `boards.local.txt` from this folder into the Arduino
AVR core directory and re-upload:

```
# Windows location (adjust the version)
%LOCALAPPDATA%\Arduino15\packages\arduino\hardware\avr\<ver>\boards.local.txt
```

The file overrides both `micro.*` and `leonardo.*` build properties so it
covers either board. (See the comments in `boards.local.txt` for details and
the optional VID/PID spoofing block — leave that off unless you know what
you're doing.)

After re-upload, Device Manager shows the board as `HID Keyboard` (HID-compliant
composite device) instead of `Arduino Micro`.

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

A momentary reset button (RST -> GND) is handy: once the device starts
spamming keystrokes you may not be able to interact with the IDE long enough
to hit "upload". Tap reset twice quickly to drop into the bootloader.
