using System;
using System.Collections.Generic;
using Raylib_cs;

namespace Scratch;

/// <summary>Mouse and keyboard, mapped to Scratch's stage coordinates and key names.</summary>
public sealed class InputState
{
    public double MouseX, MouseY;
    public bool MouseDown;
    /// <summary>Keys currently held, as Scratch names them ("A", "space", "up arrow", ...).</summary>
    readonly List<string> _pressed = new();
    readonly Dictionary<KeyboardKey, string> _physical = new();
    bool _pressStartedOnStage;

    // Set by the automation driver (testing) to replace the real mouse.
    public (double x, double y, bool down)? MouseOverride;
    public (bool down, double dx, double dy)? LookOverride;

    /// <summary>Right mouse button held for "mouse look" (not a Scratch feature; used by the 3D camera).</summary>
    public bool RightMouseDown;
    /// <summary>How far the mouse moved this frame while the right button is held, in stage units (up = positive).</summary>
    public double LookDeltaX, LookDeltaY;
    /// <summary>Mouse wheel this frame (positive = away from you) and middle-button drag in stage units.</summary>
    public double WheelMove, PanDeltaX, PanDeltaY;
    public bool MiddleMouseDown;
    bool _looking, _lookJustStarted;
    System.Numerics.Vector2 _lookStartPos;

    static readonly Dictionary<KeyboardKey, string> Named = new()
    {
        [KeyboardKey.Space] = "space", [KeyboardKey.Left] = "left arrow", [KeyboardKey.Right] = "right arrow",
        [KeyboardKey.Up] = "up arrow", [KeyboardKey.Down] = "down arrow", [KeyboardKey.Enter] = "enter",
        [KeyboardKey.KpEnter] = "enter", [KeyboardKey.Backspace] = "backspace", [KeyboardKey.Delete] = "delete",
        [KeyboardKey.LeftShift] = "shift", [KeyboardKey.RightShift] = "shift", [KeyboardKey.CapsLock] = "caps lock",
        [KeyboardKey.ScrollLock] = "scroll lock", [KeyboardKey.LeftControl] = "control", [KeyboardKey.RightControl] = "control",
        [KeyboardKey.Escape] = "escape", [KeyboardKey.Insert] = "insert", [KeyboardKey.Home] = "home",
        [KeyboardKey.End] = "end", [KeyboardKey.PageUp] = "page up", [KeyboardKey.PageDown] = "page down",
    };

    static readonly HashSet<string> KeyNames = new(Named.Values) { "any" };

    static string? UsLayoutChar(KeyboardKey k)
    {
        int c = (int)k;
        if (c >= 'A' && c <= 'Z') return ((char)c).ToString();
        if (c >= '0' && c <= '9') return ((char)c).ToString();
        if (k >= KeyboardKey.Kp0 && k <= KeyboardKey.Kp9) return ((char)('0' + (k - KeyboardKey.Kp0))).ToString();
        return k switch
        {
            KeyboardKey.Grave => "`", KeyboardKey.Minus => "-", KeyboardKey.Equal => "=",
            KeyboardKey.LeftBracket => "[", KeyboardKey.RightBracket => "]", KeyboardKey.Backslash => "\\",
            KeyboardKey.Semicolon => ";", KeyboardKey.Apostrophe => "'", KeyboardKey.Comma => ",",
            KeyboardKey.Period => ".", KeyboardKey.Slash => "/", KeyboardKey.KpDecimal => ".",
            KeyboardKey.KpDivide => "/", KeyboardKey.KpMultiply => "*", KeyboardKey.KpSubtract => "-",
            KeyboardKey.KpAdd => "+", _ => null,
        };
    }

    public void Update(Engine engine, Rectangle stage)
    {
        // ---- mouse ----
        if (MouseOverride is { } m)
        {
            MouseX = m.x; MouseY = m.y; MouseDown = m.down;
            var look = LookOverride ?? (false, 0, 0);
            RightMouseDown = look.down; LookDeltaX = look.dx; LookDeltaY = look.dy;
        }
        else
        {
            var p = Raylib.GetMousePosition();
            float dpi = Raylib.GetRenderWidth() / (float)Math.Max(1, Raylib.GetScreenWidth());
            double px = p.X * dpi, py = p.Y * dpi;
            bool inside = px >= stage.X && py >= stage.Y && px < stage.X + stage.Width && py < stage.Y + stage.Height;
            UpdateMouseLook(dpi, stage, inside);
            WheelMove = inside ? Raylib.GetMouseWheelMove() : 0;
            MiddleMouseDown = Raylib.IsMouseButtonDown(MouseButton.Middle) && inside;
            var md = Raylib.GetMouseDelta();
            PanDeltaX = MiddleMouseDown ? md.X * dpi * 480 / stage.Width : 0;
            PanDeltaY = MiddleMouseDown ? -md.Y * dpi * 360 / stage.Height : 0;
            if (!_looking) // the pointer stays put while looking around
            {
                MouseX = Ops.Clamp(480 * ((px - stage.X) / stage.Width - 0.5), -240, 240);
                MouseY = Ops.Clamp(-360 * ((py - stage.Y) / stage.Height - 0.5), -180, 180);
            }
            if (Raylib.IsMouseButtonPressed(MouseButton.Left)) _pressStartedOnStage = inside;
            MouseDown = Raylib.IsMouseButtonDown(MouseButton.Left) && _pressStartedOnStage;
        }

        // Scratch turns mouse-wheel scrolling into up/down arrow "key pressed" events (Glue3D scrolls lists this way).
        if (WheelMove > 0) engine.KeyPressed("up arrow");
        else if (WheelMove < 0) engine.KeyPressed("down arrow");

        // ---- keyboard: pair each pressed key with the character it typed (layout and Shift aware) ----
        var chars = new Queue<int>();
        for (int c = Raylib.GetCharPressed(); c != 0; c = Raylib.GetCharPressed()) chars.Enqueue(c);
        for (var k = (KeyboardKey)Raylib.GetKeyPressed(); k != KeyboardKey.Null; k = (KeyboardKey)Raylib.GetKeyPressed())
        {
            string? key = null;
            if (Named.TryGetValue(k, out var name)) key = name;
            else
            {
                string? us = UsLayoutChar(k);
                if (us != null)
                {
                    key = chars.Count > 0 ? char.ConvertFromUtf32(chars.Dequeue()).ToUpperInvariant() : us;
                    if (key.Length != 1) key = us;
                }
            }
            if (key == null) continue;
            _physical[k] = key;
            KeyDown(engine, key);
        }
        // Held keys repeat "when key pressed" like the browser's key repeat.
        foreach (var (k, key) in _physical)
            if (Raylib.IsKeyPressedRepeat(k)) engine.KeyPressed(key);
        var released = new List<KeyboardKey>();
        foreach (var (k, key) in _physical)
            if (Raylib.IsKeyUp(k)) { released.Add(k); _pressed.Remove(key); }
        foreach (var k in released) _physical.Remove(k);
    }

    /// <summary>
    /// Holding the right mouse button hides and locks the cursor so you can turn freely;
    /// releasing it puts the cursor back where it was.
    /// </summary>
    void UpdateMouseLook(float dpi, Rectangle stage, bool inside)
    {
        LookDeltaX = LookDeltaY = 0;
        if (!_looking && inside && Raylib.IsMouseButtonPressed(MouseButton.Right))
        {
            _looking = true;
            _lookJustStarted = true;
            _lookStartPos = Raylib.GetMousePosition();
            Raylib.DisableCursor();
            return;
        }
        if (_looking && !Raylib.IsMouseButtonDown(MouseButton.Right))
        {
            _looking = false;
            Raylib.EnableCursor();
            Raylib.SetMousePosition((int)_lookStartPos.X, (int)_lookStartPos.Y);
        }
        RightMouseDown = _looking;
        if (!_looking) return;
        var d = Raylib.GetMouseDelta();
        if (_lookJustStarted) { _lookJustStarted = false; return; } // first frame: cursor was just re-centred
        LookDeltaX = d.X * dpi * 480 / stage.Width;
        LookDeltaY = -d.Y * dpi * 360 / stage.Height;
    }

    public void KeyDown(Engine engine, string key)
    {
        if (!_pressed.Contains(key)) _pressed.Add(key);
        engine.KeyPressed(key);
    }

    public void KeyUp(string key) => _pressed.Remove(key);

    /// <summary>Scratch's "key (...) pressed?" argument rules.</summary>
    public bool IsKeyDown(Value arg)
    {
        string key;
        if (arg.IsNumber)
        {
            double n = arg.RawNumber;
            if (n >= 48 && n <= 90) key = ((char)(int)n).ToString();
            else key = n switch { 32 => "space", 37 => "left arrow", 38 => "up arrow", 39 => "right arrow", 40 => "down arrow", _ => arg.Str };
        }
        else key = arg.Str;
        if (key == "any") return _pressed.Count > 0;
        if (!(key.Length > 1 && KeyNames.Contains(key)))
        {
            if (key.Length > 1) key = key.Substring(0, 1);
            key = key == " " ? "space" : key == "\r" ? "enter" : key == "\u001b" ? "escape" : key.ToUpperInvariant();
        }
        return _pressed.Contains(key);
    }
}
