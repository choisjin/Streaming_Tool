using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StreamingHost.Input;

/// <summary>
/// Translates the JSON input events that arrive on the WebRTC DataChannel into
/// the tiny ASCII protocol consumed by <see cref="SerialBridge"/> (and thus by
/// the Pro Micro firmware). Keeps a virtual cursor for mouse — the firmware's
/// USB HID mouse only supports relative motion, so we convert absolute screen
/// coordinates from the mobile to per-frame deltas.
/// </summary>
public sealed class InputRouter
{
    private readonly SerialBridge _serial;
    private readonly int _screenWidth;
    private readonly int _screenHeight;

    // Virtual cursor in pixels; tracked so we can convert absolute (x,y) clicks
    // from the touch surface into the deltas the HID mouse needs.
    private double _vx;
    private double _vy;

    public event Action<string>? Diagnostic;

    public InputRouter(SerialBridge serial, int screenWidth, int screenHeight)
    {
        _serial = serial;
        _screenWidth = screenWidth;
        _screenHeight = screenHeight;
        _vx = screenWidth / 2.0;
        _vy = screenHeight / 2.0;
    }

    /// <summary>Feed one raw JSON message (the DataChannel payload) and let it
    /// turn into one or more serial commands.</summary>
    public void Handle(string json)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch { Diagnostic?.Invoke("input: bad json"); return; }
        if (node is null) return;

        var type = node["type"]?.GetValue<string>();
        switch (type)
        {
            case "keyboard": HandleKeyboard(node); break;
            case "mouse":    HandleMouse(node);    break;
            default:         Diagnostic?.Invoke($"input: unknown type \"{type}\""); break;
        }
    }

    // --- keyboard ---

    private void HandleKeyboard(JsonNode node)
    {
        var action = node["action"]?.GetValue<string>();   // "down" | "up"
        var key    = node["key"]?.GetValue<string>() ?? "";
        var code   = node["code"]?.GetValue<string>() ?? "";

        var hid = MapKey(key, code);
        if (hid == 0)
        {
            Diagnostic?.Invoke($"input: unmapped key key=\"{key}\" code=\"{code}\"");
            return;
        }

        var cmd = action switch
        {
            "down" => $"KD {hid}",
            "up"   => $"KU {hid}",
            _      => null,
        };
        if (cmd is not null) _serial.SendLine(cmd);
    }

    /// <summary>
    /// Map a JS keyboard event (key + code) to the integer Arduino Keyboard.h
    /// uses with Keyboard.press()/release(). Source of truth: the modifier and
    /// non-printable defines in Arduino's USBAPI.h.
    /// </summary>
    private static byte MapKey(string key, string code)
    {
        // Special / non-printable keys first (matched by event.key, then code).
        switch (key)
        {
            case "Backspace": return 0xB2;
            case "Tab":       return 0xB3;
            case "Enter":     return 0xB0;
            case "Escape":    return 0xB1;
            case " ":         return 0x20;     // space
            case "Shift":     return 0x81;     // LEFT_SHIFT
            case "Control":   return 0x80;     // LEFT_CTRL
            case "Alt":       return 0x82;     // LEFT_ALT
            case "Meta":      return 0x83;     // LEFT_GUI
            case "CapsLock":  return 0xC1;
            case "ArrowUp":   return 0xDA;
            case "ArrowDown": return 0xD9;
            case "ArrowLeft": return 0xD8;
            case "ArrowRight":return 0xD7;
            case "Home":      return 0xD2;
            case "End":       return 0xD5;
            case "PageUp":    return 0xD3;
            case "PageDown":  return 0xD6;
            case "Insert":    return 0xD1;
            case "Delete":    return 0xD4;
            case "F1": return 0xC2; case "F2": return 0xC3; case "F3": return 0xC4;
            case "F4": return 0xC5; case "F5": return 0xC6; case "F6": return 0xC7;
            case "F7": return 0xC8; case "F8": return 0xC9; case "F9": return 0xCA;
            case "F10": return 0xCB; case "F11": return 0xCC; case "F12": return 0xCD;
        }

        // Korean IME hangul/hanja toggle from VirtualMousePad — we send Right Alt,
        // which is the Korean keyboard's standard 한/영 key.
        if (key == "HangulMode") return 0x86; // RIGHT_ALT

        // Printable ASCII — mobile already sends 'a'/'A'/'1'/'!' etc as event.key.
        // Arduino Keyboard accepts any 0x20–0x7E directly.
        if (key.Length == 1)
        {
            var c = (byte)key[0];
            if (c is >= 0x20 and <= 0x7E) return c;
        }

        // Fallback: derive from event.code for letter/digit/punctuation when key
        // is something unexpected (shifted symbols on mobile keyboards differ).
        if (code.StartsWith("Key") && code.Length == 4) return (byte)(char.ToLower(code[3]));
        if (code.StartsWith("Digit") && code.Length == 6) return (byte)code[5];

        // (the explicit casts above keep the compiler happy on char -> byte.)

        return 0; // unmapped
    }

    // --- mouse ---

    private void HandleMouse(JsonNode node)
    {
        var action = node["action"]?.GetValue<string>();
        var x = node["x"]?.GetValue<double>();
        var y = node["y"]?.GetValue<double>();
        var button = node["button"]?.GetValue<int>() ?? 0;

        switch (action)
        {
            case "move":
                if (x is null || y is null) return;
                MoveAbsolute(x.Value, y.Value);
                break;

            case "down":
            case "up":
            case "click":
            case "dblclick":
                if (x is not null && y is not null) MoveAbsolute(x.Value, y.Value);
                var mask = ButtonMaskFromIndex(button);
                var cmd = action switch
                {
                    "down"     => $"MD {mask}",
                    "up"       => $"MU {mask}",
                    "click"    => $"MC {mask}",
                    "dblclick" => $"MC {mask}",       // firmware doesn't expose dbl natively; one click is enough
                    _ => null,
                };
                if (cmd is not null) _serial.SendLine(cmd);
                if (action == "dblclick") _serial.SendLine($"MC {mask}");
                break;

            case "scroll":
                var dx = (int)(node["deltaX"]?.GetValue<double>() ?? 0);
                var dy = (int)(node["deltaY"]?.GetValue<double>() ?? 0);
                // Browser scroll wheels are inverted relative to HID conventions;
                // dy>0 = scroll down for the user, but HID mouse wheel report is
                // dy>0 = scroll up. Flip.
                _serial.SendLine($"MS {dx} {-dy}");
                break;
        }
    }

    /// <summary>
    /// Convert the mobile's normalized (0..1) coordinates to the equivalent
    /// HID relative motion. We track the virtual cursor and emit only the delta.
    /// Large jumps are split into multiple chunks because Arduino Mouse.move
    /// accepts only int8.
    /// </summary>
    private void MoveAbsolute(double nx, double ny)
    {
        nx = Math.Clamp(nx, 0, 1);
        ny = Math.Clamp(ny, 0, 1);
        var tx = nx * _screenWidth;
        var ty = ny * _screenHeight;
        var dx = (int)Math.Round(tx - _vx);
        var dy = (int)Math.Round(ty - _vy);
        if (dx == 0 && dy == 0) return;
        _vx = tx;
        _vy = ty;
        _serial.SendLine($"MM {dx} {dy}");
    }

    /// <summary>
    /// Mobile sends a JS-style button index: 0=L, 1=Middle, 2=R. The firmware /
    /// HID protocol uses a bitmask: 1=L, 2=R, 4=M.
    /// </summary>
    private static int ButtonMaskFromIndex(int btn) => btn switch
    {
        0 => 1,
        1 => 4,
        2 => 2,
        _ => 1,
    };
}
