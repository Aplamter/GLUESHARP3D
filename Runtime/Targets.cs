using System;
using System.Collections;
using System.Collections.Generic;
using Raylib_cs;

namespace Scratch;

public enum Effect { Color, Fisheye, Whirl, Pixelate, Mosaic, Brightness, Ghost }
public enum PenParam { Color, Saturation, Brightness, Transparency }
public enum SoundEffect { Pitch, Pan }
public enum RotationStyle { AllAround, LeftRight, DontRotate }

/// <summary>Thrown to end the current script immediately (e.g. "delete this clone").</summary>
public sealed class StopScript : Exception { public static readonly StopScript Instance = new(); }

public enum HatKind { GreenFlag, Broadcast, KeyPressed, CloneStart }

/// <summary>An event script ("when ... "): which event starts it and the code to run.</summary>
public sealed record Hat(HatKind Kind, string Key, Func<Target, IEnumerator> Start);

/// <summary>Shared by the stage and sprites: costumes, effects, sounds, and the blocks every target has.</summary>
public abstract class Target
{
    public Engine Engine = null!;
    public Costume[] Costumes = Array.Empty<Costume>();
    public SoundAsset[] Sounds = Array.Empty<SoundAsset>();
    public int CurrentCostume;
    public double[] Effects = new double[7];
    public double Volume = 100;
    public double SoundPitch, SoundPan;
    internal Dictionary<int, Sound>? SoundVoices;
    public bool Disposed;

    public abstract bool IsStage { get; }
    public abstract Hat[] Hats { get; }

    // ---------- looks ----------
    public Costume Costume => Costumes[CurrentCostume];
    public double CostumeNumber => CurrentCostume + 1;
    public string CostumeName => Costume.Name;

    public void SetCostumeIndex(double index)
    {
        index = Ops.Round(index);
        if (double.IsInfinity(index) || double.IsNaN(index)) index = 0;
        CurrentCostume = (int)Ops.WrapClamp(index, 0, Costumes.Length - 1);
        OnVisualChange();
    }

    /// <summary>"switch costume to": by name first, then "next/previous costume", then by number.</summary>
    public void SwitchCostume(Value c)
    {
        if (c.IsNumber) { SetCostumeIndex(c.RawNumber - 1); return; }
        string s = c.Str;
        for (int i = 0; i < Costumes.Length; i++)
            if (Costumes[i].Name == s) { SetCostumeIndex(i); return; }
        if (s == "next costume" || s == "next backdrop") SetCostumeIndex(CurrentCostume + 1);
        else if (s == "previous costume" || s == "previous backdrop") SetCostumeIndex(CurrentCostume - 1);
        else
        {
            double n = c.JsNumber;
            if (!double.IsNaN(n) && !c.IsWhiteSpace) SetCostumeIndex(n - 1);
        }
    }

    public void SetEffect(Effect e, double v)
    {
        Effects[(int)e] = e switch
        {
            Effect.Ghost => Ops.Clamp(v, 0, 100),
            Effect.Brightness => Ops.Clamp(v, -100, 100),
            _ => v,
        };
        OnVisualChange();
    }
    public void ChangeEffect(Effect e, double v) => SetEffect(e, Effects[(int)e] + v);
    public void ClearEffects() { Array.Clear(Effects); OnVisualChange(); }

    protected virtual void OnVisualChange() { }

    // ---------- sound ----------
    int SoundIndex(Value sound)
    {
        int len = Sounds.Length;
        if (len == 0) return -1;
        string name = sound.Str;
        for (int i = 0; i < len; i++) if (Sounds[i].Name == name) return i;
        // parseInt semantics
        string t = name.Trim();
        int j = 0; bool neg = false;
        if (j < t.Length && (t[j] == '-' || t[j] == '+')) { neg = t[j] == '-'; j++; }
        long v = 0; int digits = 0;
        while (j < t.Length && char.IsAsciiDigit(t[j]) && digits < 15) { v = v * 10 + (t[j] - '0'); j++; digits++; }
        if (digits == 0) return -1;
        return (int)Ops.WrapClamp((neg ? -v : v) - 1, 0, len - 1);
    }

    public void StartSound(Value sound)
    {
        int i = SoundIndex(sound);
        if (i >= 0) Engine.Audio.Play(this, i);
    }
    public void SetVolume(double v) { Volume = Ops.Clamp(v, 0, 100); Engine.Audio.Sync(this); }
    public void ChangeVolume(double v) => SetVolume(Volume + v);
    public void SetSoundEffect(SoundEffect e, double v)
    {
        // The project disables TurboWarp's "misc limits", which widens the pitch range.
        if (e == SoundEffect.Pitch) SoundPitch = Ops.Clamp(v, -1000, 1000);
        else SoundPan = Ops.Clamp(v, -100, 100);
        Engine.Audio.Sync(this);
    }
    public void ChangeSoundEffect(SoundEffect e, double v) =>
        SetSoundEffect(e, (e == SoundEffect.Pitch ? SoundPitch : SoundPan) + v);
    public void ClearSoundEffects() { SoundPitch = 0; SoundPan = 0; Engine.Audio.Sync(this); }
    public void StopAllSounds() => Engine.Audio.StopAll();

    // ---------- sensing / events / control shared by all targets ----------
    public double MouseX => Engine.Input.MouseX;
    public double MouseY => Engine.Input.MouseY;
    public bool MouseDown => Engine.Input.MouseDown;
    public bool RightMouseDown => Engine.Input.RightMouseDown;
    public double LookDeltaX => Engine.Input.LookDeltaX;
    public double LookDeltaY => Engine.Input.LookDeltaY;
    public bool KeyPressed(Value key) => Engine.Input.IsKeyDown(key);
    public double Timer => Engine.Timer;
    public void ResetTimer() => Engine.ResetTimer();
    public void Broadcast(string name) => Engine.Broadcast(name);
    public void StopOtherScriptsInSprite() => Engine.StopOtherScripts(this);
    public void StopAll() => Engine.StopAll();
    public void PenClear() => Engine.Renderer.ClearPen();
    public void ShowList(ScratchList list) => Engine.Monitors.SetVisible(list, true);
    public void HideList(ScratchList list) => Engine.Monitors.SetVisible(list, false);

    /// <summary>"wait" inside a block that runs without screen refresh: Scratch blocks until the time passes.</summary>
    public void BusyWait(double seconds)
    {
        double end = Engine.Now + Math.Max(0, seconds);
        while (Engine.Now < end) { }
    }

    /// <summary>"wait (seconds)".</summary>
    public IEnumerator Wait(double seconds)
    {
        double end = Engine.Now + Math.Max(0, seconds);
        Engine.RequestRedraw();
        yield return null;
        while (Engine.Now < end) yield return null;
    }
}

public abstract class StageBase : Target
{
    public override bool IsStage => true;
}

public abstract class SpriteBase : Target
{
    public string Name = "";
    public double X, Y;
    public double Direction = 90;
    public double Size = 100;
    public bool Visible = true;
    public RotationStyle RotationStyle = RotationStyle.AllAround;
    public bool IsClone;
    public StageBase BaseStage = null!;

    // Pen state (Scratch defaults).
    public bool PenIsDown;
    public double PenColor = 66.66, PenSaturation = 100, PenBrightness = 100, PenTransparency = 0;
    public double PenSize = 1;
    public float PenR, PenG = 0, PenB = 1, PenA = 1;

    public override bool IsStage => false;

    protected SpriteBase() { UpdatePenColor(); }

    protected override void OnVisualChange()
    {
        if (Visible) Engine.RequestRedraw();
    }

    // ---------- reporters ----------
    static double LimitPrecision(double c)
    {
        double r = Ops.Round(c);
        return Math.Abs(c - r) < 1e-9 ? r : c;
    }
    public double XPosition => LimitPrecision(X);
    public double YPosition => LimitPrecision(Y);
    public double SizeReporter => Ops.Round(Size);

    // ---------- motion ----------
    public void GoTo(double x, double y)
    {
        double oldX = X, oldY = Y;
        X = x; Y = y;  // the project turns TurboWarp's fencing off, so sprites aren't kept on stage
        if (Visible) Engine.RequestRedraw();
        if (PenIsDown) Engine.Renderer.PenLine(this, oldX, oldY, X, Y);
    }
    public void SetX(double x) => GoTo(x, Y);
    public void SetY(double y) => GoTo(X, y);
    public void ChangeX(double dx) => GoTo(X + dx, Y);
    public void ChangeY(double dy) => GoTo(X, Y + dy);
    public void PointInDirection(double d)
    {
        if (!double.IsFinite(d)) return;
        Direction = Ops.WrapClamp(d, -179, 180);
        OnVisualChange();
    }
    public void TurnRight(double deg) => PointInDirection(Direction + deg);
    public void TurnLeft(double deg) => PointInDirection(Direction - deg);

    // ---------- looks ----------
    public void SetSize(double s) { Size = 100 * Math.Max(0, s / 100); OnVisualChange(); }
    public void ChangeSize(double s) => SetSize(Size + s);
    public void Show() { Visible = true; Engine.RequestRedraw(); }
    public void Hide() { if (Visible) Engine.RequestRedraw(); Visible = false; }

    // ---------- pen ----------
    public void PenDown()
    {
        PenIsDown = true;
        Engine.Renderer.PenLine(this, X, Y, X, Y);
    }
    public void PenUp() => PenIsDown = false;
    public void Stamp() => Engine.Renderer.Stamp(this);

    public void SetPenSize(double s) => PenSize = Math.Max(0, s); // high-quality pen: no 1..1200 limit
    public void ChangePenSize(double s) => SetPenSize(PenSize + s);

    public void SetPenColor(Value color)
    {
        var (r, g, b, a) = ColorUtil.ToRgb(color);
        var (h, s, v) = ColorUtil.RgbToHsv(r, g, b);
        PenColor = h / 360 * 100;
        PenSaturation = s * 100;
        PenBrightness = v * 100;
        PenTransparency = a.HasValue ? 100 * (1 - a.Value / 255.0) : 0;
        UpdatePenColor();
    }

    public void SetPenParam(PenParam p, double v) => SetOrChangePenParam(p, v, false);
    public void ChangePenParam(PenParam p, double v) => SetOrChangePenParam(p, v, true);

    void SetOrChangePenParam(PenParam p, double v, bool change)
    {
        switch (p)
        {
            case PenParam.Color: PenColor = Ops.WrapClamp(v + (change ? PenColor : 0), 0, 100); break;
            case PenParam.Saturation: PenSaturation = Ops.Clamp(v + (change ? PenSaturation : 0), 0, 100); break;
            case PenParam.Brightness: PenBrightness = Ops.Clamp(v + (change ? PenBrightness : 0), 0, 100); break;
            case PenParam.Transparency: PenTransparency = Ops.Clamp(v + (change ? PenTransparency : 0), 0, 100); break;
        }
        UpdatePenColor();
    }

    void UpdatePenColor()
    {
        var (r, g, b) = ColorUtil.HsvToRgb(PenColor * 360 / 100, PenSaturation / 100, PenBrightness / 100);
        PenR = r / 255f; PenG = g / 255f; PenB = b / 255f;
        PenA = (float)(1 - PenTransparency / 100);
    }

    // ---------- sensing ----------
    public bool TouchingMouse() => Engine.Renderer.TouchingPoint(this, Engine.Input.MouseX, Engine.Input.MouseY);

    // ---------- clones ----------
    /// <summary>Copies sprite-only lists into a new clone (generated per sprite).</summary>
    protected virtual void CopyLocalLists() { }

    public void CreateClone()
    {
        var original = this;
        var clone = (SpriteBase)MemberwiseClone();
        clone.IsClone = true;
        clone.Effects = (double[])Effects.Clone();
        clone.SoundVoices = null;
        clone.Disposed = false;
        clone.CopyLocalLists();
        Engine.AddClone(original, clone);
    }

    public void DeleteThisClone()
    {
        if (!IsClone) return;
        Engine.RemoveClone(this);
        throw StopScript.Instance;
    }
}

/// <summary>Scratch's color conversions (scratch-vm util/color.js and cast.js).</summary>
public static class ColorUtil
{
    public static (int r, int g, int b, int? a) ToRgb(Value v)
    {
        if (v.IsString && v.Str.StartsWith('#'))
        {
            var hex = HexToRgb(v.Str);
            return hex ?? (0, 0, 0, 255);
        }
        double d = v.Num;
        if (!double.IsFinite(d)) d = 0;
        int n = unchecked((int)(uint)(long)Math.Truncate(d % 4294967296.0)); // JS ToInt32
        int a = (n >> 24) & 0xFF;
        return ((n >> 16) & 0xFF, (n >> 8) & 0xFF, n & 0xFF, a > 0 ? a : 255);
    }

    static (int, int, int, int?)? HexToRgb(string hex)
    {
        string h = hex.Substring(1);
        if (h.Length == 3) h = $"{h[0]}{h[0]}{h[1]}{h[1]}{h[2]}{h[2]}";
        if (h.Length != 6) return null;
        try
        {
            return (Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16), null);
        }
        catch { return null; }
    }

    public static (double h, double s, double v) RgbToHsv(int ri, int gi, int bi)
    {
        double r = ri / 255.0, g = gi / 255.0, b = bi / 255.0;
        double x = Math.Min(Math.Min(r, g), b);
        double v = Math.Max(Math.Max(r, g), b);
        double h = 0, s = 0;
        if (x != v)
        {
            double f = r == x ? g - b : (g == x ? b - r : r - g);
            double i = r == x ? 3 : (g == x ? 5 : 1);
            h = ((i - f / (v - x)) * 60) % 360;
            s = (v - x) / v;
        }
        return (h, s, v);
    }

    public static (int r, int g, int b) HsvToRgb(double h, double s, double v)
    {
        h %= 360; if (h < 0) h += 360;
        s = Math.Max(0, Math.Min(s, 1));
        v = Math.Max(0, Math.Min(v, 1));
        int i = (int)Math.Floor(h / 60);
        double f = h / 60 - i;
        double p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        double r, g, b;
        switch (i)
        {
            default: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            case 5: r = v; g = p; b = q; break;
        }
        return ((int)Math.Floor(r * 255), (int)Math.Floor(g * 255), (int)Math.Floor(b * 255));
    }
}
