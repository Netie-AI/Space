using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using static NetieSpace.Apps.Video.VideoFfmpeg;

namespace NetieSpace.Apps.Video;

/// <summary>
/// Third wave of Netie Video runners: meme captions, stop motion and screen recording. Same shape as
/// <see cref="VideoToolProvider2"/>; the runners live in the partial <see cref="VideoToolProvider"/> to share its helpers.
/// </summary>
public sealed class VideoToolProvider3 : IToolProvider
{
    public ToolApp App => ToolApp.Video;

    public IEnumerable<(string ToolId, ToolRunnerDelegate Run)> Runners()
    {
        yield return ("vid-meme", VideoToolProvider.MemeAsync);
        yield return ("vid-stop-motion", VideoToolProvider.StopMotionAsync);
        yield return ("vid-screen-record", VideoToolProvider.ScreenRecordAsync);
    }
}

public sealed partial class VideoToolProvider
{
    // ------------------------------------------------------------------ vid-meme

    private const int MemeMaxChars = 200;
    private const int MemeMaxLines = 3;

    /// <summary>Pessimistic advance of one bold capital in em (DejaVu Sans Bold: W and M reach 1.0, most capitals 0.85, a space 0.35).</summary>
    private static double MemeCharEm(char c) => c switch { 'W' or 'M' => 1.05, ' ' => 0.4, _ => 0.9 };

    private static double MemeEmWidth(string s) => s.Sum(MemeCharEm);

    private static string? MemeFont()
    {
        var list = new List<string>();
        try
        {
            var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (!string.IsNullOrEmpty(fonts))
                foreach (var n in new[] { "impact.ttf", "arialbd.ttf" }) list.Add(IoPath.Combine(fonts, n));
        }
        catch { /* ignore */ }
        list.Add(@"C:\Windows\Fonts\impact.ttf");
        list.Add(@"C:\Windows\Fonts\arialbd.ttf");
        list.Add("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf");
        list.Add("/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf");
        return list.FirstOrDefault(IoFile.Exists) ?? W2FindFont();
    }

    /// <summary>The caption option, upper-cased with plain line breaks; null when the option is absent. Blank or oversized is refused.</summary>
    private static string? MemeCaption(ToolRequest r, string key)
    {
        if (!r.Options.TryGetValue(key, out var raw) || raw is null) return null;
        if (string.IsNullOrWhiteSpace(raw)) throw new ArgumentException("option '" + key + "' is empty");
        if (raw.Length > MemeMaxChars) throw new ArgumentException("option '" + key + "' is longer than " + MemeMaxChars + " characters");
        if (raw.Contains('\0')) throw new ArgumentException("option '" + key + "' contains a NUL character");
        var sb = new StringBuilder();
        foreach (var c in raw.Replace("\r\n", "\n").Replace('\r', '\n'))
            sb.Append(c == '\n' || !char.IsControl(c) ? c : ' ');
        return sb.ToString().Trim().ToUpperInvariant();
    }

    /// <summary>Greedy word wrap into lines of at most <paramref name="maxEm"/> em; null when a word does not fit one line.</summary>
    internal static List<string>? MemeWrap(string text, double maxEm)
    {
        var lines = new List<string>();
        foreach (var para in text.Split('\n'))
        {
            var cur = new StringBuilder();
            foreach (var word in para.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (MemeEmWidth(word) > maxEm) return null;
                if (cur.Length > 0 && MemeEmWidth(cur.ToString() + " " + word) > maxEm) { lines.Add(cur.ToString()); cur.Clear(); }
                if (cur.Length > 0) cur.Append(' ');
                cur.Append(word);
            }
            if (cur.Length > 0) lines.Add(cur.ToString());
        }
        return lines;
    }

    /// <summary>Largest font size (shrinking from the base) at which every caption wraps into at most three lines of the frame width.</summary>
    internal static int MemeFit(IEnumerable<string> captions, int width, int height, out Dictionary<string, List<string>> wrapped)
    {
        var list = captions.ToList();
        var floor = Math.Max(8, (int)Math.Round(height * 0.04));
        for (var fs = Math.Max(8, (int)Math.Round(height * 0.09)); fs >= floor; fs = fs > floor && fs * 9 / 10 < floor ? floor : fs * 9 / 10)
        {
            var maxEm = width * 0.88 / fs;
            wrapped = new Dictionary<string, List<string>>();
            var ok = true;
            foreach (var c in list)
            {
                var w = MemeWrap(c, maxEm);
                if (w is null || w.Count > MemeMaxLines) { ok = false; break; }
                wrapped[c] = w;
            }
            if (ok) return fs;
            if (fs == floor) break;
        }
        wrapped = new Dictionary<string, List<string>>();
        return 0;
    }

    internal static async Task<IReadOnlyList<string>> MemeAsync(ToolRequest r, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var top = MemeCaption(r, "top");
        var bottom = MemeCaption(r, "bottom");
        if (top is null && bottom is null) throw new ArgumentException("option 'top' or option 'bottom' is required");
        if (r.Files.Count != 1) throw new ArgumentException("vid-meme needs one video or picture");
        var input = Full(r.Files[0]);
        var font = MemeFont() ?? throw new InvalidOperationException("no system font was found to draw text with (looked in the Windows Fonts folder and the usual Linux font folders)");
        var still = W2Is(input, W2ImageExts) && !W2Is(input, new[] { ".gif" });

        var info = still ? Probe(input) : RequireVideo(input);
        if (!info.HasVideo || info.Width <= 0 || info.Height <= 0) throw new InvalidOperationException(input + " could not be read as a video or picture");
        // Captions are sized on the displayed frame: rotation is applied by FFmpeg before any filter, pixel shape here.
        var squared = Math.Abs(info.Sar - 1.0) > 0.001;
        var w = squared && info.Sar > 1 ? (int)Math.Round(info.Width * info.Sar) : info.Width;
        var h = squared && info.Sar < 1 ? (int)Math.Round(info.Height / info.Sar) : info.Height;

        var captions = new[] { top, bottom }.Where(c => c is not null).Select(c => c!).Distinct().ToList();
        var fs = MemeFit(captions, w, h, out var wrapped);
        if (fs == 0)
            throw new ArgumentException("the caption does not fit: it needs more than " + MemeMaxLines + " lines or has a word wider than the frame");
        var border = Math.Max(2, fs / 10);

        var temps = new List<string>();
        var outPath = "";
        try
        {
            var filters = new List<string>();
            if (squared) filters.Add(SquarePixels);
            foreach (var (text, top1) in new[] { (top, true), (bottom, false) })
            {
                if (text is null) continue;
                var file = W2NewTemp("meme", ".txt");
                temps.Add(file);
                IoFile.WriteAllText(file, string.Join("\n", wrapped[text]), new UTF8Encoding(false));
                filters.Add("drawtext=fontfile=" + W2FilterPath(font) + ":textfile=" + W2FilterPath(file)
                    + ":expansion=none:fontsize=" + fs.ToString(CultureInfo.InvariantCulture)
                    + ":fontcolor=white:bordercolor=black:borderw=" + border.ToString(CultureInfo.InvariantCulture)
                    + ":line_spacing=" + (fs / 10).ToString(CultureInfo.InvariantCulture)
                    + ":text_align=center:x=(w-text_w)/2:y=" + (top1 ? "h*0.03" : "h-text_h-h*0.03"));
            }

            var a = new List<string> { "-i", input };
            if (still)
            {
                a.AddRange(new[] { "-vf", string.Join(",", filters), "-frames:v", "1", "-update", "1" });
                outPath = ReserveOutput(r.OutDir, input, "meme", ".png");
            }
            else
            {
                a.AddRange(Maps);
                a.AddRange(Vf(filters.ToArray()));
                a.AddRange(Encode(IsWebM(input)));
                outPath = ReserveOutput(r.OutDir, input, "meme", OutExt(input));
            }
            a.Add(outPath);
            await RunAsync(a, still ? 0 : info.Duration, progress, IoPath.GetFileName(input), new[] { outPath }, ct).ConfigureAwait(false);
            return new[] { outPath };
        }
        catch
        {
            if (outPath.Length > 0) TryDelete(outPath);
            throw;
        }
        finally
        {
            foreach (var t in temps) TryDelete(t);
        }
    }

    // ------------------------------------------------------------------ vid-stop-motion

    internal static Task<IReadOnlyList<string>> StopMotionAsync(ToolRequest r, IProgress<ToolProgress>? p, CancellationToken ct)
    {
        var keep = IntOpt(r, "fps", 1, 15, 8);
        return OneToOne(r, p, ct, "stop-motion", OutExt, (input, info) =>
        {
            var src = info.Fps >= 1 && info.Fps <= 240 ? info.Fps : 25;
            if (Math.Abs(src - Math.Round(src)) < 0.01) src = Math.Round(src);
            if (keep >= Math.Floor(src))
                throw new ArgumentException("option 'fps': " + keep + " is not below the frame rate of the video (" + Num(src) + ")");
            // Sample the clip at the chosen rate, then repeat each kept frame back up to the original rate: the length stays.
            var a = new List<string> { "-i", input };
            a.AddRange(Maps);
            // tpad holds the last kept frame to the end, so a length that is not a whole number of kept intervals is still reached.
            var hold = (int)Math.Ceiling(src / keep) + 1;
            a.AddRange(Vf("fps=" + keep.ToString(CultureInfo.InvariantCulture) + ":eof_action=pass", "fps=" + Num(src),
                "tpad=stop_mode=clone:stop=" + hold.ToString(CultureInfo.InvariantCulture)));
            a.AddRange(Encode(IsWebM(input)));
            a.AddRange(new[] { "-t", Num(info.Duration) });
            return new Plan(a, info.Duration);
        });
    }

    // ------------------------------------------------------------------ vid-screen-record

    /// <summary>The ffmpeg arguments (without the output path) for a gdigrab recording. Pure, so it is testable off Windows.</summary>
    internal static List<string> ScreenRecordArgs(int seconds, int fps, (int X, int Y, int W, int H)? region, bool cursor)
    {
        var a = new List<string> { "-f", "gdigrab", "-framerate", fps.ToString(CultureInfo.InvariantCulture), "-draw_mouse", cursor ? "1" : "0" };
        if (region is { } g)
            a.AddRange(new[] { "-offset_x", g.X.ToString(CultureInfo.InvariantCulture), "-offset_y", g.Y.ToString(CultureInfo.InvariantCulture),
                               "-video_size", g.W.ToString(CultureInfo.InvariantCulture) + "x" + g.H.ToString(CultureInfo.InvariantCulture) });
        a.AddRange(new[] { "-i", "desktop", "-t", seconds.ToString(CultureInfo.InvariantCulture) });
        a.AddRange(Vf("fps=" + fps.ToString(CultureInfo.InvariantCulture)));
        a.AddRange(new[] { "-an", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "23", "-pix_fmt", "yuv420p", "-movflags", "+faststart" });
        return a;
    }

    internal static (int X, int Y, int W, int H)? ParseRegion(string? text)
    {
        if (text is null) return null;
        var parts = text.Split(',');
        if (parts.Length != 4 || parts.Any(s => !int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            throw new ArgumentException("option 'region': '" + text + "' is not x,y,width,height (four whole numbers)");
        var v = parts.Select(s => int.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
        if (v[0] < 0 || v[1] < 0 || v[0] > 32767 || v[1] > 32767)
            throw new ArgumentException("option 'region': x and y must be 0..32767");
        if (v[2] < 2 || v[3] < 2 || v[2] > 16384 || v[3] > 16384)
            throw new ArgumentException("option 'region': width and height must be 2..16384");
        return (v[0], v[1], v[2], v[3]);
    }

    internal static async Task<IReadOnlyList<string>> ScreenRecordAsync(ToolRequest r, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        if (r.Files.Count > 0) throw new ArgumentException("vid-screen-record needs no input file");
        var seconds = IntOpt(r, "seconds", 1, 600, 5);
        var fps = IntOpt(r, "fps", 5, 60, 30);
        var region = ParseRegion(Opt(r, "region"));
        var cursorText = Lower(Opt(r, "cursor"), "true");
        if (cursorText is not ("true" or "false")) throw new ArgumentException("option 'cursor': '" + cursorText + "' is not true or false");
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("screen recording is available on Windows only (it uses the Windows desktop capture of FFmpeg)");

        var outPath = ReserveOutput(r.OutDir, "screen", "recording", ".mp4");
        var args = ScreenRecordArgs(seconds, fps, region, cursorText == "true");
        args.Add(outPath);
        await RunAsync(args, seconds, progress, "recording", new[] { outPath }, ct).ConfigureAwait(false);
        return new[] { outPath };
    }
}
