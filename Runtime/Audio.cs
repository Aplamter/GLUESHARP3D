using System;
using System.Collections.Generic;
using Raylib_cs;

namespace Scratch;

/// <summary>
/// Sound playback. Each sprite (and clone) gets its own voice per sound, so starting a sound
/// restarts it for that sprite only, and volume/pitch/pan apply per sprite, as in Scratch.
/// </summary>
public sealed class AudioEngine
{
    bool _ready;
    readonly HashSet<Target> _withVoices = new();

    public void Init()
    {
        Raylib.InitAudioDevice();
        _ready = Raylib.IsAudioDeviceReady();
    }

    public void Preload(IEnumerable<Target> targets)
    {
        if (!_ready) return;
        foreach (var t in targets)
            foreach (var s in t.Sounds) Load(s);
    }

    void Load(SoundAsset s)
    {
        if (s.Loaded) return;
        s.Sound = Raylib.LoadSound(s.File);
        s.Loaded = true;
        if (s.Sound.FrameCount == 0) Console.Error.WriteLine($"[audio] could not load {s.File}");
    }

    public void Play(Target t, int index)
    {
        if (!_ready) return;
        var asset = t.Sounds[index];
        Load(asset);
        if (asset.Sound.FrameCount == 0) return;
        t.SoundVoices ??= new Dictionary<int, Sound>();
        if (!t.SoundVoices.TryGetValue(index, out var voice))
        {
            voice = Raylib.LoadSoundAlias(asset.Sound);
            t.SoundVoices[index] = voice;
        }
        Apply(t, voice);
        Raylib.PlaySound(voice);
        _withVoices.Add(t);
    }

    static void Apply(Target t, Sound voice)
    {
        Raylib.SetSoundVolume(voice, (float)(t.Volume / 100));
        Raylib.SetSoundPitch(voice, (float)Math.Pow(2, t.SoundPitch / 120)); // 10 pitch units = 1 semitone
        Raylib.SetSoundPan(voice, (float)(t.SoundPan / 100));
    }

    /// <summary>Applies changed volume/effects to sounds already playing.</summary>
    public void Sync(Target t)
    {
        if (!_ready || t.SoundVoices == null) return;
        foreach (var v in t.SoundVoices.Values) Apply(t, v);
    }

    public void StopAll()
    {
        if (!_ready) return;
        foreach (var t in _withVoices)
            if (t.SoundVoices != null)
                foreach (var v in t.SoundVoices.Values) Raylib.StopSound(v);
    }

    /// <summary>A deleted clone's sounds stop.</summary>
    public void Release(Target t)
    {
        if (!_ready || t.SoundVoices == null) return;
        foreach (var v in t.SoundVoices.Values) { Raylib.StopSound(v); Raylib.UnloadSoundAlias(v); }
        t.SoundVoices = null;
        _withVoices.Remove(t);
    }

    public void Close()
    {
        if (_ready) Raylib.CloseAudioDevice();
    }
}
