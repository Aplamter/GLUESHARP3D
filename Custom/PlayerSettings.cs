// Hand-written: the build settings a game player reads from data/build.json (shared by editor and player).
using System.Text.Json.Serialization;

namespace Glue3D;

public sealed class PlayerSettings
{
    public string Title { get; set; } = "Glue3D game";
    public bool Fullscreen { get; set; }
    public bool Intro { get; set; } = true;
}

[JsonSerializable(typeof(PlayerSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class PlayerSettingsJson : JsonSerializerContext { }
