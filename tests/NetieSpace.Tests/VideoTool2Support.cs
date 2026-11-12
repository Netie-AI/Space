using System.Globalization;

namespace NetieSpace.Tests;

/// <summary>A decoded RGB frame (rgb24) with region helpers. Decoded by a separate ffmpeg call from the code under test.</summary>
internal sealed class Frame
{
    public readonly int W, H;
    public readonly byte[] Rgb;
    public Frame(int w, int h, byte[] rgb) { W = w; H = h; Rgb = rgb; }

    public (int R, int G, int B) At(int x, int y)
    {
        var i = (y * W + x) * 3;
        return (Rgb[i], Rgb[i + 1], Rgb[i + 2]);
    }

    public double Luma(int x, int y)
    {
        var (r, g, b) = At(x, y);
        return 0.299 * r + 0.587 * g + 0.114 * b;
    }

    /// <summary>Mean absolute difference of all channels over a rectangle.</summary>
    public double Diff(Frame other, int x, int y, int w, int h)
    {
        Assert.Equal(W, other.W);
        Assert.Equal(H, other.H);
        double sum = 0;
        for (var yy = y; yy < y + h; yy++)
            for (var xx = x; xx < x + w; xx++)
            {
                var (r1, g1, b1) = At(xx, yy);
                var (r2, g2, b2) = other.At(xx, yy);
                sum += Math.Abs(r1 - r2) + Math.Abs(g1 - g2) + Math.Abs(b1 - b2);
            }
        return sum / (w * h * 3.0);
    }

    public (double R, double G, double B) Mean(int x, int y, int w, int h)
    {
        double r = 0, g = 0, b = 0;
        for (var yy = y; yy < y + h; yy++)
            for (var xx = x; xx < x + w; xx++)
            {
                var p = At(xx, yy);
                r += p.R; g += p.G; b += p.B;
            }
        var n = (double)w * h;
        return (r / n, g / n, b / n);
    }

    public double MeanLuma(int x, int y, int w, int h)
    {
        var (r, g, b) = Mean(x, y, w, h);
        return 0.299 * r + 0.587 * g + 0.114 * b;
    }

    public int Count(int x, int y, int w, int h, Func<int, int, int, bool> pred)
    {
        var n = 0;
        for (var yy = y; yy < y + h; yy++)
            for (var xx = x; xx < x + w; xx++)
            {
                var p = At(xx, yy);
                if (pred(p.R, p.G, p.B)) n++;
            }
        return n;
    }

    /// <summary>Mean absolute horizontal neighbour difference of luma: a plain sharpness measure.</summary>
    public double Sharpness()
    {
        double sum = 0;
        var n = 0;
        for (var y = 0; y < H; y++)
            for (var x = 0; x + 1 < W; x++) { sum += Math.Abs(Luma(x + 1, y) - Luma(x, y)); n++; }
        return sum / n;
    }
}

internal static class VideoTool2Support
{
    /// <summary>
    /// yuv420p cannot hold an odd width or height: FFmpeg 6 quietly rounded, newer builds refuse.
    /// An odd-sized fixture is therefore written as yuv444p, which is what a real odd-sized clip is.
    /// </summary>
    private static string PixFmtFor(string sizeOrGraph)
    {
        var m = System.Text.RegularExpressions.Regex.Match(sizeOrGraph, @"\b(\d{2,5})x(\d{2,5})\b"); // not the 0x of a hex colour
        var odd = m.Success && (int.Parse(m.Groups[1].Value) % 2 == 1 || int.Parse(m.Groups[2].Value) % 2 == 1);
        return odd ? "yuv444p" : "yuv420p";
    }

    /// <summary>A flat colour clip (so any drawn pixel stands out) with an optional sine soundtrack.</summary>
    public static string MakeFlat(string dir, string name, double seconds = 3, string color = "gray",
        string size = "320x180", int rate = 15, bool audio = true)
    {
        IoDirectory.CreateDirectory(dir);
        var path = IoPath.Combine(dir, name);
        var args = new List<string> { "-y", "-v", "error", "-f", "lavfi", "-i", $"color=c={color}:s={size}:r={rate}" };
        if (audio) args.AddRange(new[] { "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100" });
        args.AddRange(new[] { "-t", seconds.ToString(CultureInfo.InvariantCulture), "-c:v", "libx264", "-crf", "10", "-pix_fmt", PixFmtFor(size) });
        if (audio) args.AddRange(new[] { "-c:a", "aac" });
        args.Add(path);
        Must("ffmpeg", args.ToArray());
        return path;
    }

    /// <summary>A flat clip made with an arbitrary lavfi video filter graph (for example a drawbox on a flat colour).</summary>
    public static string MakeLavfi(string dir, string name, string lavfi, double seconds = 3, int rate = 15)
    {
        IoDirectory.CreateDirectory(dir);
        var path = IoPath.Combine(dir, name);
        Must("ffmpeg", "-y", "-v", "error", "-f", "lavfi", "-i", lavfi, "-t", seconds.ToString(CultureInfo.InvariantCulture),
            "-r", rate.ToString(CultureInfo.InvariantCulture), "-c:v", "libx264", "-crf", "10", "-pix_fmt", PixFmtFor(lavfi), path);
        return path;
    }

    /// <summary>A still picture (png, jpg, bmp, ...) from a lavfi source; the extension picks the codec.</summary>
    public static string MakeImage(string dir, string name, string lavfi)
    {
        IoDirectory.CreateDirectory(dir);
        var path = IoPath.Combine(dir, name);
        Must("ffmpeg", "-y", "-v", "error", "-f", "lavfi", "-i", lavfi, "-frames:v", "1", path);
        return path;
    }

    public static void Must(string exe, params string[] args)
    {
        var (code, _, err) = VideoToolTestSupport.Exec(exe, args);
        if (code != 0) throw new InvalidOperationException(exe + " failed (" + code + "): " + err);
    }

    /// <summary>Frame n (0-based) decoded to rgb24 at the file's own size.</summary>
    public static Frame FrameAt(string path, int n)
    {
        var info = VideoToolTestSupport.Probe(path);
        var (code, bytes, err) = VideoToolTestSupport.Exec("ffmpeg", "-v", "error", "-i", path,
            "-vf", $"select=eq(n\\,{n}),format=rgb24", "-frames:v", "1", "-f", "rawvideo", "-");
        if (code != 0 || bytes.Length != info.Width * info.Height * 3)
            throw new InvalidOperationException("could not decode frame " + n + " of " + path + " (" + bytes.Length + " bytes): " + err);
        return new Frame(info.Width, info.Height, bytes);
    }

    public static string Out(string dir) => IoPath.Combine(dir, "out");

    /// <summary>Everything in a folder, to prove nothing stray was written.</summary>
    public static string[] Files(string dir) => IoDirectory.Exists(dir) ? IoDirectory.GetFiles(dir) : Array.Empty<string>();

    public static void WriteSrt(string path, params (double From, double To, string Text)[] cues)
    {
        var sb = new System.Text.StringBuilder();
        var i = 1;
        foreach (var (from, to, text) in cues)
        {
            sb.Append(i++).Append('\n').Append(Ts(from)).Append(" --> ").Append(Ts(to)).Append('\n').Append(text).Append("\n\n");
        }
        IoDirectory.CreateDirectory(IoPath.GetDirectoryName(path)!);
        IoFile.WriteAllText(path, sb.ToString());
    }

    private static string Ts(double s)
    {
        var t = TimeSpan.FromSeconds(s);
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}";
    }
}
