using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using NetieSpace.Apps.Tools;

namespace NetieSpace.Tests;

/// <summary>
/// Shared pieces for the readiness matrix and the scale checks (space/readiness-20261128).
/// Both are opt-in: they run only when NETIE_READINESS is exactly "1", so the normal suite keeps its speed.
/// Reports go to NETIE_READINESS_OUT (default: a temp folder) as JSON plus Markdown, never invented: every
/// row is a real ToolRunner call on a real generated file.
/// </summary>
internal static class Readiness
{
    public static bool Enabled => Environment.GetEnvironmentVariable("NETIE_READINESS") == "1";

    public static string OutDir()
    {
        var d = Environment.GetEnvironmentVariable("NETIE_READINESS_OUT");
        if (string.IsNullOrWhiteSpace(d)) d = IoPath.Combine(IoPath.GetTempPath(), "netie-readiness");
        IoDirectory.CreateDirectory(d);
        return d;
    }

    public static void WriteJson(string name, object value)
    {
        var path = IoPath.Combine(OutDir(), name);
        IoFile.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void WriteText(string name, string text) => IoFile.WriteAllText(IoPath.Combine(OutDir(), name), text);

    /// <summary>Peak resident set of this process in bytes (Linux VmHWM, Windows PeakWorkingSet64).</summary>
    public static long PeakRss()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                foreach (var line in IoFile.ReadLines("/proc/self/status"))
                    if (line.StartsWith("VmHWM:", StringComparison.Ordinal))
                        return long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture) * 1024;
            }
            return Process.GetCurrentProcess().PeakWorkingSet64;
        }
        catch (Exception) { return -1; }
    }

    public static long CurrentRss()
    {
        try { var p = Process.GetCurrentProcess(); p.Refresh(); return p.WorkingSet64; }
        catch (Exception) { return -1; }
    }

    /// <summary>One input file for an extension, or null when the suite has no honest way to make one.</summary>
    public static string? MakeInput(string dir, string ext, int index, int pdfPages = 3)
    {
        IoDirectory.CreateDirectory(dir);
        var name = "in" + index.ToString(CultureInfo.InvariantCulture) + ext;
        switch (ext)
        {
            case ".pdf":
                return PdfKit.Make(dir, name, pdfPages, tag: "DOC" + index, extraLines: 6);
            case ".png": case ".jpg": case ".jpeg": case ".gif": case ".bmp":
                return ImageToolTestSupport.NoisyImage(dir, name, ext.TrimStart('.') == "jpeg" ? "jpg" : ext.TrimStart('.'), 320, 240);
            case ".mp4": case ".mov": case ".mkv":
                return VideoToolTestSupport.MakeVideo(dir, name, seconds: 3, audio: true);
            case ".mp3": case ".wav": case ".m4a":
                return VideoToolTestSupport.MakeAudio(dir, name, seconds: 3);
            case ".html": case ".htm":
            {
                var p = IoPath.Combine(dir, name);
                IoFile.WriteAllText(p, "<!doctype html><html><body><h1>Netie readiness " + index +
                    "</h1><p>Quarterly revenue grew 12 percent.</p></body></html>", Encoding.UTF8);
                return p;
            }
            case ".srt":
            {
                var p = IoPath.Combine(dir, name);
                IoFile.WriteAllText(p, "1\n00:00:00,000 --> 00:00:02,000\nHello from Netie\n\n", Encoding.UTF8);
                return p;
            }
            default:
                return null;
        }
    }

    /// <summary>Preferred fixture order: cheap, well-supported types first.</summary>
    public static readonly string[] Preference =
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".mp4", ".mov", ".mkv", ".mp3", ".wav", ".m4a", ".gif", ".bmp", ".html", ".htm", ".srt",
    };

    public static string? PickExt(ToolEntry e)
    {
        var accepted = e.AcceptedTypes.Select(a => a.StartsWith('.') ? a.ToLowerInvariant() : "." + a.ToLowerInvariant()).ToHashSet();
        return Preference.FirstOrDefault(accepted.Contains);
    }

    public static async Task<ToolResult> RunWithTimeout(RegisteredTool tool, IReadOnlyList<string> files, string outDir,
        IReadOnlyDictionary<string, string> opts, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        return await ToolRunner.RunAsync(tool, new ToolRequest(files, outDir, opts), null, cts.Token);
    }

    /// <summary>A PDF whose pages carry a large photo-like picture, so compression has real work to do.</summary>
    public static string MakeImagePdf(string dir, string name, int pictures)
    {
        IoDirectory.CreateDirectory(dir);
        var pics = Enumerable.Range(1, pictures)
            .Select(i => (PdfTool3Kit.PhotoPng(dir, "photo" + i + ".png", 1800, 1200, seed: i), 400.0, 266.0)).ToArray();
        return PdfTool3Kit.PdfWithImages(dir, name, "Readiness pictures", pics);
    }

    /// <summary>A real PDF with its startxref pointing at the wrong offset: opens only after a lenient rescan.</summary>
    public static string MakeDamagedPdf(string dir, string name)
    {
        IoDirectory.CreateDirectory(dir);
        var orig = PdfKit.Make(dir, "orig-" + name, 3, "ALPHA", extraLines: 4);
        var s = Encoding.Latin1.GetString(IoFile.ReadAllBytes(orig));
        var sx = s.LastIndexOf("startxref", StringComparison.Ordinal);
        var eof = s.IndexOf("%%EOF", sx, StringComparison.Ordinal);
        var path = IoPath.Combine(dir, name);
        IoFile.WriteAllBytes(path, Encoding.Latin1.GetBytes(s[..sx] + "startxref\n7\n" + s[eof..]));
        return path;
    }

    /// <summary>A PDF holding a 3-column table with 5 rows, laid out with wide gaps.</summary>
    public static string MakeTablePdf(string dir, string name)
    {
        IoDirectory.CreateDirectory(dir);
        var rows = new[]
        {
            new[] { "Region", "Quarter", "Revenue" },
            new[] { "North", "Q1", "1200" },
            new[] { "South", "Q1", "950" },
            new[] { "East", "Q2", "1430" },
            new[] { "West", "Q2", "1010" },
        };
        return PdfTool3Kit.Tables(dir, name, rows, rows, rows);
    }

    /// <summary>A clip with per-frame noise, so its bitrate is high like a phone video and compression can shrink it.</summary>
    public static string MakeNoisyVideo(string dir, string name, double seconds, string size, int rate = 30)
    {
        IoDirectory.CreateDirectory(dir);
        var path = IoPath.Combine(dir, name);
        var (code, _, err) = VideoToolTestSupport.Exec("ffmpeg", "-y", "-v", "error",
            "-f", "lavfi", "-i", "testsrc2=size=" + size + ":rate=" + rate.ToString(CultureInfo.InvariantCulture) + ",noise=alls=30:allf=t",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100",
            "-t", seconds.ToString(CultureInfo.InvariantCulture),
            "-c:v", "libx264", "-crf", "16", "-preset", "veryfast", "-pix_fmt", "yuv420p", "-c:a", "aac", path);
        if (code != 0) throw new InvalidOperationException("ffmpeg failed making " + name + ": " + err);
        return path;
    }

    public static string Mb(long bytes) => bytes < 0 ? "n/a" : (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);
}
