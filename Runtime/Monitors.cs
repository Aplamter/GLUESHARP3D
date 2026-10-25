using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Raylib_cs;

namespace Scratch;

/// <summary>On-stage list monitors ("show list"), drawn in Scratch's style.</summary>
public sealed class Monitors
{
    sealed class ListMonitor
    {
        public ScratchList List = null!;
        public float X, Y, W, H;
        public bool Visible;
    }

    readonly List<ListMonitor> _lists = new();
    Font _font;
    bool _hasFont;

    public void Init(string assetDir)
    {
        string path = Path.Combine(assetDir, "fonts", "NotoSans-Medium.ttf");
        if (File.Exists(path))
        {
            _font = Raylib.LoadFontEx(path, 48, null, 0);
            Raylib.SetTextureFilter(_font.Texture, TextureFilter.Bilinear);
            _hasFont = true;
        }
    }

    public void AddList(ScratchList list, double x, double y, double w, double h, bool visible) =>
        _lists.Add(new ListMonitor { List = list, X = (float)x, Y = (float)y, W = (float)(w > 0 ? w : 100), H = (float)(h > 0 ? h : 200), Visible = visible });

    public void SetVisible(ScratchList list, bool visible)
    {
        foreach (var m in _lists) if (m.List == list) { m.Visible = visible; return; }
        if (visible) _lists.Add(new ListMonitor { List = list, X = 5, Y = 5, W = 100, H = 200, Visible = true });
    }

    public void Text(string s, float x, float y, float size, Color c)
    {
        if (_hasFont) Raylib.DrawTextEx(_font, s, new Vector2(x, y), size, 0, c);
        else Raylib.DrawText(s, (int)x, (int)y, (int)size, c);
    }

    public float Measure(string s, float size) => _hasFont ? Raylib.MeasureTextEx(_font, s, size, 0).X : Raylib.MeasureText(s, (int)size);

    public void Draw(Rectangle stage)
    {
        float k = stage.Width / 480f;
        var border = new Color(195, 204, 217, 255);
        var bg = new Color(229, 240, 255, 255);
        var item = new Color(252, 102, 44, 255);
        foreach (var m in _lists)
        {
            if (!m.Visible) continue;
            var r = new Rectangle(stage.X + m.X * k, stage.Y + m.Y * k, m.W * k, m.H * k);
            Raylib.BeginScissorMode((int)stage.X, (int)stage.Y, (int)stage.Width, (int)stage.Height);
            Raylib.DrawRectangleRounded(r, 0.04f, 6, bg);
            Raylib.DrawRectangleRoundedLinesEx(r, 0.04f, 6, 1 * k, border);
            float fs = 12 * k, rowH = 22 * k, header = 22 * k, footer = 22 * k;
            string title = m.List.Name;
            Raylib.DrawRectangle((int)r.X + 1, (int)(r.Y + header), (int)r.Width - 2, 1, border);
            Text(title, r.X + (r.Width - Measure(title, fs)) / 2, r.Y + (header - fs) / 2, fs, new Color(87, 94, 117, 255));
            var items = m.List.Items;
            float top = r.Y + header + 2 * k, bottom = r.Y + r.Height - footer;
            Raylib.BeginScissorMode((int)r.X, (int)top, (int)r.Width, (int)Math.Max(0, bottom - top));
            for (int i = 0; i < items.Count; i++)
            {
                float y = top + i * rowH;
                if (y > bottom) break;
                string n = (i + 1).ToString();
                Text(n, r.X + 4 * k, y + (rowH - fs) / 2, fs, new Color(87, 94, 117, 255));
                float bx = r.X + (8 + 8 * n.Length) * k;
                var box = new Rectangle(bx, y + 2 * k, r.X + r.Width - 6 * k - bx, rowH - 4 * k);
                Raylib.DrawRectangleRounded(box, 0.25f, 4, item);
                Text(items[i].Str, box.X + 4 * k, y + (rowH - fs) / 2, fs, Color.White);
            }
            Raylib.BeginScissorMode((int)stage.X, (int)stage.Y, (int)stage.Width, (int)stage.Height);
            string len = $"length {items.Count}";
            Raylib.DrawRectangle((int)r.X + 1, (int)bottom, (int)r.Width - 2, 1, border);
            Text(len, r.X + (r.Width - Measure(len, fs)) / 2, bottom + (footer - fs) / 2, fs, new Color(87, 94, 117, 255));
            Raylib.EndScissorMode();
        }
    }
}
