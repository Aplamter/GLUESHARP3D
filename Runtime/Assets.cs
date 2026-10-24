using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Raylib_cs;

namespace Scratch;

/// <summary>A costume image plus its size and rotation center, in Scratch stage units.</summary>
public sealed class Costume
{
    public string Name = "";
    public string File = "";
    /// <summary>Texture pixels per stage unit (SVGs were pre-rendered at high resolution).</summary>
    public double Scale = 1;
    public double Width, Height, CenterX, CenterY;
    public Texture2D Texture;
    byte[]? _alpha;
    int _pw, _ph;

    public unsafe void Load(string assetDir)
    {
        string path = Path.Combine(assetDir, File);
        Image img = Raylib.LoadImage(path);
        Raylib.ImageFormat(ref img, PixelFormat.UncompressedR8G8B8A8);
        _pw = img.Width; _ph = img.Height;
        // Keep the alpha channel for "touching mouse-pointer?".
        _alpha = new byte[_pw * _ph];
        byte* px = (byte*)img.Data;
        for (int i = 0; i < _alpha.Length; i++) _alpha[i] = px[i * 4 + 3];
        // Scratch's renderer keeps textures premultiplied; that also makes scaled-down edges correct.
        Raylib.ImageAlphaPremultiply(ref img);
        Texture = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.GenTextureMipmaps(ref Texture);
        Raylib.SetTextureFilter(Texture, Texture.Mipmaps > 1 ? TextureFilter.Trilinear : TextureFilter.Bilinear);
        Raylib.SetTextureWrap(Texture, TextureWrap.Clamp);
    }

    /// <summary>Is the pixel at costume-space (u, v) (stage units from the top-left) not transparent?</summary>
    public bool IsOpaqueAt(double u, double v)
    {
        if (_alpha == null) return false;
        int x = (int)Math.Floor(u * Scale), y = (int)Math.Floor(v * Scale);
        if (x < 0 || y < 0 || x >= _pw || y >= _ph) return false;
        return _alpha[y * _pw + x] > 0;
    }
}

public sealed class SoundAsset
{
    public string Name = "";
    public string File = "";
    public Sound Sound;
    public bool Loaded;
}

/// <summary>Reads assets/manifest.json written by the asset converter.</summary>
public sealed class AssetManifest
{
    public sealed class CostumeEntry
    {
        public string name { get; set; } = "";
        public string file { get; set; } = "";
        public double scale { get; set; }
        public double width { get; set; }
        public double height { get; set; }
        public double centerX { get; set; }
        public double centerY { get; set; }
    }
    public sealed class SoundEntry
    {
        public string name { get; set; } = "";
        public string file { get; set; } = "";
    }
    public sealed class TargetEntry
    {
        public string name { get; set; } = "";
        public List<CostumeEntry> costumes { get; set; } = new();
        public List<SoundEntry> sounds { get; set; } = new();
    }
    public List<TargetEntry> targets { get; set; } = new();

    public static AssetManifest Load(string assetDir) =>
        JsonSerializer.Deserialize(System.IO.File.ReadAllText(Path.Combine(assetDir, "manifest.json")), ManifestJson.Default.AssetManifest)!;

    public (Costume[] costumes, SoundAsset[] sounds) For(int targetIndex, string assetDir)
    {
        var t = targets[targetIndex];
        var costumes = new Costume[t.costumes.Count];
        for (int i = 0; i < costumes.Length; i++)
        {
            var c = t.costumes[i];
            costumes[i] = new Costume
            {
                Name = c.name, File = c.file, Scale = c.scale,
                Width = c.width, Height = c.height, CenterX = c.centerX, CenterY = c.centerY,
            };
            costumes[i].Load(assetDir);
        }
        var sounds = new SoundAsset[t.sounds.Count];
        for (int i = 0; i < sounds.Length; i++)
            sounds[i] = new SoundAsset { Name = t.sounds[i].name, File = Path.Combine(assetDir, t.sounds[i].file) };
        return (costumes, sounds);
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(AssetManifest))]
internal partial class ManifestJson : System.Text.Json.Serialization.JsonSerializerContext { }
