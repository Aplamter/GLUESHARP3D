// Hand-written (not overwritten by the converter): Glue3D's start menu.
//
// After the welcome popup, the app shows this menu instead of going straight into the editor:
//   - "create a new project": name it, pick the engine (tile / segment / mode7), and it is created in
//     the Projects folder next to Glue3D.exe. The engine no longer has to be configured in settings.
//   - "load a project": browse to a project folder and open it.
// Everything is drawn with Glue3D's own GUI blocks (boxes, buttons, font, pointer, tooltips, sounds),
// so it follows the selected theme like the rest of the editor.
using System;
using System.Collections.Generic;
using System.IO;
using Raylib_cs;
using Scratch;
using static Scratch.Ops;

namespace Glue3D;

public sealed partial class Glue3DSprite
{
    /// <summary>True while the start menu is showing (the editor is hidden).</summary>
    public bool MainMenuActive = true;

    /// <summary>Folder and name of the open project (null until one is created or loaded).</summary>
    public string? ProjectFolder, ProjectName;

    enum MenuDialog { None, Create, Load }
    MenuDialog _menuDialog = MenuDialog.None;

    // "create a new project": which engine parts are switched on.
    bool _engineTile = true, _engineSegment = true, _engineMode7 = true;

    // "load a project": the folder being browsed ("" = list of drives) and what's in it.
    string _browseFolder = "";
    readonly List<BrowseEntry> _browseEntries = new();
    int _browseScroll;
    const int BrowseRows = 7;
    sealed record BrowseEntry(string Path, string Label, string Detail, bool IsProject, bool IsUp);

    // Address in Glue3D's engine data that the name field types into (the same one Glue3D's
    // "new object" dialog uses; typed text goes there automatically while inputAddress points at it).
    const int NameAddress = 12;
    const int NameLimit = 24;

    bool _clickTaken; // only one menu control reacts to a click

    // ================= one frame of the menu (called from Glue3DAll instead of the editor) =================

    void MainMenuFrame()
    {
        // The same per-frame bookkeeping Glue3D's editor does in "Main: control".
        stage.inputAddress = (_menuDialog == MenuDialog.Create && Eq(stage.guiFreeze, 0) && Eq(stage.guiSplash, 0)) ? NameAddress : 0;
        stage.inputLimit = NameLimit;
        stage.guiPointerID = "";
        stage.guiInfo = "";
        if (!MouseDown) { stage.dragTime = 0; stage.guiSlider = 0; }
        stage.guiPointerX = stage.guiPointerX.Num + Nz((MouseX - stage.guiPointerX.Num) / (1 + 5 * stage.timeDelta.Num));
        stage.guiPointerY = stage.guiPointerY.Num + Nz((MouseY - stage.guiPointerY.Num) / (1 + 5 * stage.timeDelta.Num));
        stage.guiFocusEffect = stage.guiFocusEffect.Num - stage.timeDelta.Num;
        _clickTaken = false;

        PenClear();
        DrawMenuBackground();
        DrawMenuButtons();
        DrawBannerCarousel();

        if (_menuDialog == MenuDialog.Create) DrawCreateDialog();
        else if (_menuDialog == MenuDialog.Load) DrawLoadDialog();

        if (Eq(stage.guiSplash, 1)) DrawWelcomePopup();
        if (Eq(stage.errorPopup, 1)) ElementError(); // Glue3D's message popup

        // Tooltips, exactly as the editor draws them.
        if (Eq(stage.guiInfo, 1)) stage.guiInfoEffect = stage.guiInfoEffect.Num + 0.15;
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_gui.info", stage.guiInfoX, stage.guiInfoY, 90, 35, (1 - stage.guiInfoEffect.Num) * 100, -50, 0, "", "", 1, "", "");
        TextOnScaleCenterGbcCursorLimitScroll(stage.guiInfoName, stage.guiInfoX, stage.guiInfoY, 0.67 + Num(Gt(stage.guiInfoName.Str.Length, 4)) * ((stage.guiInfoName.Str.Length - 5) * -0.015), 1, 0, (1 - stage.guiInfoEffect.Num) * 135, 100, 0, "", 1, "", "");
        stage.guiInfoEffect = stage.guiInfoEffect.Num * 0.9;

        MainAfterControl(); // pointer, click bookkeeping, theme contrast
    }

    // ================= small helpers =================

    bool MenuInputFree => Eq(stage.guiFreeze, 0) && Eq(stage.guiSplash, 0) && !Eq(stage.errorPopup, 1);

    bool Hovering(double x1, double y1, double x2, double y2) =>
        MenuInputFree && MouseX >= x1 && MouseX <= x2 && MouseY <= y1 && MouseY >= y2;

    /// <summary>A fresh click this frame (same rule Glue3D's buttons use), given to one control only.</summary>
    bool TakeClick()
    {
        if (_clickTaken || !MenuInputFree || !MouseDown || !Eq(stage.guiPrevMouseState, 0)) return false;
        _clickTaken = true;
        StartSound("Windows Vista Beta Balloon");
        return true;
    }

    /// <summary>Glue3D text (its own font). Dark text by default, like dialog text.</summary>
    void Label(string text, double x, double y, double scale, bool center = false, double ghost = 50, double brightness = -100, int limit = 0)
    {
        TextOnScaleCenterGbcCursorLimitScroll(text, x, y, scale, center ? 1 : 0, "", ghost, brightness, 0, "", "",
            limit > 0 ? (Value)limit : (Value)"", "");
    }

    /// <summary>Small grey "info" text, like the hints in Glue3D's dialogs.</summary>
    void InfoText(string text, double x, double y, double scale = 0.45) =>
        TextOnScaleCenterGbcCursorLimitScroll(text, x, y, scale, "", "", 25, 30, double.NegativeInfinity, "", "", "", "");

    // ================= background, logo, buttons =================

    void DrawMenuBackground()
    {
        double t = stage.timeTimer.Num;
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_gui.contrast", 0, 0, 90, 100, 0, -100, 0, "", "", "", "", "");
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_engine.clouds", 0, 0, 90 + 0.6 * Nz(Sin(t * 6)), 130, 0, -15, 0, "", "", "", "", "");
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_gui.darkfade", 0, -120, 90, 100, 20, 0, 0, "", "", "", "", "");
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_engine.beam", 0, 112, 90, 125, 55, 0, 0, "", "", "", "", "");
        // The Glue3D logo, laid out like the start-up animation.
        double bob = 1.5 * Nz(Sin(t * 90));
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_engine.g", -48, 122 + bob, 90, 24, 0, 0, 0, "", "", "", 1, "");
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_engine.l", -14, 118 + bob, 90, 24, 0, 0, 0, "", "", "", 1, "");
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_engine.u", 14, 118 + bob, 90, 24, 0, 0, 0, "", "", "", 1, "");
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_engine.e3d", 50, 110 + bob, 90, 26, 0, 0, 0, "", "", "", 1, "");

        string root = ProjectFiles.ProjectsRoot.ToLowerInvariant();
        TextOnScaleCenterGbcCursorLimitScroll("projects folder: " + root, -232, -168, 0.4, "", "", 45, 100, 0, "", "", 70, "");
    }

    void DrawMenuButtons()
    {
        MenuButton(-215, 62, -25, 17, "_icon.filenew", "create a new project", "name it and pick the engine", () =>
        {
            EditAddress(NameAddress, "");
            _menuDialog = MenuDialog.Create;
        });
        MenuButton(-215, 5, -25, -40, "_icon.filemove", "load a project", "open a saved project folder", () =>
        {
            OpenBrowser(ProjectFiles.ProjectsRoot);
            _menuDialog = MenuDialog.Load;
        });
    }

    void MenuButton(double x1, double y1, double x2, double y2, string icon, string title, string subtitle, Action onClick)
    {
        bool hover = _menuDialog == MenuDialog.None && Hovering(x1, y1, x2, y2);
        double mid = (y1 + y2) / 2;
        RenderBoxToBoxTypeBorderAlternate(x1, y1, x2, y2, 1, 2, hover ? 1 : 0, "", "");
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage(icon, x1 + 24, mid + 1, 90, hover ? 78 : 72, 5, hover ? 10 : 0, 0, "", "", "", 1, "");
        Label(title, x1 + 46, mid + 8, 0.66, ghost: 35);
        InfoText(subtitle, x1 + 46, mid - 11, 0.43);
        if (hover)
        {
            stage.guiPointerID = "click";
            if (TakeClick()) onClick();
        }
    }

    /// <summary>Glue3D's feature banners, cycling on the right.</summary>
    void DrawBannerCarousel()
    {
        double t = stage.timeTimer.Num;
        int index = (int)Math.Floor(t / 4) % 6 + 1;
        double phase = t % 4;
        double fade = phase < 0.4 ? (1 - phase / 0.4) * 100 : phase > 3.6 ? (phase - 3.6) / 0.4 * 100 : 0;
        RenderBoxToBoxTypeBorderAlternate(22, 72, 208, -132, 1, 2, 0, "", "");
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_engine.banner_" + index, 115, -30, 90, 51, fade, 0, 0, "", "", "", 1, "");
    }

    void DrawWelcomePopup()
    {
        // Glue3D's original welcome popup (from "Editor: screen"), now shown over the start menu.
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_engine.splash", 0, 0, 90, 81, 50 + Nz(5 / Timer), -100, 0, "", "", "", "", "");
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_engine.splash", 0, 0, 90, 80, 5 / Timer, 0, 0, "", "", "", "", "");
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_button.proceed", 155, -100, 90, 60, 25, 0, 0, 1, "", "", 1, "let's go!");
        if (Eq(stage.guiClick, 1)) stage.guiSplash = 0;
    }

    // ================= "create a new project" =================

    string EngineString()
    {
        string code = (_engineTile ? "t" : "") + (_engineSegment ? "s" : "") + (_engineMode7 ? "m" : "");
        return code switch
        {
            "t" => "2dray: tile,t,",
            "ts" => "2dray: tile+seg,ts,",
            "tm" => "2dray: tile+mode7,tm,",
            "tsm" => "2dray: tile+seg+m7,tsm,",
            "s" => "2dray: segment,s,",
            "sm" => "2dray: seg+mode7,sm,",
            "m" => "2dray: mode7,m,",
            _ => "",
        };
    }

    void DrawCreateDialog()
    {
        // Same layout as Glue3D's "new object" dialog.
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_gui.dark", 0, 0, 90, 100, 0, 0, 0, 0, "", "", 0, "");
        RenderBoxToBoxTypeBorderAlternate(-95, 85, 95, -135, 1, 2, Num(Gt(stage.guiFocusEffect, 0)) * 1, "", "");
        Label("new project", -90, 78, 0.55, ghost: 75);

        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_button.delete", 85, 75, 90, 38, 30, 0, 0, MenuInputFree ? 1 : 0, "", "", 1, "");
        if (Eq(stage.guiClick, 1)) { _menuDialog = MenuDialog.None; return; }

        Label("name your project", 0, 55, 0.67, center: true);
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_icon.filenew", -62, 28, 90, 70, 10, 0, 0, MenuInputFree ? 1 : 0, "", "", 1, "project");
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_gui.info", 14, 28, 90, 35, 50, -100, 0, "", "", "", 0, "");
        string name = stage.ramEngineData.Item(NameAddress * 2).Str;
        TextOnScaleCenterGbcCursorLimitScroll(name, -28, 27.5, 0.75 + Num(Gt(name.Length, 6)) * ((name.Length - 7) * -0.022), "", "", 25, 30, double.NegativeInfinity, 1, "", "", "");

        Label("choose the engine", 0, 0, 0.65, center: true);
        EngineToggle("_engine.tile", "tile", -58, ref _engineTile);
        EngineToggle("_engine.segment", "segment", 0, ref _engineSegment);
        EngineToggle("_engine.mode7", "mode7", 58, ref _engineMode7);
        string engine = EngineString();
        InfoText(engine == "" ? "pick at least one" : engine.Split(',')[0], engine == "" ? -32 : -48, -84, 0.48);

        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_button.info", -85, -118, 90, 28, 40, 0, 0, MenuInputFree ? 1 : 0, "", "", 1, "");
        InfoText("saved in the projects folder", -74, -112, 0.42);
        InfoText("next to glue3d.exe", -74, -123, 0.42);

        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_button.accept", 70, -112, 90, 45, 5, -10, 0, MenuInputFree ? 1 : 0, "", "", 1, "create");
        if (Eq(stage.guiClick, 1) || (Eq(stage.inputSubmit, 1) && MenuInputFree))
        {
            stage.inputSubmit = 0;
            TryCreateProject();
        }
    }

    void EngineToggle(string costume, string label, double x, ref bool on)
    {
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage(costume, x, -35, 90, on ? 26 : 20, on ? 10 : 70, 0, 0, MenuInputFree ? 1 : 0, "", "", on ? 1 : 0, label);
        if (Eq(stage.guiClick, 1)) on = !on;
        Label(label, x, -66, 0.52, center: true, ghost: on ? 30 : 75);
    }

    void TryCreateProject()
    {
        string name = stage.ramEngineData.Item(NameAddress * 2).Str.Trim().Trim('.');
        string engine = EngineString();
        if (name == "" || name == "0") { Notify("please name your project", "_icon.warn", "Error_1", 1); return; }
        if (engine == "") { Notify("pick at least one engine part", "_icon.configure", "Error_1", 1); return; }
        string folder = Path.Combine(ProjectFiles.ProjectsRoot, name);
        if (Directory.Exists(folder)) { Notify("that project already exists", "_icon.warn", "Error_1", 1); return; }

        // Glue3D's own "format": a fresh, empty project.
        StartEngine(1);
        stage.ramEngineData.Replace(300, "none");
        stage.guiSplash = 0;
        // The engine choice, exactly what "settings > configure engine" does.
        EditAddress(36, engine);
        DecodeIDSeperatorIncludeSeperatorSpecialdot(stage.ramEngineData.Item(72), "", "", "", "");
        stage.engineType = stage.decodedElements.Item(2);
        EditAddress(38, "configured");

        ProjectFolder = folder;
        ProjectName = name;
        try
        {
            ProjectFiles.Save(folder, name, engine, typeof(Stage), stage);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            Notify("could not create the project folder", "_icon.warn", "Error_3", "");
            return;
        }
        EnterEditor();
        Notify("project created", "_icon.filenew", "Windows Vista Beta Contact Online", 1);
    }

    // ================= "load a project" =================

    void OpenBrowser(string folder)
    {
        _browseFolder = folder;
        _browseScroll = 0;
        _browseEntries.Clear();
        try
        {
            if (folder == "")
            {
                foreach (var d in DriveInfo.GetDrives())
                    if (d.IsReady) _browseEntries.Add(new BrowseEntry(d.RootDirectory.FullName, d.Name, d.DriveType.ToString().ToLowerInvariant(), false, false));
                return;
            }
            var parent = Directory.GetParent(folder);
            if (parent != null) _browseEntries.Add(new BrowseEntry(parent.FullName, "..", "back", false, true));
            else if (OperatingSystem.IsWindows()) _browseEntries.Add(new BrowseEntry("", "..", "drives", false, true));

            var dirs = new List<string>(Directory.GetDirectories(folder));
            dirs.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var d in dirs)
            {
                var attrs = File.GetAttributes(d);
                if ((attrs & (FileAttributes.Hidden | FileAttributes.System)) != 0 || Path.GetFileName(d).StartsWith('.')) continue;
                var info = ProjectFiles.IsProjectFolder(d) ? ProjectFiles.ReadInfo(d) : null;
                string detail = info == null ? "" :
                    (info.Engine.Split(',')[0].Replace("2dray: ", "") + "  " + (info.Saved == DateTime.MinValue ? "" : info.Saved.ToString("yyyy-MM-dd")));
                _browseEntries.Add(new BrowseEntry(d, Path.GetFileName(d), detail, info != null, false));
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            Notify("can't open that folder", "_icon.warn", "Error_1", 1);
        }
    }

    void DrawLoadDialog()
    {
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_gui.dark", 0, 0, 90, 100, 0, 0, 0, 0, "", "", 0, "");
        RenderBoxToBoxTypeBorderAlternate(-125, 100, 125, -145, 1, 2, Num(Gt(stage.guiFocusEffect, 0)) * 1, "", "");
        Label("load project", -120, 93, 0.55, ghost: 75);

        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_button.delete", 115, 90, 90, 38, 30, 0, 0, MenuInputFree ? 1 : 0, "", "", 1, "");
        if (Eq(stage.guiClick, 1)) { _menuDialog = MenuDialog.None; return; }

        Label("pick the project folder", 0, 72, 0.62, center: true);
        string where = _browseFolder == "" ? "this computer" : _browseFolder.ToLowerInvariant();
        if (where.Length > 52) where = "..." + where.Substring(where.Length - 49);
        InfoText(where, -117, 57, 0.42);

        // Mouse wheel scrolls (Glue3D turns the wheel into guiScroll).
        int maxScroll = Math.Max(0, _browseEntries.Count - BrowseRows);
        if (MenuInputFree && stage.guiScroll.Num != 0)
            _browseScroll = Math.Clamp(_browseScroll - (int)stage.guiScroll.Num, 0, maxScroll);

        for (int row = 0; row < BrowseRows; row++)
        {
            int i = _browseScroll + row;
            if (i >= _browseEntries.Count) break;
            var e = _browseEntries[i];
            double y = 38 - row * 22;
            bool hover = Hovering(-117, y + 10, 117, y - 10);
            if (hover) RenderBoxToBoxTypeBorderAlternate(-117, y + 10, 117, y - 10, 1, 2, 1, "", "");
            string icon = e.IsUp ? "_icon.filemove" : e.IsProject ? "_icon.scene" : "_icon.file";
            Render2DOnDirSizeGbcInteractiveShadowHoverMessage(icon, -103, y, 90, e.IsProject ? 48 : 42, 10, 0, 0, "", "", "", 1, "");
            string label = e.Label.ToLowerInvariant();
            Label(label.Length > 26 ? label.Substring(0, 25) + "~" : label, -88, y + 1, 0.56, ghost: e.IsProject ? 25 : 45);
            if (e.Detail != "") InfoText(e.Detail.ToLowerInvariant(), 28, y + 1, 0.42);
            if (hover)
            {
                stage.guiPointerID = "click";
                if (e.IsProject) { stage.guiInfo = 1; stage.guiInfoName = "open"; stage.guiInfoX = MouseX; stage.guiInfoY = stage.guiInfoY.Num + Nz((y + 22 - stage.guiInfoY.Num) / 2); }
                if (TakeClick())
                {
                    if (e.IsProject) LoadProject(e.Path);
                    else OpenBrowser(e.Path);
                    return;
                }
            }
        }
        if (_browseEntries.Count == 0 || (_browseEntries.Count == 1 && _browseEntries[0].IsUp))
            InfoText("this folder is empty", -40, 0, 0.5);

        // Footer: shortcut back to the projects folder, and a scroll hint.
        Render2DOnDirSizeGbcInteractiveShadowHoverMessage("_button.refresh", -108, -130, 90, 30, 30, 0, 0, MenuInputFree ? 1 : 0, "", "", 1, "projects folder");
        if (Eq(stage.guiClick, 1)) OpenBrowser(ProjectFiles.ProjectsRoot);
        InfoText(maxScroll > 0 ? $"scroll for more  ({_browseScroll + 1}-{Math.Min(_browseEntries.Count, _browseScroll + BrowseRows)} of {_browseEntries.Count})" : "click a project to open it", -94, -129, 0.42);
    }

    public void LoadProject(string folder)
    {
        ProjectFiles.Info info;
        try
        {
            info = ProjectFiles.Load(folder, typeof(Stage), stage);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            Notify("this project could not be loaded", "_icon.warn", "Error_3", "");
            return;
        }
        // Like reopening a saved project in Scratch: Glue3D rebuilds the editor from the saved data.
        stage.guiSplash = 0;
        StartEngine(0);
        ProjectFolder = folder;
        ProjectName = info.Name;
        EnterEditor();
        Notify("loaded " + info.Name, "_icon.filemove", "Exclame_2", 1);
    }

    /// <summary>Leaves the menu and shows the editor, with all temporary GUI state cleared.</summary>
    void EnterEditor()
    {
        stage.guiSplash = 0;
        stage.guiFreeze = 0;
        stage.errorPopup = 0;
        stage.inputAddress = 0;
        stage.inputSubmit = 0;
        stage.menuSelection = "";
        stage.editorSceneTool = "";
        stage.editorSceneSubject = "";
        stage.dragId = "";
        stage.timeTimer = Timer; // no time jump on the first editor frame
        EditAddress(13, 0);      // no dropdown/menu open
        _selected = null;        // gizmo selection
        _menuDialog = MenuDialog.None;
        MainMenuActive = false;
        Raylib.SetWindowTitle("Glue3D - " + ProjectName);
    }

    /// <summary>Glue3D's message popup. The short click delay stops the click that caused it from closing it.</summary>
    void Notify(Value message, Value icon, Value sound, Value dismissable)
    {
        stage.guiClickLimit = 0.5;
        Message(message, icon, sound, dismissable);
    }

    // ================= saving (File > save project.., or Ctrl+S) =================

    /// <summary>Saves the open project into its folder.</summary>
    public void SaveProject()
    {
        if (ProjectFolder == null || ProjectName == null)
        {
            Notify("no project is open", "_icon.warn", "Error_1", 1);
            return;
        }
        try
        {
            ProjectFiles.Save(ProjectFolder, ProjectName, stage.ramEngineData.Item(72).Str, typeof(Stage), stage);
            Notify("project saved", "_icon.file", "Windows XP Print complete", 1);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            Notify("the project could not be saved", "_icon.warn", "Error_3", "");
        }
    }

    /// <summary>Hooked to Engine.BeforeScripts in Program.cs: Ctrl+S saves while editing.</summary>
    public void SaveShortcutUpdate()
    {
        if (MainMenuActive) return;
        bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        if (ctrl && Raylib.IsKeyPressed(KeyboardKey.S)) SaveProject();
    }
}
