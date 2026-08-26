using System.Diagnostics;
using NetieSpace.Services;
using NetieSpace.Services.Timeline;

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

    [Fact]
    public async Task ExportTimeline_playbook_fade_wipe_to_apostrophe_folder()
    {
        var ffmpeg = Which("ffmpeg");
        var ffprobe = Which("ffprobe");
        if (ffmpeg is null || ffprobe is null)
        {
            Assert.False(IsCi(), "ffmpeg/ffprobe required on CI for V1 export smoke");
            return;
        }

        using var tmp = new TempDir();
        var folder = Path.Combine(tmp.Path, "Mom's clips");
        Directory.CreateDirectory(folder);
        var a = Path.Combine(folder, "clip_a.mp4");
        var b = Path.Combine(folder, "clip_b.mp4");
        var outPath = Path.Combine(folder, "export.mp4");
        MakePillarClip(ffmpeg, a, "blue", 1.6, 440);
        MakePillarClip(ffmpeg, b, "red", 2.0, 880);

        var project = PlaybookProject(a, b, wipe: true);
        var svc = new FfmpegService();
        Assert.True(svc.IsAvailable, "ResolveFfmpeg must find PATH ffmpeg (not Windows where only)");
        var result = await svc.ExportTimelineAsync(project, outPath);
        Assert.True(result.Success, result.Error);
        Assert.True(File.Exists(outPath) && new FileInfo(outPath).Length > 1000);
        var dur = ProbeDuration(ffprobe, outPath);
        Assert.True(dur is >= 2.8 and <= 3.4, $"ExportTimeline wipe {dur} expect 2.8..3.4");
    }

    [Fact]
    public async Task ExportTimeline_hardcut_concat_to_apostrophe_folder()
    {
        var ffmpeg = Which("ffmpeg");
        var ffprobe = Which("ffprobe");
        if (ffmpeg is null || ffprobe is null)
        {
            Assert.False(IsCi(), "ffmpeg/ffprobe required on CI for V1 export smoke");
            return;
        }

        using var tmp = new TempDir();
        var folder = Path.Combine(tmp.Path, "Mom's clips");
        Directory.CreateDirectory(folder);
        var a = Path.Combine(folder, "clip_a.mp4");
        var b = Path.Combine(folder, "clip_b.mp4");
        var outPath = Path.Combine(folder, "hardcut.mp4");
        MakePillarClip(ffmpeg, a, "blue", 1.6, 440);
        MakePillarClip(ffmpeg, b, "red", 2.0, 880);

        var project = PlaybookProject(a, b, wipe: false);
        var svc = new FfmpegService();
        Assert.True(svc.IsAvailable, "ResolveFfmpeg must find PATH ffmpeg (not Windows where only)");
        var result = await svc.ExportTimelineAsync(project, outPath);
        Assert.True(result.Success, result.Error);
        var dur = ProbeDuration(ffprobe, outPath);
        // 1.6+2.0 hard-cut. Absolute concat paths used to open Moms and fail.
        Assert.True(dur is >= 3.4 and <= 3.8, $"ExportTimeline hard-cut {dur} expect ~3.6");
    }

    [Fact]
    public async Task ExportTimeline_leave_black_last_clip_keeps_card_duration()
    {
        var ffmpeg = Which("ffmpeg");
        var ffprobe = Which("ffprobe");
        if (ffmpeg is null || ffprobe is null)
        {
            Assert.False(IsCi(), "ffmpeg/ffprobe required on CI for V1 export smoke");
            return;
        }

        using var tmp = new TempDir();
        var a = Path.Combine(tmp.Path, "clip_a.mp4");
        var b = Path.Combine(tmp.Path, "clip_b.mp4");
        var outPath = Path.Combine(tmp.Path, "leave_black.mp4");
        MakePillarClip(ffmpeg, a, "blue", 1.6, 440);
        MakePillarClip(ffmpeg, b, "red", 2.0, 880);

        var session = new VideoEditSession();
        session.Reset(TimeSpan.FromSeconds(1.6), a);
        session.AppendClip(b, TimeSpan.FromSeconds(2.0));
        session.Select(1);
        var gapStart = session.PackedStartOf(1);
        var gapDur = session.PackedEndOf(1) - gapStart;
        Assert.True(session.DeleteSelectedOrAt(TimeSpan.Zero));
        Assert.True(session.InsertBlackPlaceholder(1, gapDur));

        var project = new TimelineProject();
        project.ResetFromSource(a, TimeSpan.FromSeconds(1.6), 320, 240);
        session.ApplyTo(project, a);
        project.Width = 320;
        project.Height = 240;
        project.AddOverlay(OverlayKind.Black, gapStart, gapDur);

        var svc = new FfmpegService();
        Assert.True(svc.IsAvailable);
        var result = await svc.ExportTimelineAsync(project, outPath);
        Assert.True(result.Success, result.Error);
        var dur = ProbeDuration(ffprobe, outPath);
        // Leftover 1.6 + 2.0 hole. Missing hole/pad used to burn 1.6s and drop the card.
        Assert.True(dur is >= 3.4 and <= 3.8, $"Leave-black last clip {dur} expect ~3.6 (1.6 leftover used to drop the card)");
    }

    [Fact]
    public async Task ExportTimeline_caption_and_text_from_apostrophe_folder()
    {
        var ffmpeg = Which("ffmpeg");
        var ffprobe = Which("ffprobe");
        if (ffmpeg is null || ffprobe is null)
        {
            Assert.False(IsCi(), "ffmpeg/ffprobe required on CI for V1 export smoke");
            return;
        }

        using var tmp = new TempDir();
        var folder = Path.Combine(tmp.Path, "Mom's clips");
        Directory.CreateDirectory(folder);
        var a = Path.Combine(folder, "clip_a.mp4");
        var b = Path.Combine(folder, "clip_b.mp4");
        var srt = Path.Combine(folder, "clip_a.srt");
        var outPath = Path.Combine(folder, "captions.mp4");
        MakePillarClip(ffmpeg, a, "blue", 1.6, 440);
        MakePillarClip(ffmpeg, b, "red", 2.0, 880);
        File.WriteAllText(srt, "1\n00:00:00,000 --> 00:00:00,400\nhi\n");

        var project = PlaybookProject(a, b, wipe: true);
        project.SubtitleBurnPath = srt;
        project.AddOverlay(OverlayKind.Text, TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(1), "Title");

        var svc = new FfmpegService();
        Assert.True(svc.IsAvailable);
        var result = await svc.ExportTimelineAsync(project, outPath);
        Assert.True(result.Success, result.Error);
        var dur = ProbeDuration(ffprobe, outPath);
        Assert.True(dur is >= 2.8 and <= 3.4, $"caption+text wipe {dur} expect 2.8..3.4");
    }

    [Fact]
    public async Task ExportTimeline_slice_packed_second_clip_is_not_clip1_trim()
    {
        var ffmpeg = Which("ffmpeg");
        var ffprobe = Which("ffprobe");
        if (ffmpeg is null || ffprobe is null)
        {
            Assert.False(IsCi(), "ffmpeg/ffprobe required on CI for V1 export smoke");
            return;
        }

        using var tmp = new TempDir();
        var a = Path.Combine(tmp.Path, "clip_a.mp4");
        var b = Path.Combine(tmp.Path, "clip_b.mp4");
        var outPath = Path.Combine(tmp.Path, "range_clip2.mp4");
        MakePillarClip(ffmpeg, a, "blue", 1.6, 440);
        MakePillarClip(ffmpeg, b, "red", 2.0, 880);

        var project = PlaybookProject(a, b, wipe: false);
        var snap = project.Clone();
        Assert.True(snap.SlicePacked(TimeSpan.FromSeconds(1.6), project.PackedVideoDuration()));
        Assert.Single(snap.VideoTrack!.Clips);
        Assert.Equal(b, snap.VideoTrack.Clips[0].SourcePath);

        var svc = new FfmpegService();
        Assert.True(svc.IsAvailable);
        var result = await svc.ExportTimelineAsync(snap, outPath);
        Assert.True(result.Success, result.Error);
        var dur = ProbeDuration(ffprobe, outPath);
        // More... Export range used to TrimAsync clip 1 at packed 1.6..3.6 (~1.6s).
        Assert.True(dur is >= 1.8 and <= 2.2, $"Export range clip 2 {dur} expect ~2.0 (clip-1 trim used to be ~1.6)");
    }

    [Fact]
    public async Task ExportTimeline_slice_packed_full_window_keeps_wipe()
    {
        var ffmpeg = Which("ffmpeg");
        var ffprobe = Which("ffprobe");
        if (ffmpeg is null || ffprobe is null)
        {
            Assert.False(IsCi(), "ffmpeg/ffprobe required on CI for V1 export smoke");
            return;
        }

        using var tmp = new TempDir();
        var a = Path.Combine(tmp.Path, "clip_a.mp4");
        var b = Path.Combine(tmp.Path, "clip_b.mp4");
        var outPath = Path.Combine(tmp.Path, "range_full.mp4");
        MakePillarClip(ffmpeg, a, "blue", 1.6, 440);
        MakePillarClip(ffmpeg, b, "red", 2.0, 880);

        var project = PlaybookProject(a, b, wipe: true);
        var snap = project.Clone();
        // + Clip expands marks to 0..packed. Leftover 0..1.6 used to burn clip 1 only.
        Assert.True(snap.SlicePacked(TimeSpan.Zero, project.PackedVideoDuration()));

        var svc = new FfmpegService();
        Assert.True(svc.IsAvailable);
        var result = await svc.ExportTimelineAsync(snap, outPath);
        Assert.True(result.Success, result.Error);
        var dur = ProbeDuration(ffprobe, outPath);
        Assert.True(dur is >= 2.8 and <= 3.4, $"Export range full window {dur} expect 2.8..3.4");
    }

    private static TimelineProject PlaybookProject(string clipA, string clipB, bool wipe)
    {
        var session = new VideoEditSession();
        session.Reset(TimeSpan.FromSeconds(1.6), clipA);
        session.AppendClip(clipB, TimeSpan.FromSeconds(2.0));
        session.Select(0);
        session.ApplyFadeAt(0, TimeSpan.FromSeconds(0.5), null);
        session.Select(1);
        session.ApplyFadeAt(1, null, TimeSpan.FromSeconds(0.5));
        if (wipe)
        {
            session.Select(0);
            Assert.True(session.ApplyWipeAtJoin());
        }

        var project = new TimelineProject();
        project.ResetFromSource(clipA, TimeSpan.FromSeconds(1.6), 320, 240);
        session.ApplyTo(project, clipA);
        project.Width = 320;
        project.Height = 240;
        return project;
    }

    private static void MakePillarClip(string ffmpeg, string path, string color, double seconds, int hz)
    {
        Run(ffmpeg,
            "-y", "-f", "lavfi", "-i", $"color=c={color}:s=320x240:d={seconds}",
            "-f", "lavfi", "-i", $"sine=f={hz}:d={seconds}", "-shortest",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", path);
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
