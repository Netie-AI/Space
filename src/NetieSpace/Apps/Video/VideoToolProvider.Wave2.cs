using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using static NetieSpace.Apps.Video.VideoFfmpeg;

namespace NetieSpace.Apps.Video;

/// <summary>
/// Second wave of Netie Video runners. A separate provider type so the first wave stays untouched; the
/// runners themselves live in the partial <see cref="VideoToolProvider"/> below to share its helpers.
/// Every tool here was driven on a real clip and read back (see tests/NetieSpace.Tests/VideoTool2*.cs).
/// </summary>
public sealed class VideoToolProvider2 : IToolProvider
{
    public ToolApp App => ToolApp.Video;

    public IEnumerable<(string ToolId, ToolRunnerDelegate Run)> Runners()
    {
        yield return ("vid-add-text", VideoToolProvider.AddTextAsync);
        yield return ("vid-add-image", VideoToolProvider.AddImageAsync);
        yield return ("vid-subtitles", VideoToolProvider.SubtitlesAsync);
        yield return ("vid-audio-cut-merge", VideoToolProvider.AudioCutMergeAsync);
        yield return ("vid-adjust", VideoToolProvider.AdjustAsync);
        yield return ("vid-filters", VideoToolProvider.FiltersAsync);
        yield return ("vid-slideshow", VideoToolProvider.SlideshowAsync);
        yield return ("vid-stabilize", VideoToolProvider.StabilizeAsync);
        yield return ("vid-remove-logo", VideoToolProvider.RemoveLogoAsync);
        yield return ("vid-gif-editor", VideoToolProvider.GifEditorAsync);
    }
}

public sealed partial class VideoToolProvider
{
    // ------------------------------------------------------------------ wave 2 scaffolding

    private sealed record W2Plan(IReadOnlyList<string> Args, double ExpectedSeconds);

    private static readonly string[] W2ImageExts = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp" };

    private static bool W2Is(string path, string[] exts) =>
        exts.Contains(IoPath.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>One output per input video. The build step may add temp files to the list; they are always deleted.</summary>
    private static async Task<IReadOnlyList<string>> W2OneToOne(
        ToolRequest r, IProgress<ToolProgress>? progress, CancellationToken ct, string tag,
        Func<string, VideoInfo, List<string>, W2Plan> build)
    {
        var outs = new List<string>();
        try
        {
            for (var i = 0; i < r.Files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var input = Full(r.Files[i]);
                var info = RequireVideo(input);
                var temps = new List<string>();
                try
                {
                    var plan = build(input, info, temps);
                    var outPath = ReserveOutput(r.OutDir, input, tag, OutExt(input));
                    outs.Add(outPath);
                    var args = plan.Args.Concat(new[] { outPath }).ToList();
                    await RunAsync(args, plan.ExpectedSeconds, new SlicedProgress(progress, i, r.Files.Count),
                        IoPath.GetFileName(input), new[] { outPath }, ct).ConfigureAwait(false);
                }
                finally
                {
                    foreach (var t in temps) TryDelete(t);
                }
            }
            return outs;
        }
        catch
        {
            foreach (var o in outs) TryDelete(o);
            throw;
        }
    }

    /// <summary>
    /// A new empty file with a plain name in a folder whose path has no characters that matter to the
    /// filter-graph parser (the apostrophe above all: ffmpeg strips it from file names given to filters).
    /// </summary>
    private static string W2NewTemp(string kind, string ext)
    {
        var bad = new[] { '\'', ',', ';', '[', ']', '%', '\n', '\r' };
        var dirs = new List<string> { IoPath.GetTempPath(), AppContext.BaseDirectory };
        foreach (var d in dirs)
        {
            string full;
            try { full = IoPath.GetFullPath(d); } catch { continue; }
            if (full.IndexOfAny(bad) >= 0 || !IoDirectory.Exists(full)) continue;
            var path = IoPath.Combine(full, "_ns_" + kind + "_" + Guid.NewGuid().ToString("N") + ext);
            try
            {
                using (new System.IO.FileStream(path, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write)) { }
                return path;
            }
            catch (Exception) { /* not writable, try the next */ }
        }
        throw new InvalidOperationException("no usable temp folder: the temp path contains a quote or another character ffmpeg filters cannot take");
    }

    /// <summary>A file path as a quoted, escaped filter option value (same technique as FfmpegService).</summary>
    private static string W2FilterPath(string path) =>
        "'" + NetieSpace.Services.FfmpegExportGraph.EscapeFilterPath(path) + "'";

    private static string W2CopyForFilter(string source, string kind, List<string> temps)
    {
        var ext = IoPath.GetExtension(source);
        if (string.IsNullOrWhiteSpace(ext) || ext.Length > 8 || ext.Any(c => !char.IsLetterOrDigit(c) && c != '.')) ext = ".bin";
        var tmp = W2NewTemp(kind, ext);
        temps.Add(tmp);
        IoFile.Copy(source, tmp, overwrite: true);
        return tmp;
    }

    /// <summary>Grid word to (0 left/top, 1 centre, 2 right/bottom) pairs.</summary>
    private static (int H, int V) W2Grid(string text, string option)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "top-left": case "northwest": return (0, 0);
            case "top-center": case "north": return (1, 0);
            case "top-right": case "northeast": return (2, 0);
            case "middle-left": case "west": return (0, 1);
            case "middle-center": case "center": return (1, 1);
            case "middle-right": case "east": return (2, 1);
            case "bottom-left": case "southwest": return (0, 2);
            case "bottom-center": case "south": return (1, 2);
            case "bottom-right": case "southeast": return (2, 2);
        }
        throw new ArgumentException("option '" + option + "': '" + text + "' is not a grid position (for example top-left, middle-center, bottom-right)");
    }

    private static (int H, int V) W2GridOpt(ToolRequest r, string key, string fallback) =>
        W2Grid(Opt(r, key) ?? fallback, key);

    /// <summary>#RRGGBB or #AARRGGBB (AA = opacity) to ffmpeg's 0xRRGGBB[@alpha].</summary>
    private static string W2Color(ToolRequest r, string key, string? fallback)
    {
        var v = Opt(r, key) ?? fallback ?? throw new ArgumentException("option '" + key + "' is required");
        if (!HexColor.IsMatch(v)) throw new ArgumentException("option '" + key + "': '" + v + "' is not #RRGGBB or #AARRGGBB");
        var h = v.Substring(1);
        if (h.Length == 6) return "0x" + h.ToUpperInvariant();
        var alpha = int.Parse(h.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
        return "0x" + h.Substring(2).ToUpperInvariant() + "@" + alpha.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>Optional start/end option pair to a validated window (end is null when open).</summary>
    private static (double Start, double? End) W2Window(ToolRequest r)
    {
        var start = ParseTime(Opt(r, "start") ?? "0", "start");
        var endText = Opt(r, "end");
        double? end = endText is null ? null : ParseTime(endText, "end");
        if (end is double e && e <= start) throw new ArgumentException("option 'end' must be later than option 'start'");
        return (start, end);
    }

    private static string W2Enable(double start, double? end)
    {
        if (start <= 0 && end is null) return "";
        return end is double e
            ? ":enable='between(t," + Num(start) + "," + Num(e) + ")'"
            : ":enable='gte(t," + Num(start) + ")'";
    }

    private static void W2CheckWindow(double start, VideoInfo info)
    {
        if (start >= info.Duration)
            throw new ArgumentException("option 'start' (" + Num(start) + " s) is at or past the end of the video (" + Num(info.Duration) + " s)");
    }

    // ------------------------------------------------------------------ vid-add-text

    private static string? W2FindFont()
    {
        var list = new List<string>();
        try
        {
            var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (!string.IsNullOrEmpty(fonts))
                foreach (var n in new[] { "arial.ttf", "segoeui.ttf", "calibri.ttf", "tahoma.ttf" }) list.Add(IoPath.Combine(fonts, n));
        }
        catch { /* ignore */ }
        list.Add(@"C:\Windows\Fonts\arial.ttf");
        list.Add(@"C:\Windows\Fonts\segoeui.ttf");
        list.Add("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf");
        list.Add("/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf");
        list.Add("/usr/share/fonts/truetype/freefont/FreeSans.ttf");
        list.Add("/usr/share/fonts/TTF/DejaVuSans.ttf");
        list.Add("/Library/Fonts/Arial.ttf");
        return list.FirstOrDefault(IoFile.Exists);
    }

    internal static Task<IReadOnlyList<string>> AddTextAsync(ToolRequest r, IProgress<ToolProgress>? p, CancellationToken ct)
    {
        var text = r.Options.TryGetValue("text", out var raw) ? raw : null;
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("option 'text' is required");
        if (text.Length > 1000) throw new ArgumentException("option 'text' is longer than 1000 characters");
        if (text.Any(c => c == '\0')) throw new ArgumentException("option 'text' contains a NUL character");
        var size = IntOpt(r, "size", 8, 400, 36);
        var (gh, gv) = W2GridOpt(r, "position", "bottom-center");
        var color = W2Color(r, "color", "#FFFFFF");
        var background = Opt(r, "background") is null ? null : W2Color(r, "background", null);
        var (start, end) = W2Window(r);
        var font = W2FindFont() ?? throw new InvalidOperationException("no system font was found to draw text with (looked in the Windows Fonts folder and the usual Linux font folders)");

        var x = gh switch { 0 => "w*0.04", 1 => "(w-text_w)/2", _ => "w-text_w-w*0.04" };
        var y = gv switch { 0 => "h*0.04", 1 => "(h-text_h)/2", _ => "h-text_h-h*0.04" };

        return W2OneToOne(r, p, ct, "text", (input, info, temps) =>
        {
            W2CheckWindow(start, info);
            // The text goes through a file, never through the filter string: nothing in it can be read as filter syntax.
            var textFile = W2NewTemp("text", ".txt");
            temps.Add(textFile);
            IoFile.WriteAllText(textFile, text.Replace("\r\n", "\n"), new UTF8Encoding(false));

            var f = new StringBuilder("drawtext=fontfile=" + W2FilterPath(font) + ":textfile=" + W2FilterPath(textFile)
                + ":expansion=none:fontsize=" + size.ToString(CultureInfo.InvariantCulture)
                + ":fontcolor=" + color + ":x=" + x + ":y=" + y);
            if (background is not null)
                f.Append(":box=1:boxcolor=" + background + ":boxborderw=" + Math.Max(4, size / 4).ToString(CultureInfo.InvariantCulture));
            f.Append(W2Enable(start, end));

            var a = new List<string> { "-i", input };
            a.AddRange(Maps);
            a.AddRange(Vf(f.ToString()));
            a.AddRange(Encode(IsWebM(input)));
            return new W2Plan(a, info.Duration);
        });
    }

    // ------------------------------------------------------------------ vid-add-image

    internal static async Task<IReadOnlyList<string>> AddImageAsync(ToolRequest r, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var scale = IntOpt(r, "scale", 1, 100, 25);
        var opacity = IntOpt(r, "opacity", 0, 100, 100);
        var (gh, gv) = W2GridOpt(r, "position", "top-right");
        var (start, end) = W2Window(r);

        var files = r.Files.Select(Full).ToList();
        if (files.Count != 2) throw new ArgumentException("vid-add-image needs one video and one image");
        var video = files.FirstOrDefault(f => W2Is(f, VideoExts));
        var image = files.FirstOrDefault(f => W2Is(f, W2ImageExts));
        if (video is null || image is null || video == image)
            throw new ArgumentException("vid-add-image needs one video and one image (png, jpg, gif, bmp, tif or webp); got " + string.Join(", ", files.Select(IoPath.GetFileName)));

        var info = RequireVideo(video);
        W2CheckWindow(start, info);
        var imgInfo = Probe(image);
        if (!imgInfo.HasVideo || imgInfo.Width <= 0 || imgInfo.Height <= 0) throw new InvalidOperationException(image + " could not be read as a picture");

        var ovW = Math.Max(2, (int)Math.Round(info.Width * scale / 100.0) / 2 * 2);
        var x = gh switch { 0 => "W*0.03", 1 => "(W-w)/2", _ => "W-w-W*0.03" };
        var y = gv switch { 0 => "H*0.03", 1 => "(H-h)/2", _ => "H-h-H*0.03" };
        var fps = info.Fps >= 1 && info.Fps <= 120 ? info.Fps : 25;

        var ov = "[1:v:0]format=rgba,scale=" + ovW.ToString(CultureInfo.InvariantCulture) + ":-2";
        if (opacity < 100) ov += ",colorchannelmixer=aa=" + Num(opacity / 100.0);
        var graph = ov + "[ov];[0:v:0][ov]overlay=x=" + x + ":y=" + y + ":shortest=1" + W2Enable(start, end) + "[o];[o]" + EvenScale + "[v]";

        var a = new List<string> { "-i", video };
        if (W2Is(image, new[] { ".gif" })) a.AddRange(new[] { "-ignore_loop", "0", "-i", image });
        else a.AddRange(new[] { "-loop", "1", "-framerate", Num(fps), "-i", image });
        a.AddRange(new[] { "-filter_complex", graph, "-map", "[v]", "-map", "0:a:0?" });
        a.AddRange(Encode(IsWebM(video)));
        a.AddRange(new[] { "-t", Num(info.Duration) });

        var outPath = ReserveOutput(r.OutDir, video, "with-image", OutExt(video));
        a.Add(outPath);
        await RunAsync(a, info.Duration, progress, "adding image", new[] { outPath }, ct).ConfigureAwait(false);
        return new[] { outPath };
    }

    // ------------------------------------------------------------------ vid-subtitles

    internal static async Task<IReadOnlyList<string>> SubtitlesAsync(ToolRequest r, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var sizePct = IntOpt(r, "size", 2, 30, 7);
        var (gh, gv) = W2GridOpt(r, "position", "bottom-center");
        var colorText = Opt(r, "color") ?? "#FFFFFF";
        if (!HexColor.IsMatch(colorText)) throw new ArgumentException("option 'color': '" + colorText + "' is not #RRGGBB or #AARRGGBB");
        var h = colorText.Substring(1);
        var ass = h.Length == 6
            ? "00" + h.Substring(4, 2) + h.Substring(2, 2) + h.Substring(0, 2)
            : (255 - int.Parse(h.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString("X2", CultureInfo.InvariantCulture)
              + h.Substring(6, 2) + h.Substring(4, 2) + h.Substring(2, 2);
        // libass reads force_style Alignment as legacy SSA numbers: 1..3 bottom, 5..7 top, 9..11 middle (left, centre, right).
        var alignment = (gv == 2 ? 0 : gv == 0 ? 4 : 8) + gh + 1;

        var files = r.Files.Select(Full).ToList();
        var subs = files.Where(f => W2Is(f, new[] { ".srt", ".vtt" })).ToList();
        var videos = files.Where(f => !subs.Contains(f)).ToList();
        var optSub = Opt(r, "subtitles");
        if (optSub is not null)
        {
            if (subs.Count > 0) throw new ArgumentException("give the subtitle file either as the second input or as option 'subtitles', not both");
            if (!IoFile.Exists(optSub)) throw new ArgumentException("option 'subtitles': file not found: " + optSub);
            subs.Add(Full(optSub));
        }
        if (videos.Count != 1) throw new ArgumentException("vid-subtitles needs exactly one video");
        if (subs.Count != 1) throw new ArgumentException("vid-subtitles needs one .srt or .vtt file (as the second input or option 'subtitles')");
        var video = videos[0];
        var sub = subs[0];
        if (!W2Is(sub, new[] { ".srt", ".vtt" })) throw new ArgumentException("option 'subtitles': only .srt and .vtt files are supported");

        var info = RequireVideo(video);
        string head;
        using (var fs = new System.IO.StreamReader(sub, Encoding.UTF8, true))
        {
            var buf = new char[65536];
            var n = fs.Read(buf, 0, buf.Length);
            head = new string(buf, 0, n);
        }
        if (!head.Contains("-->", StringComparison.Ordinal))
            throw new InvalidOperationException(IoPath.GetFileName(sub) + " has no subtitle cues (no 'start --> end' line was found)");

        var temps = new List<string>();
        var outPath = ReserveOutput(r.OutDir, video, "subtitled", OutExt(video));
        try
        {
            var safeSub = W2CopyForFilter(sub, "sub", temps);
            var fontSize = Math.Max(4, (int)Math.Round(sizePct / 100.0 * 288));
            var filter = "subtitles=filename=" + W2FilterPath(safeSub)
                + ":original_size=" + (info.Width).ToString(CultureInfo.InvariantCulture) + "x" + (info.Height).ToString(CultureInfo.InvariantCulture)
                + ":force_style='Fontsize=" + fontSize.ToString(CultureInfo.InvariantCulture)
                + ",PrimaryColour=&H" + ass + "&,Alignment=" + alignment.ToString(CultureInfo.InvariantCulture) + "'";
            var a = new List<string> { "-i", video };
            a.AddRange(Maps);
            a.AddRange(Vf(filter));
            a.AddRange(Encode(IsWebM(video)));
            a.Add(outPath);
            await RunAsync(a, info.Duration, progress, "burning subtitles", new[] { outPath }, ct).ConfigureAwait(false);
            return new[] { outPath };
        }
        catch
        {
            TryDelete(outPath);
            throw;
        }
        finally
        {
            foreach (var t in temps) TryDelete(t);
        }
    }

    // ------------------------------------------------------------------ vid-audio-cut-merge

    internal static async Task<IReadOnlyList<string>> AudioCutMergeAsync(ToolRequest r, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var format = Pick(r, "format", "mp3", "mp3", "wav", "m4a", "flac", "ogg");
        var start = ParseTime(Opt(r, "start") ?? "0", "start");
        var endText = Opt(r, "end");
        double? end = endText is null ? null : ParseTime(endText, "end");
        var fade = IntOpt(r, "fade", 0, 30, 0);
        if (end is double e0 && e0 <= start) throw new ArgumentException("option 'end' must be later than option 'start'");

        var files = r.Files.Select(Full).ToList();
        foreach (var f in files)
            if (!W2Is(f, AudioExts)) throw new ArgumentException("vid-audio-cut-merge takes audio files only (" + string.Join(" ", AudioExts) + "): " + f);
        var infos = files.Select(f =>
        {
            var i = Probe(f);
            if (!i.HasAudio) throw new InvalidOperationException(f + " has no audio stream");
            if (i.Duration <= 0) throw new InvalidOperationException("could not read the duration of " + f);
            return i;
        }).ToList();
        var total = infos.Sum(i => i.Duration);
        var cutting = start > 0 || end is not null;
        if (files.Count == 1 && !cutting)
            throw new ArgumentException("option 'start' or 'end' is required to cut one file (give two or more files to join them)");
        if (start >= total) throw new ArgumentException("option 'start' (" + Num(start) + " s) is at or past the end of the audio (" + Num(total) + " s)");
        var stop = Math.Min(end ?? total, total);
        var len = stop - start;

        var args = new List<string>();
        foreach (var f in files) args.AddRange(new[] { "-i", f });
        var g = new List<string>();
        for (var i = 0; i < files.Count; i++)
            g.Add($"[{i}:a:0]aresample=44100,aformat=sample_fmts=fltp:channel_layouts=stereo[a{i}]");
        var label = "a0";
        if (files.Count > 1)
        {
            g.Add(string.Concat(Enumerable.Range(0, files.Count).Select(i => $"[a{i}]")) + $"concat=n={files.Count}:v=0:a=1[j]");
            label = "j";
        }
        var tail = new List<string>();
        if (cutting) tail.Add($"atrim=start={Num(start)}:end={Num(stop)}");
        tail.Add("asetpts=PTS-STARTPTS");
        var d = Math.Min(fade, len / 2);
        if (d > 0.01)
        {
            tail.Add($"afade=t=in:st=0:d={Num(d)}");
            tail.Add($"afade=t=out:st={Num(len - d)}:d={Num(d)}");
        }
        g.Add("[" + label + "]" + string.Join(",", tail) + "[a]");
        args.AddRange(new[] { "-filter_complex", string.Join(";", g), "-map", "[a]", "-vn" });
        args.AddRange(AudioCodec(format));

        var outPath = ReserveOutput(r.OutDir, files[0], files.Count > 1 ? "joined" : "cut", "." + format);
        args.Add(outPath);
        await RunAsync(args, len, progress, files.Count > 1 ? "joining audio" : "cutting audio", new[] { outPath }, ct).ConfigureAwait(false);
        return new[] { outPath };
    }

    // ------------------------------------------------------------------ vid-adjust

    internal static Task<IReadOnlyList<string>> AdjustAsync(ToolRequest r, IProgress<ToolProgress>? p, CancellationToken ct)
    {
        var brightness = IntOpt(r, "brightness", -100, 100, 0);
        var contrast = IntOpt(r, "contrast", -100, 100, 0);
        var saturation = IntOpt(r, "saturation", -100, 100, 0);
        var hue = IntOpt(r, "hue", -180, 180, 0);
        if (brightness == 0 && contrast == 0 && saturation == 0 && hue == 0)
            throw new ArgumentException("nothing to adjust: set at least one of the options brightness, contrast, saturation, hue");

        double Gain(int v) => v >= 0 ? 1 + v / 50.0 : 1 + v / 100.0; // -100 -> 0, 0 -> 1, +100 -> 3
        var chain = new List<string>();
        if (brightness != 0 || contrast != 0)
            chain.Add("eq=brightness=" + Num(brightness / 100.0) + ":contrast=" + Num(Gain(contrast)));
        // The hue filter (not eq) does saturation: at -100 it leaves exactly neutral chroma, eq leaves a trace of colour.
        if (hue != 0 || saturation != 0)
            chain.Add("hue=h=" + hue.ToString(CultureInfo.InvariantCulture) + ":s=" + Num(Gain(saturation)));

        return W2OneToOne(r, p, ct, "adjusted", (input, info, _) =>
        {
            var a = new List<string> { "-i", input };
            a.AddRange(Maps);
            a.AddRange(Vf(chain.ToArray()));
            a.AddRange(Encode(IsWebM(input)));
            return new W2Plan(a, info.Duration);
        });
    }

    // ------------------------------------------------------------------ vid-filters

    internal static Task<IReadOnlyList<string>> FiltersAsync(ToolRequest r, IProgress<ToolProgress>? p, CancellationToken ct)
    {
        var name = Pick(r, "filter", "grayscale", "grayscale", "sepia", "blur", "sharpen", "vignette");
        var k = IntOpt(r, "strength", 1, 100, 50) / 100.0;
        string f;
        switch (name)
        {
            case "grayscale":
                f = "hue=s=0";
                break;
            case "sepia":
                f = "colorchannelmixer="
                    + "rr=" + Num((1 - k) + k * .393) + ":rg=" + Num(k * .769) + ":rb=" + Num(k * .189) + ":"
                    + "gr=" + Num(k * .349) + ":gg=" + Num((1 - k) + k * .686) + ":gb=" + Num(k * .168) + ":"
                    + "br=" + Num(k * .272) + ":bg=" + Num(k * .534) + ":bb=" + Num((1 - k) + k * .131);
                break;
            case "blur":
                f = "gblur=sigma=" + Num(0.5 + k * 11.5);
                break;
            case "sharpen":
                f = "unsharp=luma_msize_x=5:luma_msize_y=5:luma_amount=" + Num(k * 3);
                break;
            default:
                f = "vignette=angle=" + Num(0.25 + k * 0.85); // a wider lens angle darkens the corners more
                break;
        }
        return W2OneToOne(r, p, ct, name, (input, info, _) =>
        {
            var a = new List<string> { "-i", input };
            a.AddRange(Maps);
            a.AddRange(Vf(f));
            a.AddRange(Encode(IsWebM(input)));
            return new W2Plan(a, info.Duration);
        });
    }

    // ------------------------------------------------------------------ vid-remove-logo

    internal static Task<IReadOnlyList<string>> RemoveLogoAsync(ToolRequest r, IProgress<ToolProgress>? p, CancellationToken ct)
    {
        var x = IntOpt(r, "x", 0, 16384);
        var y = IntOpt(r, "y", 0, 16384);
        var w = IntOpt(r, "width", 1, 16384);
        var h = IntOpt(r, "height", 1, 16384);
        var (start, end) = W2Window(r);
        return W2OneToOne(r, p, ct, "delogo", (input, info, _) =>
        {
            W2CheckWindow(start, info);
            // delogo needs the rectangle strictly inside the frame (a 1 pixel band all round).
            if (x < 1) throw new ArgumentException("option 'x' must be at least 1 (the logo box has to sit inside the picture)");
            if (y < 1) throw new ArgumentException("option 'y' must be at least 1 (the logo box has to sit inside the picture)");
            if (x + w > info.Width - 1) throw new ArgumentException("option 'width': x + width (" + (x + w) + ") must stay inside the picture width " + info.Width + " by at least 1 pixel");
            if (y + h > info.Height - 1) throw new ArgumentException("option 'height': y + height (" + (y + h) + ") must stay inside the picture height " + info.Height + " by at least 1 pixel");
            var f = $"delogo=x={x}:y={y}:w={w}:h={h}" + W2Enable(start, end);
            var a = new List<string> { "-i", input };
            a.AddRange(Maps);
            a.AddRange(Vf(f));
            a.AddRange(Encode(IsWebM(input)));
            return new W2Plan(a, info.Duration);
        });
    }

    // ------------------------------------------------------------------ vid-slideshow

    internal static async Task<IReadOnlyList<string>> SlideshowAsync(ToolRequest r, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var secs = IntOpt(r, "seconds", 1, 60, 3);
        var width = IntOpt(r, "width", 16, 7680, 1280);
        var height = IntOpt(r, "height", 16, 7680, 720);
        if (width % 2 != 0) throw new ArgumentException("option 'width': " + width + " must be even");
        if (height % 2 != 0) throw new ArgumentException("option 'height': " + height + " must be even");
        var fit = Pick(r, "fit", "pad", "pad", "crop");

        var files = r.Files.Select(Full).ToList();
        var images = new List<string>();
        var music = new List<string>();
        foreach (var f in files)
        {
            if (W2Is(f, new[] { ".gif" })) throw new NotSupportedException("animated GIFs in a slideshow are not built in this version: " + f);
            if (W2Is(f, W2ImageExts)) images.Add(f);
            else if (W2Is(f, AudioExts)) music.Add(f);
            else if (W2Is(f, VideoExts)) throw new NotSupportedException("video clips in a slideshow are not built in this version: " + f);
            else throw new ArgumentException("vid-slideshow takes pictures and at most one music file: " + f);
        }
        if (images.Count == 0) throw new ArgumentException("vid-slideshow needs at least one picture");
        if (images.Count > 300) throw new ArgumentException("vid-slideshow takes at most 300 pictures, got " + images.Count);
        if (music.Count > 1) throw new ArgumentException("vid-slideshow takes at most one music file, got " + music.Count);
        if (music.Count == 1 && !Probe(music[0]).HasAudio) throw new InvalidOperationException(music[0] + " has no audio stream");

        // A looped input that cannot be decoded never ends, so every picture is proven readable first.
        foreach (var img in images)
        {
            VideoInfo pi;
            try { pi = Probe(img); }
            catch (InvalidOperationException ex) { throw new InvalidOperationException(IoPath.GetFileName(img) + " could not be read as a picture: " + ex.Message); }
            if (!pi.HasVideo || pi.Width <= 0 || pi.Height <= 0)
                throw new InvalidOperationException(IoPath.GetFileName(img) + " could not be read as a picture");
        }

        const int fps = 25;
        var total = images.Count * secs;
        var a = new List<string>();
        foreach (var img in images)
            a.AddRange(new[] { "-loop", "1", "-framerate", fps.ToString(CultureInfo.InvariantCulture), "-t", secs.ToString(CultureInfo.InvariantCulture), "-i", img });
        if (music.Count == 1) a.AddRange(new[] { "-i", music[0] });

        var g = new List<string>();
        var concatIn = new StringBuilder();
        for (var i = 0; i < images.Count; i++)
        {
            var fitChain = fit == "pad"
                ? $"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color=black"
                : $"scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height}";
            g.Add($"[{i}:v:0]{fitChain},setsar=1,fps={fps},format=yuv420p[v{i}]");
            concatIn.Append($"[v{i}]");
        }
        g.Add($"{concatIn}concat=n={images.Count}:v=1:a=0[v]");
        if (music.Count == 1)
            g.Add($"[{images.Count}:a:0]aresample=44100,apad,atrim=duration={total}[a]");
        a.AddRange(new[] { "-filter_complex", string.Join(";", g), "-map", "[v]" });
        if (music.Count == 1) a.AddRange(new[] { "-map", "[a]" });
        a.AddRange(Encode(false));
        a.AddRange(new[] { "-t", total.ToString(CultureInfo.InvariantCulture) });

        var outPath = ReserveOutput(r.OutDir, images[0], "slideshow", ".mp4");
        a.Add(outPath);
        await RunAsync(a, total, progress, "building slideshow", new[] { outPath }, ct).ConfigureAwait(false);
        return new[] { outPath };
    }

    // ------------------------------------------------------------------ vid-stabilize

    private static readonly ConcurrentDictionary<string, string> W2FilterLists = new();

    /// <summary>Names of the filters this ffmpeg build has (cached per binary). Empty when it cannot be asked.</summary>
    private static string W2Filters()
    {
        var exe = RequireFfmpeg();
        return W2FilterLists.GetOrAdd(exe, e =>
        {
            var psi = new ProcessStartInfo { FileName = e, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var x in new[] { "-hide_banner", "-filters" }) psi.ArgumentList.Add(x);
            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("could not start ffmpeg");
            var err = proc.StandardError.ReadToEndAsync();
            var o = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            return o;
        });
    }

    /// <summary>Test seam: pretend this ffmpeg build has (true) or lacks (false) a named filter.</summary>
    internal static Func<string, bool>? FilterProbeOverride;

    private static bool W2HasFilter(string name) =>
        FilterProbeOverride?.Invoke(name) ??
        // The flags column differs between FFmpeg versions (three characters in 6.x, other widths
        // later), so match "flags name inputs->outputs" without assuming its shape.
        Regex.IsMatch(W2Filters(), @"^\s*\S+\s+" + Regex.Escape(name) + @"\s+\S+->\S+", RegexOptions.Multiline);

    internal static async Task<IReadOnlyList<string>> StabilizeAsync(ToolRequest r, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var strength = IntOpt(r, "strength", 1, 100, 50);
        var vidstab = W2HasFilter("vidstabdetect") && W2HasFilter("vidstabtransform");
        if (!vidstab && !W2HasFilter("deshake"))
            throw new InvalidOperationException("this FFmpeg build has neither vidstab nor deshake, so stabilizing is not possible here");

        var shakiness = Math.Max(1, (int)Math.Ceiling(strength / 10.0));
        var smoothing = 5 + (int)Math.Round(strength / 100.0 * 25);
        var outs = new List<string>();
        try
        {
            for (var i = 0; i < r.Files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var input = Full(r.Files[i]);
                var info = RequireVideo(input);
                var outPath = ReserveOutput(r.OutDir, input, "stabilized", OutExt(input));
                outs.Add(outPath);
                var temps = new List<string>();
                try
                {
                    if (vidstab)
                    {
                        var trf = W2NewTemp("trf", ".trf");
                        temps.Add(trf);
                        var pass1 = new List<string> { "-i", input, "-vf",
                            "vidstabdetect=shakiness=" + shakiness.ToString(CultureInfo.InvariantCulture) + ":result=" + W2FilterPath(trf),
                            "-an", "-f", "null", "-" };
                        await RunAsync(pass1, info.Duration, new SlicedProgress(progress, i * 2, r.Files.Count * 2),
                            "analysing " + IoPath.GetFileName(input), new[] { outPath }, ct).ConfigureAwait(false);
                        var a = new List<string> { "-i", input };
                        a.AddRange(Maps);
                        a.AddRange(Vf("vidstabtransform=input=" + W2FilterPath(trf) + ":smoothing=" + smoothing.ToString(CultureInfo.InvariantCulture) + ":optzoom=1"));
                        a.AddRange(Encode(IsWebM(input)));
                        a.Add(outPath);
                        await RunAsync(a, info.Duration, new SlicedProgress(progress, i * 2 + 1, r.Files.Count * 2),
                            IoPath.GetFileName(input), new[] { outPath }, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        var a = new List<string> { "-i", input };
                        a.AddRange(Maps);
                        a.AddRange(Vf("deshake=rx=" + Math.Max(4, strength * 64 / 100).ToString(CultureInfo.InvariantCulture)
                            + ":ry=" + Math.Max(4, strength * 64 / 100).ToString(CultureInfo.InvariantCulture)));
                        a.AddRange(Encode(IsWebM(input)));
                        a.Add(outPath);
                        await RunAsync(a, info.Duration, new SlicedProgress(progress, i, r.Files.Count),
                            IoPath.GetFileName(input), new[] { outPath }, ct).ConfigureAwait(false);
                    }
                }
                finally
                {
                    foreach (var t in temps) TryDelete(t);
                }
            }
            return outs;
        }
        catch
        {
            foreach (var o in outs) TryDelete(o);
            throw;
        }
    }

    // ------------------------------------------------------------------ vid-gif-editor

    internal static Task<IReadOnlyList<string>> GifEditorAsync(ToolRequest r, IProgress<ToolProgress>? p, CancellationToken ct)
    {
        var start = ParseTime(Opt(r, "start") ?? "0", "start");
        var durText = Opt(r, "duration");
        double? length = durText is null ? null : ParseTime(durText, "duration");
        if (length is double l0 && l0 <= 0) throw new ArgumentException("option 'duration' must be greater than 0");
        var width = IntOpt(r, "width", 0, 1280, 0);
        if (width != 0 && width < 16) throw new ArgumentException("option 'width': " + width + " is below 16 (use 0 to keep the size)");
        var fpsOpt = IntOpt(r, "fps", 0, 30, 0);
        var speed = IntOpt(r, "speed", 25, 400, 100);

        return W2OneToOneGif(r, p, ct, (input, info) =>
        {
            W2CheckWindow(start, info);
            var len = Math.Min(length ?? (info.Duration - start), info.Duration - start);
            if (len > 120) throw new ArgumentException("option 'duration': a GIF longer than 120 s is not sensible (this would be " + Num(len) + " s)");
            var fps = fpsOpt > 0 ? fpsOpt : (info.Fps >= 1 ? Math.Min(30, (int)Math.Round(info.Fps)) : 12);
            var chain = new List<string>();
            if (speed != 100) chain.Add("setpts=PTS*100/" + speed.ToString(CultureInfo.InvariantCulture));
            chain.Add("fps=" + fps.ToString(CultureInfo.InvariantCulture));
            if (width > 0) chain.Add("scale=" + width.ToString(CultureInfo.InvariantCulture) + ":-1:flags=lanczos");
            var graph = "[0:v:0]" + string.Join(",", chain) + ",split[a][b];[a]palettegen[p];[b][p]paletteuse[g]";
            var a = new List<string>();
            if (start > 0) a.AddRange(new[] { "-ss", Num(start) });
            a.AddRange(new[] { "-t", Num(len), "-i", input, "-filter_complex", graph, "-map", "[g]", "-an", "-loop", "0" });
            return new W2Plan(a, len * 100.0 / speed);
        });
    }

    private static async Task<IReadOnlyList<string>> W2OneToOneGif(
        ToolRequest r, IProgress<ToolProgress>? progress, CancellationToken ct, Func<string, VideoInfo, W2Plan> build)
    {
        var outs = new List<string>();
        try
        {
            for (var i = 0; i < r.Files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var input = Full(r.Files[i]);
                var info = RequireVideo(input);
                var plan = build(input, info);
                var outPath = ReserveOutput(r.OutDir, input, "edited", ".gif");
                outs.Add(outPath);
                await RunAsync(plan.Args.Concat(new[] { outPath }).ToList(), plan.ExpectedSeconds,
                    new SlicedProgress(progress, i, r.Files.Count), IoPath.GetFileName(input), new[] { outPath }, ct).ConfigureAwait(false);
            }
            return outs;
        }
        catch
        {
            foreach (var o in outs) TryDelete(o);
            throw;
        }
    }
}
