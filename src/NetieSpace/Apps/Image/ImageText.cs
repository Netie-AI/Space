using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace NetieSpace.Apps.Image;

/// <summary>
/// Text drawing for the image tools. SixLabors.ImageSharp.Drawing is NOT in the dependency graph, so this
/// uses SixLabors.Fonts 1.0.0-beta17 (Apache-2.0, arrives through PdfSharpCore) only to read glyph
/// outlines from a font already installed on the machine, and fills them with a small scanline
/// rasteriser of its own. No font file is bundled.
/// </summary>
public static class ImageText
{
    public const int MaxTextLength = 500;
    private const int SubScan = 4;

    private static readonly string[] Preferred =
    {
        "Segoe UI", "Arial", "Calibri", "Helvetica", "Liberation Sans", "DejaVu Sans", "Noto Sans",
        "FreeSans", "Inter", "Roboto", "Open Sans", "Ubuntu", "Verdana", "Tahoma",
    };

    /// <summary>Validates caption or watermark text at the trust boundary and returns it trimmed.</summary>
    public static string CleanText(string optionKey, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"option '{optionKey}' is empty; give some text");
        var t = value.Trim();
        if (t.Length > MaxTextLength)
            throw new ArgumentException($"option '{optionKey}' is longer than {MaxTextLength} characters");
        foreach (var c in t)
            if (char.IsControl(c) && c != '\n' && c != '\r')
                throw new ArgumentException($"option '{optionKey}' contains a control character");
        return t.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    /// <summary>Finds a system font family. Throws a clear message when the machine has none.</summary>
    public static FontFamily ResolveFamily()
    {
        var all = SystemFonts.Collection.Families.ToList();
        foreach (var name in Preferred)
        {
            var hit = all.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (hit.Name != null) return hit;
        }
        foreach (var f in all.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            if (f.GetAvailableStyles().Contains(FontStyle.Regular)) return f;
        throw new InvalidOperationException(
            "no system font could be found to draw text with; install a font (for example Arial or DejaVu Sans) and try again");
    }

    /// <summary>Glyph outlines of a laid-out text block, as closed polygons in pixel units.</summary>
    public sealed class Ink
    {
        public List<Vector2[]> Polygons { get; } = new();
        public float MinX, MinY, MaxX, MaxY;
        public float Width => MaxX - MinX;
        public float Height => MaxY - MinY;
        public bool IsEmpty => Polygons.Count == 0;
    }

    public static Ink Layout(string text, FontFamily family, float sizePx, bool bold, float wrapLengthPx)
    {
        var font = family.CreateFont(sizePx, bold && family.GetAvailableStyles().Contains(FontStyle.Bold)
            ? FontStyle.Bold : FontStyle.Regular);
        var opts = new TextOptions(font)
        {
            Dpi = 72,
            WrappingLength = wrapLengthPx > 0 ? wrapLengthPx : -1,
            TextAlignment = TextAlignment.Center,
        };
        var rec = new Collector();
        TextRenderer.RenderTextTo(rec, text, opts);
        var ink = rec.Result;
        if (ink.IsEmpty)
            throw new InvalidOperationException("the chosen font has no outlines for that text; use other characters");
        return ink;
    }

    private sealed class Collector : IGlyphRenderer
    {
        public Ink Result { get; } = new();
        private readonly List<Vector2> _cur = new();
        private Vector2 _pos;

        public void BeginText(FontRectangle bounds) { }
        public void EndText() { Finish(); }
        public bool BeginGlyph(FontRectangle bounds, GlyphRendererParameters parameters) => true;
        public void EndGlyph() { }
        public void BeginFigure() { _cur.Clear(); }
        public void MoveTo(Vector2 point) { _cur.Add(point); _pos = point; }
        public void LineTo(Vector2 point) { _cur.Add(point); _pos = point; }

        public void QuadraticBezierTo(Vector2 secondControlPoint, Vector2 point)
        {
            var p0 = _pos;
            for (var i = 1; i <= 8; i++)
            {
                var t = i / 8f; var u = 1 - t;
                _cur.Add(u * u * p0 + 2 * u * t * secondControlPoint + t * t * point);
            }
            _pos = point;
        }

        public void CubicBezierTo(Vector2 secondControlPoint, Vector2 thirdControlPoint, Vector2 point)
        {
            var p0 = _pos;
            for (var i = 1; i <= 12; i++)
            {
                var t = i / 12f; var u = 1 - t;
                _cur.Add(u * u * u * p0 + 3 * u * u * t * secondControlPoint + 3 * u * t * t * thirdControlPoint + t * t * t * point);
            }
            _pos = point;
        }

        public void EndFigure()
        {
            if (_cur.Count >= 3) Result.Polygons.Add(_cur.ToArray());
            _cur.Clear();
        }

        private void Finish()
        {
            if (Result.Polygons.Count == 0) return;
            var all = Result.Polygons.SelectMany(p => p).ToList();
            Result.MinX = all.Min(p => p.X); Result.MaxX = all.Max(p => p.X);
            Result.MinY = all.Min(p => p.Y); Result.MaxY = all.Max(p => p.Y);
        }
    }

    /// <summary>
    /// Fills the ink on the image with its top-left ink corner at (left, top). When <paramref name="outlinePx"/>
    /// is above zero a coloured halo of that radius is laid under the fill. Opacity is 0..1.
    /// </summary>
    public static void Draw(Image<Rgba32> img, Ink ink, float left, float top, Rgba32 fill, float opacity,
        float outlinePx = 0, Rgba32 outline = default)
    {
        var halo = (int)Math.Ceiling(Math.Max(0, outlinePx));
        var dx = left - ink.MinX;
        var dy = top - ink.MinY;
        var x0 = Math.Max(0, (int)Math.Floor(left) - halo - 1);
        var y0 = Math.Max(0, (int)Math.Floor(top) - halo - 1);
        var x1 = Math.Min(img.Width, (int)Math.Ceiling(left + ink.Width) + halo + 2);
        var y1 = Math.Min(img.Height, (int)Math.Ceiling(top + ink.Height) + halo + 2);
        if (x1 <= x0 || y1 <= y0) return;
        var w = x1 - x0; var h = y1 - y0;
        var cov = Rasterise(ink, dx - x0, dy - y0, w, h);

        if (halo > 0)
        {
            var ring = Dilate(cov, w, h, outlinePx);
            Composite(img, ring, x0, y0, w, h, outline, opacity);
        }
        Composite(img, cov, x0, y0, w, h, fill, opacity);
    }

    private static float[] Rasterise(Ink ink, float ox, float oy, int w, int h)
    {
        var cov = new float[w * h];
        var edges = new List<(float X0, float Y0, float X1, float Y1)>();
        foreach (var poly in ink.Polygons)
            for (var i = 0; i < poly.Length; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Length];
                if (a.Y == b.Y) continue;
                edges.Add((a.X + ox, a.Y + oy, b.X + ox, b.Y + oy));
            }
        var xs = new List<(float X, int Dir)>();
        var weight = 1f / SubScan;
        for (var row = 0; row < h; row++)
            for (var s = 0; s < SubScan; s++)
            {
                var y = row + (s + 0.5f) / SubScan;
                xs.Clear();
                foreach (var (ax, ay, bx, by) in edges)
                {
                    var up = ay < by;
                    var lo = up ? ay : by; var hi = up ? by : ay;
                    if (y < lo || y >= hi) continue;
                    var t = (y - ay) / (by - ay);
                    xs.Add((ax + t * (bx - ax), up ? 1 : -1));
                }
                if (xs.Count < 2) continue;
                xs.Sort((p, q) => p.X.CompareTo(q.X));
                var wind = 0;
                for (var i = 0; i < xs.Count - 1; i++)
                {
                    wind += xs[i].Dir;
                    if (wind == 0) continue;
                    AddSpan(cov, row * w, w, xs[i].X, xs[i + 1].X, weight);
                }
            }
        return cov;
    }

    private static void AddSpan(float[] cov, int rowOffset, int w, float xa, float xb, float weight)
    {
        if (xb <= 0 || xa >= w) return;
        xa = Math.Max(xa, 0); xb = Math.Min(xb, w);
        var ia = (int)Math.Floor(xa); var ib = (int)Math.Floor(xb);
        if (ia == ib) { cov[rowOffset + ia] += (xb - xa) * weight; return; }
        cov[rowOffset + ia] += (ia + 1 - xa) * weight;
        for (var i = ia + 1; i < ib; i++) cov[rowOffset + i] += weight;
        if (ib < w) cov[rowOffset + ib] += (xb - ib) * weight;
    }

    private static float[] Dilate(float[] cov, int w, int h, float radius)
    {
        var r = (int)Math.Ceiling(radius);
        var offsets = new List<(int X, int Y)>();
        for (var oy = -r; oy <= r; oy++)
            for (var ox = -r; ox <= r; ox++)
                if (ox * ox + oy * oy <= radius * radius + 0.25f) offsets.Add((ox, oy));
        var res = new float[cov.Length];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var best = 0f;
                foreach (var (ox, oy) in offsets)
                {
                    var xx = x + ox; var yy = y + oy;
                    if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                    var c = cov[yy * w + xx];
                    if (c > best) { best = c; if (best >= 1f) break; }
                }
                res[y * w + x] = best;
            }
        return res;
    }

    private static void Composite(Image<Rgba32> img, float[] cov, int x0, int y0, int w, int h, Rgba32 color, float opacity)
    {
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var a = Math.Min(1f, cov[y * w + x]) * opacity * (color.A / 255f);
                if (a <= 0f) continue;
                var d = img[x0 + x, y0 + y];
                var da = d.A / 255f;
                var oa = a + da * (1 - a);
                if (oa <= 0f) continue;
                byte Mix(byte s, byte t) => (byte)Math.Clamp((int)Math.Round((s * a + t * da * (1 - a)) / oa), 0, 255);
                img[x0 + x, y0 + y] = new Rgba32(Mix(color.R, d.R), Mix(color.G, d.G), Mix(color.B, d.B),
                    (byte)Math.Clamp((int)Math.Round(oa * 255), 0, 255));
            }
    }
}
