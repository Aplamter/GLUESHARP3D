// Hand-written (not overwritten by the converter): File > build.. turns the open project into a game .exe.
//
// How it works (the same way Unity and Godot build games):
//   - Glue3D comes with a prebuilt *game player* in its "player" folder (Player/Glue3DPlayer.csproj).
//     It is compiled from the same code, but with BuildFlavor.IsEditor = false, so the editor isn't in it at all.
//   - "build" copies that player as <project>.exe and puts the game next to it:
//
//       Projects/my-game/build/
//         my-game.exe          <- the game player
//         data/project.glue3d  <- your game (the project, saved just before building)
//         data/build.json      <- build settings (title, fullscreen, intro)
//         data/assets/         <- costumes, sounds, font
//
//   - The player opens the project in Glue3D's own "exported game" mode: fullscreen game view, the game
//     starts by itself, and no editor.
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Raylib_cs;
using Scratch;
using static Scratch.Ops;

namespace Glue3D;

public sealed partial class Glue3DSprite
{
    bool _buildDialogOpen;
    int _buildStep; // 0 = idle, 1 = show "building..." this frame, 2 = build now
    bool _buildFullscreen;
    bool _buildIntro = true;

    static string PlayerFileName => OperatingSystem.IsWindows() ? "Glue3DPlayer.exe" : "Glue3DPlayer";
    static string PlayerFolder => Path.Combine(AppContext.BaseDirectory, "player");
    string BuildFolder => Path.Combine(ProjectFolder!, "build");
    string BuildSettingsFile => Path.Combine(ProjectFolder!, "build-settings.json");

    /// <summary>File > build..</summary>
    void OpenBuildDialog()
    {
        if (ProjectFolder == null || ProjectName == null) { Notify("no project is open", "_icon.warn", "Error_1", 1); return; }
        LoadBuildSettings();
        _buildDialogOpen = true;
        _buildStep = 0;
        stage.guiFreeze = 1; // freeze the editor underneath, like Glue3D's own dialogs
    }

    void CloseBuildDialog()
    {
        _buildDialogOpen = false;
        _buildStep = 0;
        stage.guiFreeze = 0;
    }

    /// <summary>Called every editor frame from Glue3DAll (after the editor is drawn, before the pointer).</summary>
    void BuildDialogFrame()
    {
        if (!_buildDialogOpen) return;
        if (_buildStep == 2) { RunBuild(); return; }

        // Glue3D's dialog trick: unfreeze while drawing our own buttons so they react, freeze again after.
        stage.guiFreeze = 1 - stage.guiFreeze.Num;
        bool live = _buildStep == 0;
        double i = live ? 1 : 0;

        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_gui.dark", 0, 0, 90, 100, 0, 0, 0, 0, "", "", 0, "");
        RenderBoxToBoxTypeBorderAlternate(-100, 90, 100, -135, 1, 2, Num(Gt(stage.guiFocusEffect, 0)) * 1, "", "");
        Label("build", -95, 83, 0.55, ghost: 75);
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_button.delete", 90, 80, 90, 38, 30, 0, 0, i, "", "", 1, "");
        if (Eq(stage.guiClick, 1)) { stage.guiFreeze = 1 - stage.guiFreeze.Num; CloseBuildDialog(); return; }

        Label("build your game", 0, 62, 0.67, center: true);
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_icon.game", -65, 35, 90, 70, 10, 0, 0, i, "", "", 1, "game");
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_gui.info", 14, 35, 90, 35, 50, -100, 0, "", "", "", 0, "");
        string exe = ProjectName + (OperatingSystem.IsWindows() ? ".exe" : "");
        TextOnScaleCenterGbcCursorLimitScroll(exe, -28, 34.5, 0.7 + Num(Gt(exe.Length, 8)) * ((exe.Length - 9) * -0.02), "", "", 25, 30, double.NegativeInfinity, "", "", "", "");

        Label("build settings", 0, 8, 0.62, center: true);
        BuildToggle("start in fullscreen", "_icon.ui", -12, ref _buildFullscreen, "f11 switches it in the game", i);
        BuildToggle("play the glue3d intro", "_icon.star", -42, ref _buildIntro, "the logo animation at start", i);

        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_button.info", -90, -115, 90, 28, 40, 0, 0, i, "", "", 1, "");
        InfoText("only the game is built, without the editor", -79, -111, 0.42);
        InfoText("into your project's build folder", -79, -122, 0.42);

        if (live)
        {
            Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_button.proceed", 75, -82, 90, 52, 5, -10, 0, 1, "", "", 1, "build");
            if (Eq(stage.guiClick, 1))
            {
                SaveBuildSettings();
                _buildStep = 1; // draw "building..." first, then build on the next frame
            }
        }
        else
        {
            InfoText("building...", 40, -82, 0.6);
            _buildStep = 2;
        }
        stage.guiFreeze = 1 - stage.guiFreeze.Num;
    }

    void BuildToggle(string label, string icon, double y, ref bool on, string hint, double interactive)
    {
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage(icon, -70, y, 90, 45, 10, 0, 0, "", "", "", 1, "");
        TextOnScaleCenterGbcCursorLimitScroll(label, -52, y + 4, 0.5, "", "", 25, 30, double.NegativeInfinity, "", "", "", "");
        InfoText(hint, -52, y - 7, 0.38);
        // Glue3D's toggle style (as in its settings): the check is greyed out when off.
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_button.accept", 70, y, 90, 35, 40, 0, on ? 0 : double.NegativeInfinity, interactive, "", 1, 1, on ? "on" : "off");
        if (Eq(stage.guiClick, 1)) on = !on;
    }

    // ---------------- the actual build ----------------

    void RunBuild()
    {
        string? error = null;
        string outFolder = BuildFolder;
        try
        {
            string player = Path.Combine(PlayerFolder, PlayerFileName);
            if (!File.Exists(player))
                throw new BuildException("the game player is missing (player folder next to glue3d)");

            // 1. Save, so the game is exactly what's in the editor now.
            ProjectFiles.Save(ProjectFolder!, ProjectName!, stage.ramEngineData.Item(72).Str, typeof(Stage), stage);

            // 2. Fresh build folder.
            if (Directory.Exists(outFolder)) Directory.Delete(outFolder, recursive: true);
            string data = Path.Combine(outFolder, "data");
            Directory.CreateDirectory(data);

            // 3. The player becomes <project>.exe (plus anything that ships beside it).
            foreach (var file in Directory.GetFiles(PlayerFolder))
            {
                string name = Path.GetFileName(file) == PlayerFileName
                    ? ProjectName + (OperatingSystem.IsWindows() ? ".exe" : "")
                    : Path.GetFileName(file);
                File.Copy(file, Path.Combine(outFolder, name));
                if (!OperatingSystem.IsWindows() && name == ProjectName)
                    File.SetUnixFileMode(Path.Combine(outFolder, name), File.GetUnixFileMode(file));
            }

            // 4. The game: project data, build settings, and the art/sounds/font it draws with.
            File.Copy(Path.Combine(ProjectFolder!, ProjectFiles.FileName), Path.Combine(data, ProjectFiles.FileName));
            File.WriteAllText(Path.Combine(data, "build.json"), JsonSerializer.Serialize(new PlayerSettings
            {
                Title = ProjectName!,
                Fullscreen = _buildFullscreen,
                Intro = _buildIntro,
            }, PlayerSettingsJson.Default.PlayerSettings));
            CopyFolder(Engine.AssetDir, Path.Combine(data, "assets"));
        }
        catch (BuildException e) { error = e.Message; }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            error = "the build failed: " + e.GetType().Name.Replace("Exception", "").ToLowerInvariant();
        }

        CloseBuildDialog();
        if (error != null)
        {
            Notify(error, "_icon.warn", "Error_3", "");
            return;
        }
        Notify("build complete: " + ProjectName + "/build", "_icon.game", "Windows XP Print complete", 1);
        OpenInFileBrowser(outFolder);
    }

    sealed class BuildException : Exception { public BuildException(string m) : base(m) { } }

    static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(from)) CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    /// <summary>Shows the finished build in Explorer (Windows) or the file manager.</summary>
    static void OpenInFileBrowser(string folder)
    {
        if (Environment.GetEnvironmentVariable("SCRATCH_AUTOMATION") != null) return; // not during automated tests
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS()) Process.Start("open", folder);
            else Process.Start("xdg-open", folder);
        }
        catch { /* nothing to open it with; the message already says where it is */ }
    }

    // ---------------- build settings are remembered per project ----------------

    void LoadBuildSettings()
    {
        try
        {
            if (!File.Exists(BuildSettingsFile)) return;
            var s = JsonSerializer.Deserialize(File.ReadAllText(BuildSettingsFile), PlayerSettingsJson.Default.PlayerSettings);
            if (s == null) return;
            _buildFullscreen = s.Fullscreen;
            _buildIntro = s.Intro;
        }
        catch { /* use defaults */ }
    }

    void SaveBuildSettings()
    {
        try
        {
            File.WriteAllText(BuildSettingsFile, JsonSerializer.Serialize(new PlayerSettings
            {
                Title = ProjectName!, Fullscreen = _buildFullscreen, Intro = _buildIntro,
            }, PlayerSettingsJson.Default.PlayerSettings));
        }
        catch { /* not important */ }
    }
}
