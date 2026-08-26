using System.Diagnostics;
using NetieSpace.Services;

namespace NetieSpace.Tests;

/// <summary>
/// Runs Space's FfmpegExportGraph through real ffmpeg on Windows CI.
/// Does not check the PARKING_LOT human Export gate.
/// </summary>
public class FfmpegExportLiveTests
{
    [Fact]
    public void Fade_then_wipeleft_encodes()
    {
        var ffmpeg = Which("ffmpeg");
        var ffprobe = Which("ffprobe");
        if (ffmpeg is null || ffprobe is null)
        {
            Assert.False(
                IsCi(),
                "ffmpeg/ffprobe required on CI for V1 export smoke");
            return;
        }

        using var tmp = new TempDir();
        var a = Path.Combine(tmp.Path, "a.mp4");
        var b = Path.Combine(tmp.Path, "b.mp4");
        var faded = Path.Combine(tmp.Path, "faded.mp4");
        var outPath = Path.Combine(tmp.Path, "wipe.mp4");

        Run(ffmpeg,
            "-y", "-f", "lavfi", "-i", "color=c=red:s=320x240:d=2",
            "-f", "lavfi", "-i", "sine=f=440:d=2", "-shortest",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", a);
        Run(ffmpeg,
            "-y", "-f", "lavfi", "-i", "color=c=blue:s=320x240:d=2",
            "-f", "lavfi", "-i", "sine=f=880:d=2", "-shortest",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", b);
        Run(ffmpeg,
            "-y", "-i", a, "-t", "1.6",
            "-vf", "fade=t=in:st=0:d=0.2,fade=t=out:st=1.2:d=0.2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", faded);

        var fc = FfmpegExportGraph.XfadeWithAudio(
            TimeSpan.FromSeconds(1.6), TimeSpan.FromMilliseconds(500), 320, 240);
        Run(ffmpeg,
            "-y", "-i", faded, "-i", b,
            "-filter_complex", fc,
            "-map", "[vout]", "-map", "[aout]",
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "23",
            "-c:a", "aac", "-movflags", "+faststart", outPath);

        Assert.True(new FileInfo(outPath).Length > 1000);
        var dur = ProbeDuration(ffprobe, outPath);
        // Same 2.8..3.4 as check-pillar1-export. Hard-cut concat is ~3.6s
        // and used to pass the old 2.5..4.0 window.
        Assert.True(dur is >= 2.8 and <= 3.4, $"duration {dur} expect 2.8..3.4 (wipe ~3.1; hard-cut ~3.6)");
    }

    private static bool IsCi()
    {
        var v = Environment.GetEnvironmentVariable("CI");
        return v is "true" or "True" or "1";
    }

    private static string? Which(string name)
    {
        var file = OperatingSystem.IsWindows() ? name + ".exe" : name;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            var candidate = Path.Combine(dir, file);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static void Run(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(120_000))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException(exe);
        }

        stdout.GetAwaiter().GetResult();
        var err = stderr.GetAwaiter().GetResult();
        if (p.ExitCode != 0)
            throw new InvalidOperationException(err);
    }

    private static double ProbeDuration(string ffprobe, string path)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffprobe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var a in new[]
                 {
                     "-v", "error", "-show_entries", "format=duration",
                     "-of", "default=noprint_wrappers=1:nokey=1", path
                 })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit(30_000);
        return double.Parse(p.StandardOutput.ReadToEnd().Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("netie-xfade-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* ignore */ }
        }
    }
}
