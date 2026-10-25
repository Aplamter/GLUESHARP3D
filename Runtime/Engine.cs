using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Raylib_cs;

namespace Scratch;

/// <summary>One running script: a stack of iterators (the event script, plus any custom blocks that can pause).</summary>
public sealed class ScriptThread
{
    public readonly Target Target;
    public readonly Hat Hat;
    public readonly Stack<IEnumerator> Stack = new();
    public bool Done;

    public ScriptThread(Target target, Hat hat)
    {
        Target = target; Hat = hat;
        Stack.Push(hat.Start(target));
    }
}

/// <summary>
/// The Scratch runtime: owns the window, runs scripts frame by frame like Scratch's sequencer,
/// and draws the stage.
/// </summary>
public sealed class Engine
{
    public const double FrameRate = 60; // from the project's TurboWarp settings

    public StageBase Stage = null!;
    /// <summary>Sprites and clones, bottom layer first.</summary>
    public readonly List<SpriteBase> Sprites = new();
    public readonly Renderer Renderer;
    public readonly AudioEngine Audio = new();
    public readonly InputState Input = new();
    public readonly Monitors Monitors = new();
    public Automation? Automation;

    readonly List<ScriptThread> _threads = new();
    ScriptThread? _current;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    double _timerStart;
    bool _redrawRequested;

    /// <summary>Runs every frame after input is read and before scripts run (hand-written tools hook in here).</summary>
    public event Action? BeforeScripts;
    /// <summary>Draws on top of the stage every frame, in window pixels (the stage rectangle is passed in).</summary>
    public event Action<Rectangle>? Overlay;
    internal void DrawOverlay(Rectangle stage) => Overlay?.Invoke(stage);

    /// <summary>Folder with costumes, sounds and fonts (a built game keeps them in data/assets).</summary>
    public string AssetDir = Path.Combine(AppContext.BaseDirectory, "assets");
    /// <summary>Open in borderless fullscreen (F11 toggles).</summary>
    public bool StartFullscreen;

    public Engine() { Renderer = new Renderer(this); }

    public double Now => _clock.Elapsed.TotalSeconds;
    public double Timer => Now - _timerStart;
    public void ResetTimer() => _timerStart = Now;
    public void RequestRedraw() => _redrawRequested = true;

    // ---------------- setup ----------------

    public void SetStage(StageBase stage) { stage.Engine = this; Stage = stage; }

    public void AddSprite(SpriteBase s) { s.Engine = this; Sprites.Add(s); }

    public void AddClone(SpriteBase original, SpriteBase clone)
    {
        // Clones go directly behind the sprite they were cloned from.
        int i = Sprites.IndexOf(original);
        Sprites.Insert(Math.Max(0, i), clone);
        foreach (var hat in clone.Hats)
            if (hat.Kind == HatKind.CloneStart) _threads.Add(new ScriptThread(clone, hat));
    }

    public void RemoveClone(SpriteBase clone)
    {
        clone.Disposed = true;
        Sprites.Remove(clone);
        foreach (var t in _threads) if (t.Target == clone) t.Done = true;
        Audio.Release(clone);
    }

    public void CreateCloneOf(string spriteName)
    {
        foreach (var s in Sprites)
            if (!s.IsClone && s.Name == spriteName) { s.CreateClone(); return; }
    }

    // ---------------- events ----------------

    /// <summary>Execution order: top sprite first, the stage last (like Scratch).</summary>
    IEnumerable<Target> ExecutionOrder()
    {
        for (int i = Sprites.Count - 1; i >= 0; i--) yield return Sprites[i];
        yield return Stage;
    }

    void StartHats(HatKind kind, string key, bool restartExisting)
    {
        foreach (var target in ExecutionOrder())
        {
            if (target.Disposed) continue;
            foreach (var hat in target.Hats)
            {
                if (hat.Kind != kind) continue;
                if (kind != HatKind.GreenFlag && !string.Equals(hat.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
                int existing = _threads.FindIndex(t => t.Target == target && t.Hat == hat);
                if (existing >= 0)
                {
                    if (restartExisting) { _threads[existing].Done = true; _threads[existing] = new ScriptThread(target, hat); }
                    else if (!_threads[existing].Done) continue;
                    else _threads.Add(new ScriptThread(target, hat));
                }
                else _threads.Add(new ScriptThread(target, hat));
            }
        }
    }

    public void Broadcast(string name) => StartHats(HatKind.Broadcast, name, true);

    public void KeyPressed(string scratchKey)
    {
        StartHats(HatKind.KeyPressed, scratchKey, false);
        StartHats(HatKind.KeyPressed, "any", false);
    }

    public void GreenFlag()
    {
        StopAll();
        ResetTimer();
        foreach (var t in ExecutionOrder()) { t.SoundPitch = 0; t.SoundPan = 0; Audio.Sync(t); }
        StartHats(HatKind.GreenFlag, "", true);
    }

    public void StopAll()
    {
        foreach (var t in _threads) t.Done = true;
        _threads.Clear();
        foreach (var s in Sprites.ToArray())
            if (s.IsClone) RemoveClone(s);
        foreach (var t in ExecutionOrder()) t.ClearEffects();
        Audio.StopAll();
    }

    public void StopOtherScripts(Target target)
    {
        foreach (var t in _threads)
            if (t.Target == target && t != _current) t.Done = true;
    }

    // ---------------- running scripts ----------------

    /// <summary>
    /// One frame of Scratch's sequencer: keep stepping scripts until something asks for a redraw
    /// (pen, stamp, visible sprite change) or 75% of the frame time is used.
    /// </summary>
    /// <summary>Total time spent running scripts (for the automation "stats" command).</summary>
    public double ScriptSeconds;

    public void StepThreads()
    {
        double t0 = Now;
        try { StepThreadsInner(); } finally { ScriptSeconds += Now - t0; }
    }

    void StepThreadsInner()
    {
        _redrawRequested = false;
        double start = Now, budget = 0.75 / FrameRate;
        while (true)
        {
            int active = 0;
            for (int i = 0; i < _threads.Count; i++)
            {
                var th = _threads[i];
                if (th.Done) continue;
                Step(th);
                if (!th.Done) active++;
            }
            _threads.RemoveAll(t => t.Done);
            if (active == 0 || _redrawRequested || Now - start >= budget) break;
        }
    }

    void Step(ScriptThread th)
    {
        _current = th;
        try
        {
            while (th.Stack.Count > 0 && !th.Done)
            {
                var it = th.Stack.Peek();
                if (!it.MoveNext()) { th.Stack.Pop(); continue; }
                if (it.Current is IEnumerator inner) { th.Stack.Push(inner); continue; }
                return; // paused until the next pass / frame
            }
            th.Done = true;
        }
        catch (StopScript)
        {
            th.Done = true;
        }
        finally
        {
            _current = null;
        }
    }

    // ---------------- main loop ----------------

    public void Run(string title, Action<Engine> createProject)
    {
        // No HighDpiWindow flag: GLFW already makes the process DPI-aware on Windows, so sizes are real pixels.
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.InitWindow(960, 720, title);
        Raylib.SetExitKey(KeyboardKey.Null);
        FitWindowToMonitor();
        if (StartFullscreen) Raylib.ToggleBorderlessWindowed();
        Raylib.SetTargetFPS((int)FrameRate);

        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.White);
        Raylib.DrawText("Loading...", 20, 20, 30, Color.DarkGray);
        Raylib.EndDrawing();

        Audio.Init();
        Renderer.Init(Raylib.GetRenderWidth(), Raylib.GetRenderHeight());
        Monitors.Init(AssetDir);
        createProject(this);
        Audio.Preload(ExecutionOrder());
        Automation = Automation.FromEnvironment(this);

        GreenFlag();
        while (!Raylib.WindowShouldClose() && !(Automation?.Finished ?? false))
        {
            var stageRect = StageRect();
            Input.Update(this, stageRect);
            Automation?.Update();
            BeforeScripts?.Invoke();
            if (Raylib.IsKeyPressed(KeyboardKey.F11) || (Raylib.IsKeyPressed(KeyboardKey.Enter) && Raylib.IsKeyDown(KeyboardKey.LeftAlt)))
                Raylib.ToggleBorderlessWindowed();
            StepThreads();
            Renderer.Present(stageRect);
        }
        Audio.Close();
        Raylib.CloseWindow();
    }

    static void FitWindowToMonitor()
    {
        int m = Raylib.GetCurrentMonitor();
        int mw = Raylib.GetMonitorWidth(m), mh = Raylib.GetMonitorHeight(m);
        if (mw <= 0 || mh <= 0) return;
        // Largest whole multiple of 480x360 that fits comfortably on the screen.
        int scale = Math.Max(1, Math.Min((int)(mw * 0.9 / 480), (int)(mh * 0.85 / 360)));
        Raylib.SetWindowSize(480 * scale, 360 * scale);
        Raylib.SetWindowPosition((mw - 480 * scale) / 2, (mh - 360 * scale) / 2);
    }

    /// <summary>The 4:3 stage area inside the window (letterboxed), in render pixels.</summary>
    public static Rectangle StageRect()
    {
        float w = Raylib.GetRenderWidth(), h = Raylib.GetRenderHeight();
        float scale = Math.Min(w / 480f, h / 360f);
        float sw = MathF.Floor(480 * scale), sh = MathF.Floor(360 * scale);
        return new Rectangle(MathF.Floor((w - sw) / 2), MathF.Floor((h - sh) / 2), sw, sh);
    }
}

/// <summary>Starting contents of lists, read from assets/initial-lists.json.</summary>
public sealed class InitialLists
{
    readonly Dictionary<string, System.Text.Json.JsonElement> _data;

    public InitialLists(string assetDir)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(assetDir, "initial-lists.json")));
        _data = new();
        foreach (var p in doc.RootElement.EnumerateObject()) _data[p.Name] = p.Value.Clone();
    }

    public void Fill(ScratchList list, string key)
    {
        if (!_data.TryGetValue(key, out var arr)) return;
        var items = new List<Value>(arr.GetArrayLength());
        foreach (var v in arr.EnumerateArray())
        {
            items.Add(v.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number => (Value)v.GetDouble(),
                System.Text.Json.JsonValueKind.True => (Value)true,
                System.Text.Json.JsonValueKind.False => (Value)false,
                _ => Value.FromLiteral(v.GetString() ?? ""),
            });
        }
        list.Items = items;
    }
}
