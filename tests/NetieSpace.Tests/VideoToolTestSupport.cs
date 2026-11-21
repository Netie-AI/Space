using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using NetieSpace.Apps.Tools;

namespace NetieSpace.Tests;

/// <summary>Facts read back from a produced file by a separate ffprobe call (independent of the code under test).</summary>
internal sealed record ProbedFile(double Duration, string? VideoCodec, int Width, int Height, string? AudioCodec, string Format);

internal sealed class ListProgress : IProgress<ToolProgress>
{
    public readonly List<ToolProgress> Reports = new();
    public void Report(ToolProgress value) { lock (Reports) Reports.Add(value); }
}

/// <summary>
/// Shared helpers for the VideoTool tests. Inputs are made with ffmpeg lavfi; results are checked with ffprobe
/// and, where the picture matters, by comparing a decoded frame with a reference frame. ffmpeg and ffprobe are
/// required: when they are missing these helpers throw, so a missing engine fails the suite loudly (no skips).
/// </summary>
/// <summary>
/// Cancels on the first progress report that shows ffmpeg mid-encode (1 to 90 percent), synchronously on the
/// reporting thread. A fixed Task.Delay before Cancel races the encode: on a busy runner the delay's
/// continuation runs seconds late and the tool finishes first. Fired says whether the cancel really happened.
/// </summary>
internal sealed class CancelOnProgress : IProgress<ToolProgress>
{
    private readonly CancellationTokenSource _cts;
    private readonly System.Diagnostics.Stopwatch? _sw;
    private int _fired;
    public CancelOnProgress(CancellationTokenSource cts, System.Diagnostics.Stopwatch? sw = null) { _cts = cts; _sw = sw; }
    public bool Fired => Volatile.Read(ref _fired) == 1;
    public void Report(ToolProgress value)
    {
        if (value.Percent is < 1 or > 90) return;
        if (Interlocked.Exchange(ref _fired, 1) == 1) return;
        _sw?.Start();
        _cts.Cancel();
    }
}

internal static class VideoToolTestSupport
{
    private static readonly Lazy<ToolRegistry> RegistryLazy = new(() => ToolRegistry.Load(probe: new EngineProbes()));
    public static ToolRegistry Registry => RegistryLazy.Value;

    // ---------- process helpers ----------

    private static string Exe(string name)
    {
        var bundled = IoPath.Combine(ToolTestSupport.RepoFile("tools", "ffmpeg"), name + (OperatingSystem.IsWindows() ? ".exe" : ""));
        return IoFile.Exists(bundled) ? bundled : name;
    }

    public static (int Exit, byte[] Out, string Err) Exec(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(Exe(exe))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process p;
        try { p = Process.Start(psi)!; }
        catch (Exception ex)
        {
            throw new InvalidOperationException(exe + " is required by the video tool tests but could not be started: " + ex.Message);
        }
        using (p)
        {
            var err = p.StandardError.ReadToEndAsync();
            using var ms = new MemoryStream();
            p.StandardOutput.BaseStream.CopyTo(ms);
            p.WaitForExit();
            return (p.ExitCode, ms.ToArray(), err.Result);
        }
    }

    private static void MustExec(string exe, params string[] args)
    {
        var (code, _, err) = Exec(exe, args);
        if (code != 0) throw new InvalidOperationException(exe + " failed (" + code + "): " + err);
    }

    // ---------- inputs ----------

    /// <summary>A real clip: testsrc picture plus a 440 Hz sine, codecs chosen from the extension.</summary>
    public static string MakeVideo(string dir, string name, double seconds = 3, bool audio = true,
        string size = "320x180", int rate = 15, int crf = 12)
    {
        IoDirectory.CreateDirectory(dir);
        var path = IoPath.Combine(dir, name);
        var ext = IoPath.GetExtension(name).ToLowerInvariant();
        var args = new List<string> { "-y", "-v", "error", "-f", "lavfi", "-i", $"testsrc=size={size}:rate={rate}" };
        if (audio) args.AddRange(new[] { "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100" });
        args.AddRange(new[] { "-t", seconds.ToString(CultureInfo.InvariantCulture) });
        switch (ext)
        {
            case ".webm":
                args.AddRange(new[] { "-c:v", "libvpx", "-b:v", "1M" });
                if (audio) args.AddRange(new[] { "-c:a", "libvorbis" });
                break;
            case ".avi":
                args.AddRange(new[] { "-c:v", "mpeg4", "-q:v", "3" });
                if (audio) args.AddRange(new[] { "-c:a", "libmp3lame" });
                break;
            default:
                args.AddRange(new[] { "-c:v", "libx264", "-crf", crf.ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p" });
                if (audio) args.AddRange(new[] { "-c:a", "aac" });
                break;
        }
        args.Add(path);
        MustExec("ffmpeg", args.ToArray());
        return path;
    }

    public static string MakeAudio(string dir, string name, double seconds = 5, int freq = 880)
    {
        IoDirectory.CreateDirectory(dir);
        var path = IoPath.Combine(dir, name);
        MustExec("ffmpeg", "-y", "-v", "error", "-f", "lavfi", "-i",
            "sine=frequency=" + freq.ToString(CultureInfo.InvariantCulture) + ":sample_rate=44100",
            "-t", seconds.ToString(CultureInfo.InvariantCulture), path);
        return path;
    }

    // ---------- read back ----------

    public static ProbedFile Probe(string path)
    {
        var (code, stdout, err) = Exec("ffprobe", "-v", "error", "-print_format", "json", "-show_format", "-show_streams", "-i", path);
        if (code != 0) throw new InvalidOperationException("ffprobe could not read " + path + ": " + err);
        using var doc = JsonDocument.Parse(stdout);
        string? vc = null, ac = null;
        int w = 0, h = 0;
        foreach (var s in doc.RootElement.GetProperty("streams").EnumerateArray())
        {
            var type = s.GetProperty("codec_type").GetString();
            if (type == "video" && vc is null)
            {
                vc = s.GetProperty("codec_name").GetString();
                w = s.GetProperty("width").GetInt32();
                h = s.GetProperty("height").GetInt32();
            }
            if (type == "audio" && ac is null) ac = s.GetProperty("codec_name").GetString();
        }
        var fmt = doc.RootElement.GetProperty("format");
        var dur = double.Parse(fmt.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        return new ProbedFile(dur, vc, w, h, ac, fmt.GetProperty("format_name").GetString() ?? "");
    }

    public static int CountFrames(string path)
    {
        var (code, stdout, err) = Exec("ffprobe", "-v", "error", "-count_frames", "-select_streams", "v:0",
            "-show_entries", "stream=nb_read_frames", "-of", "csv=p=0", "-i", path);
        if (code != 0) throw new InvalidOperationException("ffprobe failed: " + err);
        return int.Parse(System.Text.Encoding.ASCII.GetString(stdout).Trim(), CultureInfo.InvariantCulture);
    }

    /// <summary>Mean volume in dB of the first audio stream (ffmpeg volumedetect).</summary>
    public static double MeanVolumeDb(string path)
    {
        var (_, _, err) = Exec("ffmpeg", "-hide_banner", "-i", path, "-vn", "-af", "volumedetect", "-f", "null", "-");
        var m = System.Text.RegularExpressions.Regex.Match(err, @"mean_volume:\s*(-?[\d.]+|-inf) dB");
        if (!m.Success) throw new InvalidOperationException("no mean_volume in: " + err);
        return m.Groups[1].Value == "-inf" ? -200 : double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>Frame n (0-based) as 64x36 gray bytes, after an optional reference filter chain.</summary>
    public static byte[] FrameGray(string path, int n, string? pre = null)
    {
        var vf = (pre is null ? "" : pre + ",") + $"select=eq(n\\,{n}),scale=64:36:flags=area,format=gray";
        var (code, bytes, err) = Exec("ffmpeg", "-v", "error", "-i", path, "-vf", vf, "-frames:v", "1", "-f", "rawvideo", "-");
        if (code != 0 || bytes.Length != 64 * 36)
            throw new InvalidOperationException("could not read frame " + n + " of " + path + " (" + bytes.Length + " bytes): " + err);
        return bytes;
    }

    public static double MeanAbsDiff(byte[] a, byte[] b)
    {
        Assert.Equal(a.Length, b.Length);
        double sum = 0;
        for (var i = 0; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]);
        return sum / a.Length;
    }

    // ---------- driving the tools ----------

    public static string NewDir() => ToolTestSupport.NewTempDir();

    public static ToolResult Run(string id, string outDir, string[] files, params (string Key, string Value)[] opts)
    {
        var tool = Registry.Find(id) ?? throw new InvalidOperationException("unknown tool " + id);
        Assert.True(tool.Availability.Available, id + " should be available: " + tool.Availability.Text);
        var req = new ToolRequest(files, outDir, opts.ToDictionary(o => o.Key, o => o.Value));
        return ToolRunner.RunAsync(tool, req, null, CancellationToken.None).GetAwaiter().GetResult();
    }

    public static IReadOnlyList<string> RunOk(string id, string outDir, string[] files, params (string Key, string Value)[] opts)
    {
        var r = Run(id, outDir, files, opts);
        Assert.True(r.Ok, id + " failed: " + r.Error);
        foreach (var o in r.Outputs)
        {
            Assert.True(IoFile.Exists(o), "missing output " + o);
            Assert.StartsWith(IoPath.GetFullPath(outDir), IoPath.GetFullPath(o));
        }
        return r.Outputs;
    }

    public static string RunOk1(string id, string outDir, string[] files, params (string Key, string Value)[] opts)
    {
        var outs = RunOk(id, outDir, files, opts);
        Assert.Single(outs);
        return outs[0];
    }

    public static (int Exit, string Out, string Err) Cli(string id, string outDir, string[] files, params (string Key, string Value)[] opts)
    {
        var args = new List<string> { "--tool", id };
        foreach (var f in files) { args.Add("--in"); args.Add(f); }
        args.Add("--out"); args.Add(outDir);
        foreach (var (k, v) in opts) { args.Add("--opt"); args.Add(k + "=" + v); }
        var o = new StringWriter();
        var e = new StringWriter();
        var code = ToolCli.RunWith(args.ToArray(), o, e, Registry);
        return (code, o.ToString(), e.ToString());
    }

    /// <summary>Runs the CLI, expects exit 0, and returns the printed output paths (each must exist).</summary>
    public static string[] CliOk(string id, string outDir, string[] files, params (string Key, string Value)[] opts)
    {
        var (code, stdout, stderr) = Cli(id, outDir, files, opts);
        Assert.True(code == 0, id + " exit " + code + ": " + stderr);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
        Assert.NotEmpty(lines);
        foreach (var l in lines) Assert.True(IoFile.Exists(l) && new FileInfo(l).Length > 0, "CLI printed a path that is not a real file: " + l);
        return lines;
    }
}
