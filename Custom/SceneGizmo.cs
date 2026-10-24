// Hand-written additions to the generated Glue3DSprite (not overwritten by the converter):
// a Unity-style toolbar (Select / Move / Rotate / Scale + gizmo on/off) and 3D gizmos for the scene view.
using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;
using Scratch;
using static Scratch.Ops;

namespace Glue3D;

public enum SceneTool { Select, Move, Rotate, Scale }

public sealed partial class Glue3DSprite
{
    // ---------------- settings ----------------

    /// <summary>Tool selected when the app starts.</summary>
    public SceneTool CurrentSceneTool = SceneTool.Move;
    /// <summary>Show gizmo handles on the selected object (the toolbar's last button).</summary>
    public bool ShowGizmo = true;
    /// <summary>Gizmo arm length on screen, in stage units.</summary>
    const double GizmoScreenSize = 42;
    /// <summary>Snapping while Ctrl is held.</summary>
    const double MoveSnap = 0.25, RotateSnap = 15, ScaleSnap = 0.1;

    // ---------------- state ----------------

    string? _selected;           // name of the selected object
    int _hotHandle = -1;         // handle under the mouse (for highlighting)
    int _dragHandle = -1;        // handle being dragged, or DirectDrag
    const int CenterHandle = 3, DirectDrag = 10;
    double _dragStartMouseX, _dragStartMouseY;
    readonly double[] _dragStart = new double[3]; // the 3 values being edited, when the drag began
    Vector2 _dragAxisScreen;     // move/scale: axis direction on screen (stage units per world unit * arm)
    double _dragArm;             // world length of the gizmo arm when the drag began
    Vector2 _ringTangent; double _ringRadius;
    (double x, double z) _dragPlaneStart;
    bool _consumingClick, _prevMouseDown, _sceneViewActive;
    int _hoverButton = -1;

    static readonly string[] AxisNames = { "x", "y", "z" };
    static readonly Color[] AxisColors =
    {
        new(232, 64, 64, 255), new(110, 204, 60, 255), new(66, 128, 244, 255),
    };
    static readonly Color HotColor = new(255, 214, 0, 255);

    // ---------------- where the 3D view is ----------------

    /// <summary>True while the editable 3D scene view is on screen and nothing (dialog, menu, running game) is over it.</summary>
    bool SceneViewActive() =>
        Eq(stage.ramEngineData.Item(76), "configured")
        && Eq(stage.editorTab, "scene")
        && Eq(stage.allRun, 0)
        && Eq(stage.engineFullscreen, 0)
        && Eq(stage.guiFreeze, 0)
        && Eq(stage.ramEngineData.Item(26), 0)   // no dropdown/menu open
        && Eq(stage.editorSceneTool, "");         // none of Glue3D's own scene tools in use

    double ViewLeft => stage.engineScreenTopleftx.Num;
    double ViewTop => stage.engineScreenToplefty.Num;
    double ViewRight => stage.engineScreenBottomrightx.Num;
    double ViewBottom => stage.engineScreenBottomrighty.Num;

    bool InSceneViewport(double x, double y) => x > ViewLeft && x < ViewRight && y < ViewTop && y > ViewBottom;

    // ---------------- 3D <-> screen, the same math as the engine's "project point" block ----------------

    (double camX, double camY, double camZ, double sin, double cos, double fov, double pitch, double kx, double ky) Camera()
    {
        double yaw = stage.ramEngineData.Item(118).Num;
        double pm = Math.Abs(stage.enginePerspectivemultiplier.Num);
        bool stretched = Eq(stage.ramEngineData.Item(178), 0);
        return (stage.ramEngineData.Item(112).Num, stage.ramEngineData.Item(114).Num, stage.ramEngineData.Item(116).Num,
                Sin(yaw), Cos(yaw), stage.engineFov.Num, stage.ramEngineData.Item(120).Num,
                stretched ? 1.15 : 1, stretched ? 1.15 + 0.06 * pm : 1);
    }

    /// <summary>World point to stage coordinates. False if it is behind the camera.</summary>
    bool Project(double x, double y, double z, out Vector2 screen, out double depth)
    {
        var c = Camera();
        double ox = x - c.camX, oy = y - c.camY, oz = z - c.camZ;
        double nx = c.cos * ox - c.sin * oz, nz = c.sin * ox + c.cos * oz;
        depth = nz;
        screen = default;
        if (nz <= 0.05) return false;
        double w = ViewRight - ViewLeft, h = ViewTop - ViewBottom;
        double midX = stage.engineScreenMidx.Num, midY = stage.engineScreenMidy.Num;
        double sx = c.kx * (midX + w * (nx * c.fov / nz) / 480);
        double sy = c.ky * (midY + h * (oy * c.fov / nz - 3 * c.pitch) / 360);
        screen = new Vector2((float)sx, (float)sy);
        return true;
    }

    /// <summary>The point on the horizontal plane at height y that is under a stage-coordinate position.</summary>
    bool UnprojectToPlane(double sx, double sy, double planeY, out double x, out double z)
    {
        var c = Camera();
        x = z = 0;
        double w = ViewRight - ViewLeft, h = ViewTop - ViewBottom;
        double u = (sx / c.kx - stage.engineScreenMidx.Num) * 480 / w;        // = nx * fov / nz
        double v = (sy / c.ky - stage.engineScreenMidy.Num) * 360 / h + 3 * c.pitch; // = ny * fov / nz
        double ny = planeY - c.camY;
        if (Math.Abs(v) < 1e-6) return false;
        double nz = ny * c.fov / v;
        if (nz <= 0.05 || nz > 500) return false;
        double nx = u * nz / c.fov;
        x = c.cos * nx + c.sin * nz + c.camX;
        z = -c.sin * nx + c.cos * nz + c.camZ;
        return true;
    }

    /// <summary>World length that appears about GizmoScreenSize stage units long at this depth.</summary>
    double ArmLength(double depth) =>
        GizmoScreenSize * depth * 480 / ((ViewRight - ViewLeft) * Math.Max(1, Camera().fov));

    // ---------------- scene objects ----------------

    readonly record struct SceneObject(string Name, string Type, double X, double Y, double Z);

    /// <summary>Objects in the scene that can be moved in 3D (UI elements are 2D, so they're skipped).</summary>
    List<SceneObject> SceneObjects()
    {
        var list = new List<SceneObject>();
        int index = 2;
        int count = (int)stage.scene.Item(1).Num;
        for (int i = 0; i < count && index <= stage.scene.Length; i++)
        {
            string type = stage.scene.Item(index + 5).Str;
            string model = stage.scene.Item(index + 7).Str;
            if (model != "elementui")
                list.Add(new SceneObject(stage.scene.Item(index + 3).Str, type,
                    stage.scene.Item(index + 11).Num, stage.scene.Item(index + 13).Num, stage.scene.Item(index + 15).Num));
            int size = (int)stage.scene.Item(index).Num;
            if (size <= 0) break;
            index += size;
        }
        return list;
    }

    double ReadObject(string name, string key)
    {
        EditDataInObjectDataOperation(name, key, 0, "read");
        return stage.dataReturn.Num;
    }

    void WriteObject(string name, string key, double value) =>
        EditDataInObjectDataOperation(name, key, Math.Round(value, 4), "set");

    string[] EditedKeys() => CurrentSceneTool switch
    {
        SceneTool.Rotate => new[] { "ry", "rx", "rz" }, // rings around the X, Y and Z axes (rx is yaw in Glue3D)
        SceneTool.Scale => new[] { "sx", "sy", "sz" },
        _ => new[] { "x", "y", "z" },
    };

    // ---------------- toolbar ----------------

    const double ButtonSize = 17, ButtonGap = 2;
    static readonly string[] ButtonTips = { "Select (opens Glue3D's object menu)", "Move", "Rotate", "Scale", "Show gizmo" };

    (double x, double y) ButtonPos(int i) =>
        (ViewLeft + 5 + i * (ButtonSize + ButtonGap) + (i == 4 ? 6 : 0), ViewTop - 5);

    int ToolbarButtonAt(double mx, double my)
    {
        for (int i = 0; i < 5; i++)
        {
            var (bx, by) = ButtonPos(i);
            if (mx >= bx && mx <= bx + ButtonSize && my <= by && my >= by - ButtonSize) return i;
        }
        return -1;
    }

    // ---------------- picking and handles ----------------

    string? PickObject(double mx, double my)
    {
        string? best = null;
        double bestDist = 10;
        foreach (var o in SceneObjects())
        {
            foreach (double yOff in new[] { 0.0, 0.25 }) // the object's pivot and its icon (drawn a bit higher)
            {
                if (!Project(o.X, o.Y + yOff, o.Z, out var p, out _)) continue;
                double d = Math.Sqrt((p.X - mx) * (p.X - mx) + (p.Y - my) * (p.Y - my));
                if (d < bestDist) { bestDist = d; best = o.Name; }
            }
        }
        return best;
    }

    static double DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = ab.LengthSquared() < 1e-6f ? 0 : Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0, 1);
        return Vector2.Distance(p, a + ab * t);
    }

    static Vector3 Axis(int i) => i == 0 ? Vector3.UnitX : i == 1 ? Vector3.UnitY : Vector3.UnitZ;

    /// <summary>Point on a rotation ring: ring i goes around axis i.</summary>
    static Vector3 RingPoint(int i, double angle)
    {
        float c = (float)Math.Cos(angle), s = (float)Math.Sin(angle);
        return i switch
        {
            0 => new Vector3(0, c, s),
            1 => new Vector3(s, 0, c),
            _ => new Vector3(c, s, 0),
        };
    }

    bool SelectedPivot(out Vector3 pivot, out Vector2 screen, out double arm)
    {
        pivot = default; screen = default; arm = 0;
        if (_selected == null) return false;
        pivot = new Vector3((float)ReadObject(_selected, "x"), (float)ReadObject(_selected, "y"), (float)ReadObject(_selected, "z"));
        if (!Project(pivot.X, pivot.Y, pivot.Z, out screen, out double depth)) return false;
        arm = ArmLength(depth);
        return true;
    }

    bool ProjectV(Vector3 p, out Vector2 s) => Project(p.X, p.Y, p.Z, out s, out _);

    /// <summary>Which gizmo handle is under the mouse (0-2 = X/Y/Z, 3 = center), or -1.</summary>
    int HandleAt(double mx, double my)
    {
        if (!ShowGizmo || CurrentSceneTool == SceneTool.Select) return -1;
        if (!SelectedPivot(out var pivot, out var center, out double arm)) return -1;
        var m = new Vector2((float)mx, (float)my);
        int best = -1; double bestDist = 6;
        if (CurrentSceneTool == SceneTool.Rotate)
        {
            for (int i = 0; i < 3; i++)
            {
                Vector2? prev = null;
                for (int k = 0; k <= 48; k++)
                {
                    if (!ProjectV(pivot + RingPoint(i, k * Math.PI / 24) * (float)arm, out var p)) { prev = null; continue; }
                    if (prev is { } a)
                    {
                        double d = DistToSegment(m, a, p);
                        if (d < bestDist) { bestDist = d; best = i; }
                    }
                    prev = p;
                }
            }
            return best;
        }
        if (Vector2.Distance(m, center) < 7) return CenterHandle;
        for (int i = 0; i < 3; i++)
        {
            if (!ProjectV(pivot + Axis(i) * (float)arm, out var end)) continue;
            double d = DistToSegment(m, center, end);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    // ---------------- per-frame update (before scripts run) ----------------

    /// <summary>Hooked to Engine.BeforeScripts in Program.cs.</summary>
    public void SceneGizmoUpdate()
    {
        var input = Engine.Input;
        bool down = input.MouseDown;
        bool pressed = down && !_prevMouseDown;
        _prevMouseDown = down;
        _sceneViewActive = SceneViewActive();
        if (!_sceneViewActive)
        {
            _dragHandle = -1; _hotHandle = -1; _hoverButton = -1;
            if (_consumingClick && down) input.MouseDown = false; else _consumingClick = false;
            return;
        }
        if (_selected != null && stage.scene.IndexOf(_selected + ".start") == 0) _selected = null; // deleted

        double mx = MouseX, my = MouseY;
        _hoverButton = ToolbarButtonAt(mx, my);

        if (pressed && _hoverButton >= 0)
        {
            if (_hoverButton == 4) ShowGizmo = !ShowGizmo;
            else CurrentSceneTool = (SceneTool)_hoverButton;
            StartSound("Windows Vista Beta Blip 1");
            _consumingClick = true;
        }
        else if (pressed && CurrentSceneTool != SceneTool.Select && InSceneViewport(mx, my) && !RightMouseDown)
        {
            _consumingClick = true; // Glue3D's own click handling (object menu) is only used in Select mode
            int handle = HandleAt(mx, my);
            if (handle >= 0) BeginDrag(handle, mx, my);
            else
            {
                string? picked = PickObject(mx, my);
                if (picked != _selected && picked != null) StartSound("Windows Navigation Start3");
                _selected = picked;
                if (_selected != null && !ShowGizmo) BeginDrag(DirectDrag, mx, my);
            }
        }

        if (_dragHandle >= 0 && down) UpdateDrag(mx, my);
        if (!down) { _dragHandle = -1; _consumingClick = false; }
        else if (_consumingClick) input.MouseDown = false; // hide this click from the Scratch scripts

        _hotHandle = _dragHandle >= 0 ? _dragHandle : (down ? -1 : HandleAt(mx, my));
    }

    void BeginDrag(int handle, double mx, double my)
    {
        if (_selected == null || !SelectedPivot(out var pivot, out var center, out double arm)) return;
        _dragHandle = handle;
        _dragStartMouseX = mx; _dragStartMouseY = my;
        _dragArm = arm;
        var keys = EditedKeys();
        for (int i = 0; i < 3; i++) _dragStart[i] = ReadObject(_selected, keys[i]);

        bool planeMove = (handle == CenterHandle || handle == DirectDrag) && CurrentSceneTool == SceneTool.Move;
        if (planeMove && UnprojectToPlane(mx, my, pivot.Y, out double px, out double pz)) _dragPlaneStart = (px, pz);
        else if (planeMove) _dragHandle = -1;

        if (handle < 3 && CurrentSceneTool != SceneTool.Rotate && ProjectV(pivot + Axis(handle) * (float)arm, out var end))
            _dragAxisScreen = end - center;

        if (handle < 3 && CurrentSceneTool == SceneTool.Rotate)
        {
            // Tangent of the ring where it was grabbed, so dragging along the ring turns it.
            var m = new Vector2((float)mx, (float)my);
            double bestA = 0, bestD = double.MaxValue;
            for (int k = 0; k < 96; k++)
            {
                double a = k * Math.PI / 48;
                if (ProjectV(pivot + RingPoint(handle, a) * (float)arm, out var p) && Vector2.Distance(p, m) < bestD)
                { bestD = Vector2.Distance(p, m); bestA = a; }
            }
            ProjectV(pivot + RingPoint(handle, bestA) * (float)arm, out var p0);
            ProjectV(pivot + RingPoint(handle, bestA + 0.05) * (float)arm, out var p1);
            var t = p1 - p0;
            _ringTangent = t.LengthSquared() > 1e-8f ? Vector2.Normalize(t) : Vector2.UnitX;
            _ringRadius = Math.Max(8, Vector2.Distance(p0, center));
        }
    }

    void UpdateDrag(double mx, double my)
    {
        if (_selected == null) return;
        bool snap = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        var keys = EditedKeys();
        double dx = mx - _dragStartMouseX, dy = my - _dragStartMouseY;
        var mouseDelta = new Vector2((float)dx, (float)dy);
        static double Snap(double v, double step, bool on) => on ? Math.Round(v / step) * step : v;

        switch (CurrentSceneTool)
        {
            case SceneTool.Move when _dragHandle < 3:
            {
                double along = AlongAxis(mouseDelta);
                WriteObject(_selected, keys[_dragHandle], Snap(_dragStart[_dragHandle] + along, MoveSnap, snap));
                break;
            }
            case SceneTool.Move: // center handle, or dragging the object with the gizmo hidden: slide on the ground
            {
                if (!UnprojectToPlane(mx, my, _dragStart[1], out double px, out double pz)) break;
                WriteObject(_selected, "x", Snap(_dragStart[0] + px - _dragPlaneStart.x, MoveSnap, snap));
                WriteObject(_selected, "z", Snap(_dragStart[2] + pz - _dragPlaneStart.z, MoveSnap, snap));
                break;
            }
            case SceneTool.Rotate when _dragHandle < 3:
            {
                double degrees = Vector2.Dot(mouseDelta, _ringTangent) / _ringRadius * 180 / Math.PI;
                WriteObject(_selected, keys[_dragHandle], WrapAngle(Snap(_dragStart[_dragHandle] + degrees, RotateSnap, snap)));
                break;
            }
            case SceneTool.Rotate: // gizmo hidden: drag left/right to turn around the vertical axis
                WriteObject(_selected, "rx", WrapAngle(Snap(_dragStart[1] + dx * 1.5, RotateSnap, snap)));
                break;
            case SceneTool.Scale when _dragHandle < 3:
            {
                double factor = Math.Max(0.01, 1 + AlongAxis(mouseDelta) / Math.Max(1e-6, _dragArm));
                WriteObject(_selected, keys[_dragHandle], Math.Max(0.01, Snap(_dragStart[_dragHandle] * factor, ScaleSnap, snap)));
                break;
            }
            case SceneTool.Scale: // center handle or gizmo hidden: uniform scale
            {
                double factor = Math.Max(0.01, 1 + (dx + dy) / 60);
                for (int i = 0; i < 3; i++)
                    WriteObject(_selected, keys[i], Math.Max(0.01, Snap(_dragStart[i] * factor, ScaleSnap, snap)));
                break;
            }
        }
    }

    /// <summary>World distance along the dragged axis for a mouse movement (stage units).</summary>
    double AlongAxis(Vector2 mouseDelta)
    {
        float len2 = _dragAxisScreen.LengthSquared();
        if (len2 < 1e-6f) return 0;
        return Vector2.Dot(mouseDelta, _dragAxisScreen) / len2 * _dragArm;
    }

    static double WrapAngle(double a)
    {
        a %= 360;
        if (a > 180) a -= 360;
        if (a <= -180) a += 360;
        return a;
    }

    // ---------------- drawing (after the stage is drawn) ----------------

    /// <summary>Hooked to Engine.Overlay in Program.cs. Draws the toolbar and gizmo in window pixels.</summary>
    public void SceneGizmoDraw(Rectangle stageRect)
    {
        if (!_sceneViewActive) return;
        float k = stageRect.Width / 480f;
        Vector2 ToScreen(Vector2 s) => new(stageRect.X + (s.X + 240) * k, stageRect.Y + (180 - s.Y) * k);

        // Keep everything inside the 3D view.
        var tl = ToScreen(new Vector2((float)ViewLeft, (float)ViewTop));
        var br = ToScreen(new Vector2((float)ViewRight, (float)ViewBottom));
        Raylib.BeginScissorMode((int)tl.X, (int)tl.Y, (int)(br.X - tl.X), (int)(br.Y - tl.Y));

        if (_selected != null && SelectedPivot(out var pivot, out var center, out double arm))
        {
            var c = ToScreen(center);
            Raylib.DrawCircleLinesV(c, 9 * k, new Color(255, 214, 0, 200));
            if (ShowGizmo && CurrentSceneTool != SceneTool.Select) DrawGizmo(pivot, arm, k, ToScreen);
            DrawLabel(_selected, c + new Vector2(11 * k, -16 * k), k);
            if (_dragHandle >= 0) DrawReadout(c + new Vector2(11 * k, 4 * k), k);
        }

        DrawToolbar(k, ToScreen);
        Raylib.EndScissorMode();
    }

    void DrawGizmo(Vector3 pivot, double arm, float k, Func<Vector2, Vector2> toScreen)
    {
        if (!ProjectV(pivot, out var c0)) return;
        var center = toScreen(c0);
        float thick = 2.2f * k;

        if (CurrentSceneTool == SceneTool.Rotate)
        {
            for (int i = 0; i < 3; i++)
            {
                var col = i == _hotHandle ? HotColor : AxisColors[i];
                Vector2? prev = null;
                for (int s = 0; s <= 64; s++)
                {
                    if (!ProjectV(pivot + RingPoint(i, s * Math.PI / 32) * (float)arm, out var p)) { prev = null; continue; }
                    var sp = toScreen(p);
                    if (prev is { } a) Raylib.DrawLineEx(a, sp, thick, col);
                    prev = sp;
                }
            }
            Raylib.DrawCircleV(center, 2.5f * k, Color.White);
            return;
        }

        for (int i = 0; i < 3; i++)
        {
            if (!ProjectV(pivot + Axis(i) * (float)arm, out var e)) continue;
            var end = toScreen(e);
            var col = i == _hotHandle ? HotColor : AxisColors[i];
            Raylib.DrawLineEx(center, end, thick, col);
            var dir = end - center;
            if (dir.LengthSquared() < 1) continue;
            dir = Vector2.Normalize(dir);
            var side = new Vector2(-dir.Y, dir.X);
            if (CurrentSceneTool == SceneTool.Move)
            {
                // arrow head
                Vector2 tip = end + dir * 7 * k, l = end + side * 3.5f * k, r = end - side * 3.5f * k;
                Raylib.DrawTriangle(tip, l, r, col);
                Raylib.DrawTriangle(tip, r, l, col);
            }
            else
            {
                float h = 3.5f * k;
                Raylib.DrawRectanglePro(new Rectangle(end.X, end.Y, 2 * h, 2 * h), new Vector2(h, h), 0, col);
            }
        }
        var cc = _hotHandle == CenterHandle ? HotColor : new Color(235, 235, 235, 230);
        float hs = 4 * k;
        Raylib.DrawRectangleRec(new Rectangle(center.X - hs, center.Y - hs, 2 * hs, 2 * hs), cc);
        Raylib.DrawRectangleLinesEx(new Rectangle(center.X - hs, center.Y - hs, 2 * hs, 2 * hs), 1, new Color(40, 40, 40, 200));
    }

    void DrawLabel(string text, Vector2 at, float k)
    {
        float size = 9 * k;
        float w = Engine.Monitors.Measure(text, size);
        Raylib.DrawRectangleRounded(new Rectangle(at.X - 3 * k, at.Y - 1 * k, w + 6 * k, size + 2 * k), 0.4f, 4, new Color(20, 20, 20, 170));
        Engine.Monitors.Text(text, at.X, at.Y, size, Color.White);
    }

    void DrawReadout(Vector2 at, float k)
    {
        if (_selected == null) return;
        var keys = (string[])EditedKeys().Clone();
        Array.Sort(keys, string.CompareOrdinal); // x y z / rx ry rz / sx sy sz
        string text = string.Join("   ", Array.ConvertAll(keys, key => $"{key} {ReadObject(_selected, key):0.##}"));
        DrawLabel(text, at, k);
    }

    void DrawToolbar(float k, Func<Vector2, Vector2> toScreen)
    {
        for (int i = 0; i < 5; i++)
        {
            var (bx, by) = ButtonPos(i);
            var p = toScreen(new Vector2((float)bx, (float)by));
            var r = new Rectangle(p.X, p.Y, (float)ButtonSize * k, (float)ButtonSize * k);
            bool active = i < 4 ? (int)CurrentSceneTool == i : ShowGizmo;
            var bg = active ? new Color(58, 121, 217, 235) : new Color(38, 38, 42, 215);
            if (i == _hoverButton) bg = active ? new Color(80, 140, 230, 245) : new Color(70, 70, 76, 230);
            Raylib.DrawRectangleRounded(r, 0.25f, 4, bg);
            Raylib.DrawRectangleRoundedLinesEx(r, 0.25f, 4, 1, new Color(0, 0, 0, 120));
            DrawToolIcon(i, new Vector2(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width * 0.32f, active);
        }
        if (_hoverButton >= 0)
        {
            var (bx, by) = ButtonPos(_hoverButton);
            var p = toScreen(new Vector2((float)bx, (float)(by - ButtonSize - 3)));
            DrawLabel(ButtonTips[_hoverButton], p, k);
        }
    }

    static void DrawToolIcon(int i, Vector2 c, float s, bool active)
    {
        var col = active ? Color.White : new Color(215, 215, 215, 255);
        float t = Math.Max(1.5f, s * 0.16f);
        switch (i)
        {
            case 0: // pointer arrow
                Raylib.DrawTriangle(c + new Vector2(-s * 0.6f, -s), c + new Vector2(-s * 0.6f, s * 0.6f), c + new Vector2(s * 0.7f, s * 0.15f), col);
                Raylib.DrawLineEx(c + new Vector2(0, s * 0.2f), c + new Vector2(s * 0.55f, s * 1.0f), t * 1.4f, col);
                break;
            case 1: // four-way arrows
                foreach (var d in new[] { Vector2.UnitX, -Vector2.UnitX, Vector2.UnitY, -Vector2.UnitY })
                {
                    var side = new Vector2(-d.Y, d.X);
                    Raylib.DrawLineEx(c, c + d * s * 0.75f, t, col);
                    var tip = c + d * s * 1.1f;
                    Raylib.DrawTriangle(tip, c + d * s * 0.6f + side * s * 0.35f, c + d * s * 0.6f - side * s * 0.35f, col);
                    Raylib.DrawTriangle(tip, c + d * s * 0.6f - side * s * 0.35f, c + d * s * 0.6f + side * s * 0.35f, col);
                }
                break;
            case 2: // circular arrow
                Raylib.DrawRing(c, s * 0.75f, s * 0.75f + t, 40, 330, 24, col);
                var tipA = c + new Vector2(MathF.Cos(MathF.PI * 40 / 180), MathF.Sin(MathF.PI * 40 / 180)) * s * 0.85f;
                Raylib.DrawTriangle(tipA + new Vector2(s * 0.45f, -s * 0.05f), tipA + new Vector2(-s * 0.2f, -s * 0.45f), tipA + new Vector2(-s * 0.05f, s * 0.35f), col);
                Raylib.DrawTriangle(tipA + new Vector2(s * 0.45f, -s * 0.05f), tipA + new Vector2(-s * 0.05f, s * 0.35f), tipA + new Vector2(-s * 0.2f, -s * 0.45f), col);
                break;
            case 3: // box with a corner handle
                Raylib.DrawRectangleLinesEx(new Rectangle(c.X - s * 0.9f, c.Y - s * 0.3f, s * 1.2f, s * 1.2f), t, col);
                Raylib.DrawLineEx(c + new Vector2(-s * 0.1f, s * 0.1f), c + new Vector2(s * 0.9f, -s * 0.9f), t, col);
                Raylib.DrawRectangleV(c + new Vector2(s * 0.5f, -s * 1.1f), new Vector2(s * 0.6f, s * 0.6f), col);
                break;
            case 4: // XYZ axes
                Raylib.DrawLineEx(c + new Vector2(-s * 0.6f, s * 0.6f), c + new Vector2(s * 0.9f, s * 0.6f), t, AxisColors[0]);
                Raylib.DrawLineEx(c + new Vector2(-s * 0.6f, s * 0.6f), c + new Vector2(-s * 0.6f, -s * 0.95f), t, AxisColors[1]);
                Raylib.DrawLineEx(c + new Vector2(-s * 0.6f, s * 0.6f), c + new Vector2(s * 0.4f, -s * 0.35f), t, AxisColors[2]);
                if (!active) Raylib.DrawLineEx(c + new Vector2(-s, s), c + new Vector2(s, -s), t * 1.2f, new Color(240, 90, 90, 255));
                break;
        }
    }

    // ---------------- testing helper ----------------

    /// <summary>Where an object appears on the stage (used by automated tests).</summary>
    public string DescribeObject(string name)
    {
        double x = ReadObject(name, "x"), y = ReadObject(name, "y"), z = ReadObject(name, "z");
        string pos = Project(x, y, z, out var p, out _) ? $"{p.X:0.#} {p.Y:0.#}" : "offscreen";
        string ends = "";
        if (_selected == name && SelectedPivot(out var pv, out _, out double arm))
            for (int i = 0; i < 3; i++)
                if (ProjectV(pv + Axis(i) * (float)arm, out var e)) ends += $" {AxisNames[i]}-end {e.X:0.#} {e.Y:0.#};";
        string cam = $"cam {stage.ramEngineData.Item(112).Num:0.##} {stage.ramEngineData.Item(114).Num:0.##} {stage.ramEngineData.Item(116).Num:0.##} yaw {stage.ramEngineData.Item(118).Num:0.#} pitch {stage.ramEngineData.Item(120).Num:0.#}; menu {stage.ramEngineData.Item(26).Str}";
        return $"{cam}; {name}: screen {pos};{ends} x {x} y {y} z {z} rx {ReadObject(name, "rx")} ry {ReadObject(name, "ry")} rz {ReadObject(name, "rz")} sx {ReadObject(name, "sx")} sy {ReadObject(name, "sy")} sz {ReadObject(name, "sz")}; tool {CurrentSceneTool} hover {_hoverButton} gizmo {ShowGizmo} selected {_selected} active {_sceneViewActive}";
    }

    /// <summary>Places the camera (used by automated tests).</summary>
    public void SetCamera(double x, double y, double z, double yaw, double pitch)
    {
        CreateAddress("engine.camera_x", x, 56); CreateAddress("engine.camera_y", y, 57); CreateAddress("engine.camera_z", z, 58);
        CreateAddress("engine.camera_direction_x", yaw, 59); CreateAddress("engine.camera_direction_y", pitch, 60);
    }

    /// <summary>Creates a plain 3D object (used by automated tests; also handy from your own code).</summary>
    public void AddTestObject(string name, double x, double y, double z) =>
        CreateObjectTypeModelOnRotationScript(name, "3d", "", x, y, z, 0, 0, 0, "");
}
