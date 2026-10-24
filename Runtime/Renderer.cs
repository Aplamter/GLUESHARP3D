using System;
using System.Numerics;
using Raylib_cs;

namespace Scratch;

/// <summary>
/// Draws the stage the way Scratch's renderer does: a high-quality pen layer (pen lines and
/// stamps, premultiplied alpha) on top of the backdrop, with visible sprites above it.
/// </summary>
public sealed class Renderer
{
    readonly Engine _engine;
    RenderTexture2D _pen;
    int _penW, _penH;
    float _penQ; // pen-layer pixels per stage unit

    Shader _lineShader, _spriteShader;
    int _locEffects, _locColor, _locBrightness, _locGhost, _locPixelate, _locSkinSize;
    // Last uniform values sent, so batches only break when effects actually change.
    int _uEffects = -1; float _uColor = float.NaN, _uBrightness, _uGhost, _uPixelate; Vector2 _uSkin;

    enum Mode { None, Line, Sprite }
    Mode _mode = Mode.None;
    bool _penActive;

    public Renderer(Engine engine) { _engine = engine; }

    public void Init(int width, int height)
    {
        _lineShader = Raylib.LoadShaderFromMemory(Shaders.Vertex, Shaders.LineFragment);
        _spriteShader = Raylib.LoadShaderFromMemory(Shaders.Vertex, Shaders.SpriteFragment);
        _locEffects = Raylib.GetShaderLocation(_spriteShader, "u_effects");
        _locColor = Raylib.GetShaderLocation(_spriteShader, "u_color");
        _locBrightness = Raylib.GetShaderLocation(_spriteShader, "u_brightness");
        _locGhost = Raylib.GetShaderLocation(_spriteShader, "u_ghost");
        _locPixelate = Raylib.GetShaderLocation(_spriteShader, "u_pixelate");
        _locSkinSize = Raylib.GetShaderLocation(_spriteShader, "u_skinSize");
        Rlgl.DisableBackfaceCulling();
        ResizePen(width, height);
    }

    /// <summary>Keeps the pen layer at the on-screen stage resolution ("high quality pen"), keeping its drawing.</summary>
    public void ResizePen(int width, int height)
    {
        width = Math.Max(1, width); height = Math.Max(1, height);
        if (width == _penW && height == _penH) return;
        EndPenTarget();
        var fresh = Raylib.LoadRenderTexture(width, height);
        Raylib.SetTextureFilter(fresh.Texture, TextureFilter.Bilinear);
        Raylib.BeginTextureMode(fresh);
        Raylib.ClearBackground(new Color(0, 0, 0, 0));
        if (_penW > 0)
        {
            Raylib.BeginBlendMode(BlendMode.AlphaPremultiply);
            Raylib.DrawTexturePro(_pen.Texture, new Rectangle(0, 0, _penW, -_penH), new Rectangle(0, 0, width, height), Vector2.Zero, 0, Color.White);
            Raylib.EndBlendMode();
        }
        Raylib.EndTextureMode();
        if (_penW > 0) Raylib.UnloadRenderTexture(_pen);
        _pen = fresh; _penW = width; _penH = height;
        _penQ = width / 480f;
    }

    void BeginPenTarget()
    {
        if (_penActive) return;
        Raylib.BeginTextureMode(_pen);
        Raylib.BeginBlendMode(BlendMode.AlphaPremultiply);
        _penActive = true;
        _mode = Mode.None;
    }

    void EndPenTarget()
    {
        if (!_penActive) return;
        SetMode(Mode.None);
        Raylib.EndBlendMode();
        Raylib.EndTextureMode();
        _penActive = false;
    }

    void SetMode(Mode m)
    {
        if (m == _mode) return;
        if (_mode != Mode.None) Raylib.EndShaderMode();
        if (m == Mode.Line) Raylib.BeginShaderMode(_lineShader);
        else if (m == Mode.Sprite) Raylib.BeginShaderMode(_spriteShader);
        _mode = m;
    }

    // ---------------- pen ----------------

    public void ClearPen()
    {
        _engine.RequestRedraw();
        BeginPenTarget();
        Rlgl.DrawRenderBatchActive();
        Raylib.ClearBackground(new Color(0, 0, 0, 0));
    }

    /// <summary>One pen line with round caps, anti-aliased like Scratch's pen shader.</summary>
    public void PenLine(SpriteBase s, double x0, double y0, double x1, double y1)
    {
        _engine.RequestRedraw();
        BeginPenTarget();
        SetMode(Mode.Line);

        double diameter = s.PenSize;
        double off = (diameter == 1 || diameter == 3) ? 0.5 : 0;
        float q = _penQ;
        float ax = (float)((x0 + off + 240) * q), ay = (float)((180 - (y0 + off)) * q);
        float bx = (float)((x1 + off + 240) * q), by = (float)((180 - (y1 + off)) * q);
        float dx = bx - ax, dy = by - ay;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        float thick = (float)(diameter * q);
        float r = thick * 0.5f + 1.4142135623730951f;
        float ux, uy;
        if (len < 1e-3f) { ux = 1; uy = 0; } else { ux = dx / len; uy = dy / len; }
        float nx = -uy, ny = ux;

        byte cr = (byte)Math.Round(s.PenR * 255), cg = (byte)Math.Round(s.PenG * 255), cb = (byte)Math.Round(s.PenB * 255);
        float alpha = s.PenA;

        Rlgl.CheckRenderBatchLimit(4);
        Rlgl.Begin(DrawMode.Quads);
        Rlgl.Color4ub(cr, cg, cb, 255);
        Rlgl.Normal3f(thick, len, alpha);
        Vertex(ax - ux * r - nx * r, ay - uy * r - ny * r, -r, -r);
        Vertex(ax - ux * r + nx * r, ay - uy * r + ny * r, -r, r);
        Vertex(bx + ux * r + nx * r, by + uy * r + ny * r, len + r, r);
        Vertex(bx + ux * r - nx * r, by + uy * r - ny * r, len + r, -r);
        Rlgl.End();
    }

    static void Vertex(float x, float y, float along, float across)
    {
        Rlgl.TexCoord2f(along, across);
        Rlgl.Vertex2f(x, y);
    }

    public void Stamp(SpriteBase s)
    {
        _engine.RequestRedraw();
        BeginPenTarget();
        SetMode(Mode.Sprite);
        DrawTarget(s, s.Costume, s.X, s.Y, s.Direction, s.Size, s.RotationStyle, 0, 0, _penQ);
    }

    // ---------------- sprites ----------------

    static int EffectMask(double[] fx)
    {
        int mask = 0;
        for (int i = 0; i < fx.Length; i++) if (fx[i] != 0 && !double.IsNaN(fx[i])) mask |= 1 << i;
        return mask;
    }

    void SetUniforms(Target t, Costume c)
    {
        var fx = t.Effects;
        int mask = EffectMask(fx);
        float color = (float)(fx[(int)Effect.Color] / 200 % 1);
        float bright = (float)(Math.Max(-100, Math.Min(fx[(int)Effect.Brightness], 100)) / 100);
        float ghost = (float)(1 - Math.Max(0, Math.Min(fx[(int)Effect.Ghost], 100)) / 100);
        float pix = (float)(Math.Abs(fx[(int)Effect.Pixelate]) / 10);
        var skin = new Vector2((float)c.Width, (float)c.Height);
        // Only values of enabled effects matter; skipping the rest keeps text glyph stamps in one batch.
        bool same = mask == _uEffects
            && ((mask & (1 << (int)Effect.Color)) == 0 || color.Equals(_uColor))
            && ((mask & (1 << (int)Effect.Brightness)) == 0 || bright == _uBrightness)
            && ((mask & (1 << (int)Effect.Ghost)) == 0 || ghost == _uGhost)
            && ((mask & (1 << (int)Effect.Pixelate)) == 0 || (pix == _uPixelate && skin == _uSkin));
        if (same) return;
        Rlgl.DrawRenderBatchActive();
        _uEffects = mask; _uColor = color; _uBrightness = bright; _uGhost = ghost; _uPixelate = pix; _uSkin = skin;
        Raylib.SetShaderValue(_spriteShader, _locEffects, mask, ShaderUniformDataType.Int);
        Raylib.SetShaderValue(_spriteShader, _locColor, color, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_spriteShader, _locBrightness, bright, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_spriteShader, _locGhost, ghost, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_spriteShader, _locPixelate, pix, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_spriteShader, _locSkinSize, skin, ShaderUniformDataType.Vec2);
    }

    /// <summary>Draws a costume with Scratch's transform (position, direction, size, rotation center).</summary>
    void DrawTarget(Target t, Costume c, double x, double y, double direction, double size, RotationStyle style,
                    float offX, float offY, float q)
    {
        SetUniforms(t, c);
        double angle = 0, flip = 1;
        if (style == RotationStyle.AllAround) angle = (direction - 90) * Math.PI / 180;
        else if (style == RotationStyle.LeftRight && direction < 0) flip = -1;
        double k = size / 100 * q;
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        double px = offX + (x + 240) * q, py = offY + (180 - y) * q;

        double l = -c.CenterX, r = c.Width - c.CenterX, top = -c.CenterY, bot = c.Height - c.CenterY;

        Rlgl.SetTexture(c.Texture.Id);
        Rlgl.CheckRenderBatchLimit(4);
        Rlgl.Begin(DrawMode.Quads);
        Rlgl.Color4ub(255, 255, 255, 255);
        Rlgl.Normal3f(0, 0, 1);
        Corner(l, top, 0, 0); Corner(l, bot, 0, 1); Corner(r, bot, 1, 1); Corner(r, top, 1, 0);
        Rlgl.End();
        Rlgl.SetTexture(0);

        void Corner(double lx, double ly, float u, float v)
        {
            double sx = lx * k * flip, sy = ly * k;
            Rlgl.TexCoord2f(u, v);
            Rlgl.Vertex2f((float)(px + sx * cos - sy * sin), (float)(py + sx * sin + sy * cos));
        }
    }

    /// <summary>"touching mouse-pointer?": is the point inside a non-transparent pixel of the sprite?</summary>
    public bool TouchingPoint(SpriteBase s, double x, double y)
    {
        var c = s.Costume;
        double k = s.Size / 100;
        if (k == 0) return false;
        double angle = 0, flip = 1;
        if (s.RotationStyle == RotationStyle.AllAround) angle = (s.Direction - 90) * Math.PI / 180;
        else if (s.RotationStyle == RotationStyle.LeftRight && s.Direction < 0) flip = -1;
        // Stage offset in y-down coordinates, then undo rotation and scale.
        double dx = x - s.X, dy = -(y - s.Y);
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        double lx = (dx * cos + dy * sin) / k * flip;
        double ly = (-dx * sin + dy * cos) / k;
        return c.IsOpaqueAt(lx + c.CenterX, ly + c.CenterY);
    }

    // ---------------- frame ----------------

    public void Present(Rectangle stageRect)
    {
        EndPenTarget();
        ResizePen((int)stageRect.Width, (int)stageRect.Height);

        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.Black);
        Raylib.DrawRectangleRec(stageRect, Color.White);
        float q = stageRect.Width / 480f;

        Raylib.BeginBlendMode(BlendMode.AlphaPremultiply);
        Raylib.BeginShaderMode(_spriteShader);
        var stage = _engine.Stage;
        DrawTarget(stage, stage.Costume, 0, 0, 90, 100, RotationStyle.AllAround, stageRect.X, stageRect.Y, q);
        Raylib.EndShaderMode();

        Raylib.DrawTexturePro(_pen.Texture, new Rectangle(0, 0, _penW, -_penH), stageRect, Vector2.Zero, 0, Color.White);

        Raylib.BeginShaderMode(_spriteShader);
        foreach (var s in _engine.Sprites)
            if (s.Visible) DrawTarget(s, s.Costume, s.X, s.Y, s.Direction, s.Size, s.RotationStyle, stageRect.X, stageRect.Y, q);
        Raylib.EndShaderMode();
        Raylib.EndBlendMode();

        _engine.DrawOverlay(stageRect);
        _engine.Monitors.Draw(stageRect);
        _engine.Automation?.OnPresent();
        Raylib.EndDrawing();
    }
}
