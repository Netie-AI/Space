using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;

namespace NetieSpace.Apps.Video;

/// <summary>What ffprobe says about one input.</summary>
internal sealed record VideoInfo(
    double Duration, bool HasVideo, bool HasAudio, int Width, int Height, double Fps, string VideoCodec);

/// <summary>
/// Out-of-process FFmpeg plumbing for the video tools. Every call uses an argument list, never a shell
/// string, so a hostile file name or text option cannot reach a shell. ffmpeg is resolved from the bundled
/// tools/ffmpeg folder first, then PATH (the same order FfmpegService uses; its resolver is private, so this
/// small helper repeats the order). ffprobe goes through FfmpegService.ResolveFfprobe (internal, same assembly).
/// </summary>
internal static class VideoFfmpeg
{
    public static string RequireFfmpeg()
    {
        foreach (var dir in BundledDirs())
            foreach (var name in new[] { "ffmpeg.exe", "ffmpeg" })
            {
                var p = IoPath.Combine(dir, name);
                if (IoFile.Exists(p)) return p;
            }
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(IoPath.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var name in new[] { "ffmpeg.exe", "ffmpeg" })
            {
                var p = IoPath.Combine(dir, name);
                if (IoFile.Exists(p)) return p;
            }
        }
        throw new InvalidOperationException("FFmpeg was not found (tools/ffmpeg or PATH)");
    }

    private static IEnumerable<string> BundledDirs()
    {
        string? root = null;
        try { root = NetieSpace.Helpers.ProjectRoot.Find(); } catch { /* ignore */ }
        if (!string.IsNullOrEmpty(root)) yield return IoPath.Combine(root, "tools", "ffmpeg");
        yield return IoPath.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
    }

    public static string RequireFfprobe() =>
        NetieSpace.Services.FfmpegService.ResolveFfprobe()
        ?? throw new InvalidOperationException("ffprobe was not found next to ffmpeg or on PATH");

    public static string Num(double d) => d.ToString("0.######", CultureInfo.InvariantCulture);

    public static string Full(string path) => IoPath.GetFullPath(path);

    // ---------- probing ----------

    public static VideoInfo Probe(string path)
    {
        var psi = new ProcessStartInfo
        {
            FileName = RequireFfprobe(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-v", "error", "-print_format", "json", "-show_format", "-show_streams", "-i", Full(path) })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("could not start ffprobe");
        var err = p.StandardError.ReadToEndAsync();
        var json = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException("ffprobe could not read " + path + ": " + Tail(err.Result));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        double dur = 0;
        if (root.TryGetProperty("format", out var fmt) && fmt.TryGetProperty("duration", out var fd))
            dur = ParseDouble(fd);
        bool hasV = false, hasA = false;
        int w = 0, h = 0;
        double fps = 0;
        var codec = "";
        if (root.TryGetProperty("streams", out var streams))
            foreach (var s in streams.EnumerateArray())
            {
                var type = s.TryGetProperty("codec_type", out var t) ? t.GetString() : null;
                if (type == "audio") hasA = true;
                if (type == "video" && !hasV)
                {
                    if (s.TryGetProperty("disposition", out var disp) && disp.TryGetProperty("attached_pic", out var ap) && ap.GetInt32() == 1)
                        continue;
                    hasV = true;
                    w = s.TryGetProperty("width", out var ww) ? ww.GetInt32() : 0;
                    h = s.TryGetProperty("height", out var hh) ? hh.GetInt32() : 0;
                    codec = s.TryGetProperty("codec_name", out var cn) ? cn.GetString() ?? "" : "";
                    if (s.TryGetProperty("avg_frame_rate", out var fr)) fps = ParseRate(fr.GetString());
                    if (dur <= 0 && s.TryGetProperty("duration", out var sd)) dur = ParseDouble(sd);
                }
            }
        return new VideoInfo(dur, hasV, hasA, w, h, fps, codec);
    }

    private static double ParseDouble(JsonElement e)
    {
        var s = e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText();
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static double ParseRate(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var parts = s.Split('/');
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return 0;
        if (parts.Length == 1) return n;
        return double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 ? n / d : 0;
    }

    // ---------- running ----------

    /// <summary>
    /// Runs ffmpeg with <paramref name="args"/>. Progress comes from ffmpeg's -progress output against
    /// <paramref name="expectedSeconds"/>. On any failure or cancel the files in <paramref name="cleanup"/> are deleted.
    /// </summary>
    public static async Task RunAsync(
        IReadOnlyList<string> args, double expectedSeconds, IProgress<ToolProgress>? progress, string label,
        IEnumerable<string> cleanup, CancellationToken ct)
    {
        var cleanupList = cleanup.ToList();
        try
        {
            ct.ThrowIfCancellationRequested();
            var psi = new ProcessStartInfo
            {
                FileName = RequireFfmpeg(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in new[] { "-hide_banner", "-nostdin", "-y", "-v", "error", "-nostats", "-progress", "pipe:1" })
                psi.ArgumentList.Add(a);
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi) ?? throw new InvalidOperationException("could not start ffmpeg");
            using var reg = ct.Register(() => { try { p.Kill(true); } catch { /* already gone */ } });

            var errBuf = new StringBuilder();
            var errTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await p.StandardError.ReadLineAsync().ConfigureAwait(false)) is not null)
                {
                    if (errBuf.Length < 8000) errBuf.AppendLine(line);
                }
            });
            var outTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await p.StandardOutput.ReadLineAsync().ConfigureAwait(false)) is not null)
                {
                    if (progress is null || expectedSeconds <= 0) continue;
                    if (!line.StartsWith("out_time_us=", StringComparison.Ordinal)) continue;
                    if (!long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us) || us < 0) continue;
                    var pct = (int)Math.Min(99, us / 1_000_000.0 / expectedSeconds * 100);
                    progress.Report(new ToolProgress(pct, label));
                }
            });
            await p.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(errTask, outTask).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (p.ExitCode != 0)
                throw new InvalidOperationException("ffmpeg failed (exit " + p.ExitCode + "): " + Tail(errBuf.ToString()));
        }
        catch
        {
            foreach (var f in cleanupList) TryDelete(f);
            throw;
        }
    }

    private static string Tail(string s)
    {
        s = s.Trim();
        return s.Length > 600 ? s[^600..] : s;
    }

    public static void TryDelete(string path)
    {
        try { if (IoFile.Exists(path)) IoFile.Delete(path); } catch { /* ignore */ }
    }

    // ---------- outputs ----------

    /// <summary>
    /// Reserves a new, unused file name in <paramref name="outDir"/> (atomic CreateNew, so two inputs that
    /// share a file name, or two concurrent runs, never share an output). Never returns an existing path.
    /// </summary>
    public static string ReserveOutput(string outDir, string inputPath, string tag, string ext, string? suffix = null)
    {
        var dir = Full(outDir);
        IoDirectory.CreateDirectory(dir);
        var stem = IoPath.GetFileNameWithoutExtension(inputPath);
        if (string.IsNullOrWhiteSpace(stem)) stem = "video";
        ext = ext.StartsWith('.') ? ext : "." + ext;
        for (var n = 1; n < 10000; n++)
        {
            var name = stem + "-" + tag + (suffix ?? "") + (n == 1 ? "" : "-" + n) + ext;
            var path = IoPath.Combine(dir, name);
            try
            {
                using (new System.IO.FileStream(path, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write)) { }
                return path;
            }
            catch (System.IO.IOException) when (IoFile.Exists(path)) { /* taken, try the next */ }
        }
        throw new InvalidOperationException("could not find a free output name in " + dir);
    }

    // ---------- option parsing ----------

    private static readonly Regex Clock = new(@"^(?:(\d+):)?(\d+):(\d+(?:\.\d+)?)$", RegexOptions.Compiled);

    /// <summary>Seconds ("12", "12.5") or clock ("1:02", "0:01:02.5") to seconds; names the option on error.</summary>
    public static double ParseTime(string text, string option)
    {
        var t = (text ?? "").Trim();
        if (double.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var secs) && secs >= 0)
            return secs;
        var m = Clock.Match(t);
        if (m.Success)
        {
            var h = m.Groups[1].Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            var mi = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var s = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            if (s < 60 && (!m.Groups[1].Success || mi < 60)) return h * 3600 + mi * 60 + s;
        }
        throw new ArgumentException("option '" + option + "': '" + text + "' is not a time (use seconds like 12.5 or clock like 1:02:03)");
    }

    public static string? Opt(ToolRequest r, string key) =>
        r.Options.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    public static int IntOpt(ToolRequest r, string key, int min, int max, int? fallback = null)
    {
        var v = Opt(r, key);
        if (v is null)
        {
            if (fallback is int f) return f;
            throw new ArgumentException("option '" + key + "' is required");
        }
        if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            throw new ArgumentException("option '" + key + "': '" + v + "' is not an integer");
        if (n < min || n > max)
            throw new ArgumentException("option '" + key + "': " + n + " is outside " + min + ".." + max);
        return n;
    }

    // ---------- encode profiles ----------

    public static readonly string[] VideoExts = { ".mp4", ".mov", ".avi", ".mkv", ".webm", ".wmv", ".m4v", ".mpeg", ".3gp", ".ts" };
    public static readonly string[] AudioExts = { ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg" };

    public static bool IsWebM(string inputPath) =>
        string.Equals(IoPath.GetExtension(inputPath), ".webm", StringComparison.OrdinalIgnoreCase);

    /// <summary>Re-encode profile: H.264 + AAC in mp4, or VP9 + Opus when the input is webm.</summary>
    public static string[] Encode(bool webm, int crf = 20) => webm
        ? new[] { "-c:v", "libvpx-vp9", "-deadline", "good", "-cpu-used", "5", "-b:v", "0", "-crf", Math.Max(crf + 12, 24).ToString(CultureInfo.InvariantCulture),
                  "-pix_fmt", "yuv420p", "-c:a", "libopus", "-b:a", "128k" }
        : new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", crf.ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p",
                  "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart" };

    public static string OutExt(string inputPath) => IsWebM(inputPath) ? ".webm" : ".mp4";

    /// <summary>yuv420p needs even sizes; always the last filter of a video chain.</summary>
    public const string EvenScale = "scale=trunc(iw/2)*2:trunc(ih/2)*2";

    public static string[] Vf(params string[] filters) =>
        new[] { "-vf", string.Join(",", filters.Concat(new[] { EvenScale })) };

    public static readonly string[] Maps = { "-map", "0:v:0", "-map", "0:a:0?" };

    /// <summary>Containers where video can be stream-copied while the audio is re-encoded to AAC.</summary>
    public static bool CopyOk(string inputPath) =>
        new[] { ".mp4", ".mov", ".m4v", ".mkv" }.Contains(IoPath.GetExtension(inputPath), StringComparer.OrdinalIgnoreCase);

    public static VideoInfo RequireVideo(string path)
    {
        var info = Probe(path);
        if (!info.HasVideo) throw new InvalidOperationException(path + " has no video stream");
        if (info.Duration <= 0) throw new InvalidOperationException("could not read the duration of " + path);
        return info;
    }
}

/// <summary>Maps one file's 0..100 progress onto its slot in a batch. Synchronous, unlike Progress of T.</summary>
internal sealed class SlicedProgress : IProgress<ToolProgress>
{
    private readonly IProgress<ToolProgress>? _inner;
    private readonly int _index, _count;
    public SlicedProgress(IProgress<ToolProgress>? inner, int index, int count) { _inner = inner; _index = index; _count = Math.Max(1, count); }
    public void Report(ToolProgress v) => _inner?.Report(new ToolProgress(Math.Min(99, (_index * 100 + v.Percent) / _count), v.Message));
}
