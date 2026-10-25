// The game player: what File > build.. turns into <project>.exe.
// It contains the Scratch runtime and Glue3D's game code, but no editor (see Custom/BuildFlavor.cs).
using System;
using System.IO;
using System.Text.Json;
using Scratch;

namespace Glue3D;

public static class Program
{
    public static void Main()
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        string assetDir = Path.Combine(dataDir, "assets");
        var settings = new PlayerSettings();
        string settingsFile = Path.Combine(dataDir, "build.json");
        if (File.Exists(settingsFile))
            settings = JsonSerializer.Deserialize(File.ReadAllText(settingsFile), PlayerSettingsJson.Default.PlayerSettings) ?? settings;

        var engine = new Engine { AssetDir = assetDir, StartFullscreen = settings.Fullscreen };
        engine.Run(settings.Title, e =>
        {
            Project.Create(e, AssetManifest.Load(assetDir), assetDir, new InitialLists(assetDir));
            var stage = (Stage)e.Stage;
            ProjectFiles.Load(dataDir, typeof(Stage), stage);

            // Glue3D's own "exported game" mode: fullscreen game view, no editor, and the game
            // starts by itself (the green flag runs Glue3D's start-up, which runs the game).
            stage.allExportState = 1;
            stage.guiSplash = 0;
            stage.guiFreeze = 0;
            stage.errorPopup = 0;
            stage.allRun = 0;
            if (settings.Intro) stage.allSplashscreen = 0; // the Glue3D logo animation plays first
        });
    }
}
