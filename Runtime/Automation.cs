using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Raylib_cs;

namespace Scratch;

/// <summary>
/// Optional scripted input for automated testing. Set SCRATCH_AUTOMATION to a file of commands:
///   wait &lt;ms&gt; | move &lt;x&gt; &lt;y&gt; | down | up | key &lt;scratch key&gt; | rdown | rup | look &lt;dx&gt; &lt;dy&gt; &lt;frames&gt; | shot &lt;png path&gt; | stats | quit
/// Coordinates are Scratch stage coordinates. Not used in normal play.
/// </summary>
public sealed class Automation
{
    readonly Engine _engine;
    readonly Queue<string> _commands;
    double _waitUntil;
    string? _pendingShot;
    double _mx, _my; bool _down;
    bool _rdown; double _ldx, _ldy; int _lookFrames;
    double _wheel; double _pdx, _pdy; int _panFrames;
    int _frames; double _statsStart;
    public bool Finished;
    /// <summary>Extra test commands registered by the app ("name args...").</summary>
    public static readonly Dictionary<string, Action<string>> Hooks = new();

    Automation(Engine engine, IEnumerable<string> lines)
    {
        _engine = engine;
        _commands = new Queue<string>(lines);
        _engine.Input.MouseOverride = (0, 0, false);
        _statsStart = engine.Now;
    }

    public static Automation? FromEnvironment(Engine engine)
    {
        string? path = Environment.GetEnvironmentVariable("SCRATCH_AUTOMATION");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        return new Automation(engine, File.ReadAllLines(path));
    }

    public void Update()
    {
        _frames++;
        while (_pendingShot == null && _commands.Count > 0 && _engine.Now >= _waitUntil)
        {
            var parts = _commands.Dequeue().Trim().Split(' ', 2);
            string arg = parts.Length > 1 ? parts[1] : "";
            switch (parts[0])
            {
                case "wait": _waitUntil = _engine.Now + double.Parse(arg, CultureInfo.InvariantCulture) / 1000; break;
                case "move":
                    var xy = arg.Split(' ');
                    _mx = double.Parse(xy[0], CultureInfo.InvariantCulture); _my = double.Parse(xy[1], CultureInfo.InvariantCulture);
                    break;
                case "down": _down = true; break;
                case "up": _down = false; break;
                case "key": _engine.Input.KeyDown(_engine, arg); break;
                case "rdown": _rdown = true; break;
                case "rup": _rdown = false; break;
                case "look": // look <dx> <dy> <frames>: right-drag spread over several frames
                    var l = arg.Split(' ');
                    _lookFrames = int.Parse(l[2]);
                    _ldx = double.Parse(l[0], CultureInfo.InvariantCulture) / _lookFrames;
                    _ldy = double.Parse(l[1], CultureInfo.InvariantCulture) / _lookFrames;
                    break;
                case "keyup": _engine.Input.KeyUp(arg); break;
                case "wheel": _wheel = double.Parse(arg, CultureInfo.InvariantCulture); break;
                case "pan": // pan <dx> <dy> <frames>: middle-button drag
                    var pp = arg.Split(' ');
                    _panFrames = int.Parse(pp[2]);
                    _pdx = double.Parse(pp[0], CultureInfo.InvariantCulture) / _panFrames;
                    _pdy = double.Parse(pp[1], CultureInfo.InvariantCulture) / _panFrames;
                    break;
                case "shot": _pendingShot = arg; break;
                case "stats":
                    double secs = _engine.Now - _statsStart;
                    Console.WriteLine($"[automation] {_frames} frames in {secs:F1}s = {_frames / secs:F1} fps; scripts {1000 * _engine.ScriptSeconds / Math.Max(1, _frames):F2} ms/frame");
                    _engine.ScriptSeconds = 0;
                    _frames = 0; _statsStart = _engine.Now;
                    break;
                case "dump": // dump <stage list or variable field name>
                    var f = _engine.Stage.GetType().GetField(arg);
                    var v = f?.GetValue(_engine.Stage);
                    Console.WriteLine($"[dump] {arg} = " + (v is ScratchList sl ? string.Join(" | ", sl.Items) : v?.ToString()));
                    break;
                case "quit": Finished = true; break;
                default:
                    if (Hooks.TryGetValue(parts[0], out var hook)) hook(arg);
                    break;
            }
        }
        _engine.Input.MouseOverride = (_mx, _my, _down);
        bool moving = _lookFrames > 0;
        _engine.Input.LookOverride = (_rdown, moving ? _ldx : 0, moving ? _ldy : 0);
        if (moving) _lookFrames--;
        _engine.Input.WheelMove = _wheel; _wheel = 0;
        bool panning = _panFrames > 0;
        _engine.Input.MiddleMouseDown = panning;
        _engine.Input.PanDeltaX = panning ? _pdx : 0; _engine.Input.PanDeltaY = panning ? _pdy : 0;
        if (panning) _panFrames--;
    }

    /// <summary>Called right before the frame is shown, so screenshots contain the finished frame.</summary>
    public void OnPresent()
    {
        if (_pendingShot == null) return;
        var img = Raylib.LoadImageFromScreen();
        Raylib.ExportImage(img, _pendingShot);
        Raylib.UnloadImage(img);
        Console.WriteLine($"[automation] saved {_pendingShot}");
        _pendingShot = null;
    }
}
