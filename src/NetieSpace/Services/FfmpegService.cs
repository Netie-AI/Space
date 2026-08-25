using System.Diagnostics;
using System.Text;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

public sealed class FfmpegResult
{
    public bool Success { get; init; }
    public string? OutputPath { get; init; }
    public string? Error { get; init; }
}

public sealed class FfmpegService
{
    private readonly string? _ffmpegPath;

    public FfmpegService()
    {
        _ffmpegPath = ResolveFfmpeg();
        AppLogger.Info(_ffmpegPath is null
            ? "FFmpeg not found — video edit buttons will prompt to install"
            : $"FFmpeg: {_ffmpegPath}");
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_ffmpegPath) && IoFile.Exists(_ffmpegPath);

    public async Task<FfmpegResult> TrimAsync(
        string input,
        TimeSpan start,
        TimeSpan end,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        if (end <= start)
            return Fail("End time must be after start time.");

        var output = string.IsNullOrWhiteSpace(outputPath)
            ? SiblingOutput(input, "trim")
            : outputPath;

        // Fast path: stream-copy (may snap to keyframes)
        var copy = await RunAsync(
            $"-y -ss {FormatTs(start)} -to {FormatTs(end)} -i \"{input}\" -c copy \"{output}\"",
            output, ct);
        if (copy.Success)
            return copy;

        // Keyframe-safe fallback: re-encode so In/Out land on exact frames
        TryDelete(output);
        var reencode = await RunAsync(
            $"-y -ss {FormatTs(start)} -to {FormatTs(end)} -i \"{input}\" " +
            $"{ReencodeArgsFor(output)} \"{output}\"",
            output, ct);
        if (reencode.Success)
            return reencode;

        return Fail(
            $"Stream-copy failed ({copy.Error}); re-encode failed ({reencode.Error})");
    }

    public async Task<FfmpegResult> DeleteMiddleAsync(
        string input,
        TimeSpan cutStart,
        TimeSpan cutEnd,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        if (cutEnd <= cutStart)
            return Fail("Cut end must be after cut start.");

        var dir = IoPath.GetDirectoryName(input)!;
        var part1 = IoPath.Combine(dir, $"_ns_part1_{Guid.NewGuid():N}.mp4");
        var part2 = IoPath.Combine(dir, $"_ns_part2_{Guid.NewGuid():N}.mp4");
        var list = IoPath.Combine(dir, $"_ns_concat_{Guid.NewGuid():N}.txt");
        var output = string.IsNullOrWhiteSpace(outputPath)
            ? SiblingOutput(input, "cut")
            : outputPath;

        try
        {
            var r1 = await RunAsync(
                $"-y -i \"{input}\" -ss 0 -to {FormatTs(cutStart)} -c copy \"{part1}\"", part1, ct);
            if (!r1.Success) return r1;

            var r2 = await RunAsync(
                $"-y -ss {FormatTs(cutEnd)} -i \"{input}\" -c copy \"{part2}\"", part2, ct);
            if (!r2.Success) return r2;

            await IoFile.WriteAllTextAsync(list,
                $"file '{part1.Replace("'", "'\\''")}'\nfile '{part2.Replace("'", "'\\''")}'\n", ct);

            return await RunAsync(
                $"-y -f concat -safe 0 -i \"{list}\" -c copy \"{output}\"", output, ct);
        }
        finally
        {
            TryDelete(part1);
            TryDelete(part2);
            TryDelete(list);
        }
    }

    public async Task<FfmpegResult> MergeAsync(IReadOnlyList<string> inputs, string outputPath, CancellationToken ct = default)
    {
        if (inputs.Count < 2)
            return Fail("Need at least two videos to merge.");

        var dir = IoPath.GetDirectoryName(outputPath)!;
        IoDirectory.CreateDirectory(dir);
        var list = IoPath.Combine(dir, $"_ns_merge_{Guid.NewGuid():N}.txt");

        try
        {
            var sb = new StringBuilder();
            foreach (var input in inputs)
                sb.AppendLine($"file '{input.Replace("'", "'\\''")}'");
            await IoFile.WriteAllTextAsync(list, sb.ToString(), ct);

            // Re-encode for safer merges across different codecs
            return await RunAsync(
                $"-y -f concat -safe 0 -i \"{list}\" {ReencodeArgsFor(outputPath)} \"{outputPath}\"",
                outputPath, ct);
        }
        finally
        {
            TryDelete(list);
        }
    }

    public async Task<FfmpegResult> ExportKeptSegmentsAsync(
        string input,
        IReadOnlyList<(TimeSpan Start, TimeSpan End)> kept,
        string? outputPath = null,
        IProgress<ExportProgressReport>? progress = null,
        double estDurationSec = 0,
        CancellationToken ct = default,
        bool fillGapsWithBlack = true)
    {
        if (kept.Count == 0)
            return Fail("Nothing to export — all clips deleted.");

        var output = string.IsNullOrWhiteSpace(outputPath)
            ? SiblingOutput(input, "edit")
            : outputPath;

        // Single segment → trim only
        if (kept.Count == 1)
        {
            progress?.Report(new ExportProgressReport { Percent = 5, Message = "Trimming…" });
            return await TrimAsync(input, kept[0].Start, kept[0].End, output, ct);
        }

        var dir = IoPath.GetDirectoryName(output)!;
        IoDirectory.CreateDirectory(dir);
        var parts = new List<string>();
        var list = IoPath.Combine(dir, $"_ns_edit_{Guid.NewGuid():N}.txt");
        var totalParts = kept.Count + (fillGapsWithBlack ? kept.Count - 1 : 0);

        try
        {
            var partIndex = 0;
            for (var i = 0; i < kept.Count; i++)
            {
                if (fillGapsWithBlack && i > 0)
                {
                    var gap = kept[i].Start - kept[i - 1].End;
                    if (gap > TimeSpan.FromMilliseconds(80))
                    {
                        var black = IoPath.Combine(dir, $"_ns_black_{Guid.NewGuid():N}.mp4");
                        partIndex++;
                        progress?.Report(new ExportProgressReport
                        {
                            Percent = 10 + 70.0 * partIndex / Math.Max(1, totalParts),
                            Message = $"Black filler {partIndex}/{totalParts}…"
                        });
                        var blackRes = await RunAsync(
                            $"-y -f lavfi -i color=c=black:s=1920x1080:d={gap.TotalSeconds:0.###} " +
                            $"-f lavfi -i anullsrc=r=44100:cl=stereo -shortest " +
                            $"-c:v libx264 -tune stillimage -pix_fmt yuv420p -c:a aac -t {gap.TotalSeconds:0.###} \"{black}\"",
                            black, ct);
                        if (blackRes.Success)
                            parts.Add(black);
                    }
                }

                var part = IoPath.Combine(dir, $"_ns_seg_{Guid.NewGuid():N}.mp4");
                partIndex++;
                progress?.Report(new ExportProgressReport
                {
                    Percent = 10 + 70.0 * partIndex / Math.Max(1, totalParts),
                    Message = $"Clip {i + 1}/{kept.Count}…"
                });
                var cut = await TrimAsync(input, kept[i].Start, kept[i].End, part, ct);
                if (!cut.Success)
                    return cut;
                parts.Add(part);
            }

            var sb = new StringBuilder();
            foreach (var p in parts)
                sb.AppendLine($"file '{p.Replace("'", "'\\''")}'");
            await IoFile.WriteAllTextAsync(list, sb.ToString(), ct);

            progress?.Report(new ExportProgressReport { Percent = 85, Message = "Merging clips…" });
            return await RunAsync(
                $"-y -f concat -safe 0 -i \"{list}\" {ReencodeArgsFor(output)} \"{output}\"",
                output, progress, estDurationSec > 0 ? estDurationSec : null, ct);
        }
        finally
        {
            foreach (var p in parts)
                TryDelete(p);
            TryDelete(list);
        }
    }

    /// <summary>
    /// Export a multi-track TimelineProject via filter_complex (fades, speed, overlays, LUT, amix).
    /// Falls back to ExportKeptSegments when the project is a simple single-source keep list.
    /// </summary>
    public async Task<FfmpegResult> ExportTimelineAsync(
        Timeline.TimelineProject project,
        string? outputPath = null,
        IProgress<ExportProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(project.PrimarySourcePath) || !IoFile.Exists(project.PrimarySourcePath))
            return Fail("No primary source for timeline export.");

        var kept = project.ToKeptRanges();
        var vTrack = project.VideoTrack;
        var hasEffects = vTrack?.Clips.Any(c =>
            c.Speed != 1.0 || c.FadeIn > TimeSpan.Zero || c.FadeOut > TimeSpan.Zero ||
            c.TransitionIn != Timeline.TransitionKind.None || c.TransitionOut != Timeline.TransitionKind.None ||
            !string.IsNullOrWhiteSpace(c.LutPath)) == true;
        var hasOverlay = project.OverlayTrack?.Clips.Count > 0;
        var hasLut = !string.IsNullOrWhiteSpace(project.GlobalLutPath) && IoFile.Exists(project.GlobalLutPath);
        var a2 = project.Tracks.FirstOrDefault(t => t.Name == "A2");
        var hasA2 = a2?.Clips.Count > 0;

        var output = string.IsNullOrWhiteSpace(outputPath)
            ? SiblingOutput(project.PrimarySourcePath, "timeline")
            : outputPath;

        var estDuration = project.SourceDuration.TotalSeconds;
        if (estDuration <= 0 && kept.Count > 0)
            estDuration = kept.Sum(r => (r.End - r.Start).TotalSeconds);
        progress?.Report(new ExportProgressReport { Percent = 2, Message = "Preparing export…" });

        if (!hasEffects && !hasOverlay && !hasLut && !hasA2 && kept.Count > 0)
        {
            var simple = await ExportKeptSegmentsAsync(
                project.PrimarySourcePath, kept, output, progress, estDuration, ct,
                fillGapsWithBlack: project.FillGapsWithBlack);
            if (!simple.Success)
                return simple;
            var presetSimple = await ApplyExportPresetAsync(output, project.ExportPreset, ct);
            if (!presetSimple.Success)
                return presetSimple;
            return await BurnSubtitlesIfNeededAsync(presetSimple.OutputPath ?? output, project.SubtitleBurnPath, ct);
        }

        // Build filter graph for V1 clips with optional fade/speed, then scale for preset + lut
        if (vTrack is null || vTrack.Clips.Count == 0)
            return Fail("No video clips on V1.");

        var dir = IoPath.GetDirectoryName(output)!;
        IoDirectory.CreateDirectory(dir);
        var parts = new List<string>();
        var list = IoPath.Combine(dir, $"_ns_tl_{Guid.NewGuid():N}.txt");
        string? concatTempPath = null;

        try
        {
            for (var i = 0; i < vTrack.Clips.Count; i++)
            {
                var clip = vTrack.Clips[i];
                if (clip.Overlay != Timeline.OverlayKind.None)
                    continue;

                var src = string.IsNullOrWhiteSpace(clip.SourcePath) ? project.PrimarySourcePath : clip.SourcePath;
                var part = IoPath.Combine(dir, $"_ns_tlseg_{Guid.NewGuid():N}.mp4");
                var vf = new StringBuilder();
                if (clip.Speed > 0 && Math.Abs(clip.Speed - 1.0) > 0.01)
                    vf.Append($"setpts={1.0 / clip.Speed:0.####}*PTS");
                if (clip.FadeIn > TimeSpan.Zero)
                {
                    if (vf.Length > 0) vf.Append(',');
                    vf.Append($"fade=t=in:st=0:d={clip.FadeIn.TotalSeconds:0.###}");
                }

                if (clip.FadeOut > TimeSpan.Zero)
                {
                    var dur = (clip.SourceOut - clip.SourceIn).TotalSeconds / Math.Max(0.01, clip.Speed);
                    var st = Math.Max(0, dur - clip.FadeOut.TotalSeconds);
                    if (vf.Length > 0) vf.Append(',');
                    vf.Append($"fade=t=out:st={st:0.###}:d={clip.FadeOut.TotalSeconds:0.###}");
                }

                if (!string.IsNullOrWhiteSpace(clip.LutPath) && IoFile.Exists(clip.LutPath))
                {
                    if (vf.Length > 0) vf.Append(',');
                    vf.Append($"lut3d='{clip.LutPath.Replace("\\", "/").Replace(":", "\\:")}'");
                }

                var vfArg = vf.Length > 0 ? $"-vf \"{vf}\"" : "";
                var af = clip.Speed > 0 && Math.Abs(clip.Speed - 1.0) > 0.01
                    ? BuildAtempoChain(clip.Speed)
                    : "";
                var afArg = string.IsNullOrEmpty(af) ? "" : $"-af \"{af}\"";

                var cut = await RunAsync(
                    $"-y -ss {FormatTs(clip.SourceIn)} -to {FormatTs(clip.SourceOut)} -i \"{src}\" " +
                    $"{vfArg} {afArg} -c:v libx264 -preset veryfast -crf 23 -c:a aac -movflags +faststart \"{part}\"",
                    part, ct);
                if (!cut.Success)
                    return cut;
                parts.Add(part);
            }

            // Generated overlays as color clips inserted by timeline order — burn after concat for simplicity
            if (parts.Count == 0)
                return Fail("No exportable video clips.");

            string concatPath;
            if (parts.Count == 1)
            {
                concatPath = parts[0];
            }
            else
            {
                concatPath = IoPath.Combine(dir, $"_ns_tlcat_{Guid.NewGuid():N}.mp4");
                concatTempPath = concatPath;
                var sb = new StringBuilder();
                foreach (var p in parts)
                    sb.AppendLine($"file '{p.Replace("'", "'\\''")}'");
                await IoFile.WriteAllTextAsync(list, sb.ToString(), ct);
                var cat = await RunAsync(
                    $"-y -f concat -safe 0 -i \"{list}\" -c:v libx264 -preset veryfast -crf 23 -c:a aac -movflags +faststart \"{concatPath}\"",
                    concatPath, ct);
                if (!cat.Success)
                    return cat;
            }

            // Text / black / white overlays
            var current = concatPath;
            if (project.OverlayTrack is { Clips.Count: > 0 } ov)
            {
                foreach (var o in ov.Clips.OrderBy(c => c.TimelineStart))
                {
                    var next = IoPath.Combine(dir, $"_ns_ov_{Guid.NewGuid():N}.mp4");
                    FfmpegResult r;
                    if (o.Overlay == Timeline.OverlayKind.Text && !string.IsNullOrWhiteSpace(o.Text))
                    {
                        var escaped = o.Text.Replace("\\", "\\\\").Replace(":", "\\:").Replace("'", "\\'");
                        r = await RunAsync(
                            $"-y -i \"{current}\" -vf \"drawtext=text='{escaped}':fontsize=48:fontcolor=white:x=(w-text_w)/2:y=(h-text_h)/2:enable='between(t,{o.TimelineStart.TotalSeconds:0.###},{(o.TimelineStart + o.Duration).TotalSeconds:0.###})'\" " +
                            $"-c:v libx264 -preset veryfast -crf 23 -c:a copy -movflags +faststart \"{next}\"",
                            next, ct);
                    }
                    else if (o.Overlay is Timeline.OverlayKind.Black or Timeline.OverlayKind.White)
                    {
                        var color = o.Overlay == Timeline.OverlayKind.Black ? "black" : "white";
                        var opacity = 0.85;
                        r = await RunAsync(
                            $"-y -i \"{current}\" -f lavfi -i color=c={color}:s=1280x720:d={o.Duration.TotalSeconds:0.###} " +
                            $"-filter_complex \"[1:v]format=yuva420p,colorchannelmixer=aa={opacity:0.##}[ov];[0:v][ov]overlay=0:0:enable='between(t,{o.TimelineStart.TotalSeconds:0.###},{(o.TimelineStart + o.Duration).TotalSeconds:0.###})'\" " +
                            $"-c:v libx264 -preset veryfast -crf 23 -c:a copy -movflags +faststart \"{next}\"",
                            next, ct);
                    }
                    else
                        continue;

                    if (!r.Success)
                        return r;
                    if (current != concatPath && current != parts[0])
                        TryDelete(current);
                    current = next;
                }
            }

            if (hasLut && project.GlobalLutPath is not null)
            {
                var lutOut = IoPath.Combine(dir, $"_ns_lut_{Guid.NewGuid():N}.mp4");
                var lutPath = project.GlobalLutPath.Replace("\\", "/").Replace(":", "\\:");
                var lut = await RunAsync(
                    $"-y -i \"{current}\" -vf \"lut3d='{lutPath}'\" -c:v libx264 -preset veryfast -crf 23 -c:a copy -movflags +faststart \"{lutOut}\"",
                    lutOut, ct);
                if (!lut.Success)
                    return lut;
                if (current != concatPath)
                    TryDelete(current);
                current = lutOut;
            }

            // Move/transcode to final container then apply preset scale
            if (!string.Equals(current, output, StringComparison.OrdinalIgnoreCase))
            {
                var finalize = await FinalizeToOutputAsync(current, output, ct);
                if (!finalize.Success)
                    return finalize;
                if (current != concatPath && (parts.Count == 0 || current != parts[0]))
                    TryDelete(current);
            }

            var presetResult = await ApplyExportPresetAsync(output, project.ExportPreset, ct);
            if (!presetResult.Success)
                return presetResult;

            return await BurnSubtitlesIfNeededAsync(presetResult.OutputPath ?? output, project.SubtitleBurnPath, ct);
        }
        finally
        {
            foreach (var p in parts)
                TryDelete(p);
            TryDelete(list);
            if (concatTempPath is not null)
                TryDelete(concatTempPath);
        }
    }

    /// <summary>Burn SRT into video with ffmpeg subtitles filter (Windows path escaped).</summary>
    public async Task<FfmpegResult> BurnSubtitlesIfNeededAsync(
        string videoPath,
        string? srtPath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(srtPath) || !IoFile.Exists(srtPath))
            return new FfmpegResult { Success = true, OutputPath = videoPath };
        if (string.IsNullOrWhiteSpace(videoPath) || !IoFile.Exists(videoPath))
            return Fail("Video missing for subtitle burn-in.");

        var burned = SiblingOutput(videoPath, "subs");
        // ffmpeg subtitles= needs forward slashes and escaped drive colon
        var escaped = srtPath.Replace('\\', '/').Replace(":", "\\:");
        var r = await RunAsync(
            $"-y -i \"{videoPath}\" -vf \"subtitles='{escaped}'\" " +
            $"-c:v libx264 -preset veryfast -crf 23 -c:a copy -movflags +faststart \"{burned}\"",
            burned, ct);
        if (!r.Success)
            return r;

        TryDelete(videoPath);
        var finalize = await FinalizeToOutputAsync(burned, videoPath, ct);
        TryDelete(burned);
        if (!finalize.Success)
            return finalize;

        AppLogger.Info($"Burned subtitles → {videoPath}");
        return new FfmpegResult { Success = true, OutputPath = videoPath };
    }

    public async Task<FfmpegResult> ApplyExportPresetAsync(
        string inputPath,
        Timeline.ExportPreset preset,
        CancellationToken ct = default)
    {
        if (preset == Timeline.ExportPreset.MatchSource)
            return new FfmpegResult { Success = true, OutputPath = inputPath };

        var (w, h) = preset == Timeline.ExportPreset.Uhd4K ? (3840, 2160) : (1920, 1080);
        var scaled = SiblingOutput(inputPath, preset == Timeline.ExportPreset.Uhd4K ? "4k" : "1080");
        var r = await RunAsync(
            $"-y -i \"{inputPath}\" -vf \"scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2\" " +
            $"-c:v libx264 -preset veryfast -crf 20 -c:a aac -movflags +faststart \"{scaled}\"",
            scaled, ct);
        if (!r.Success)
            return r;

        TryDelete(inputPath);
        var finalize = await FinalizeToOutputAsync(scaled, inputPath, ct);
        TryDelete(scaled);
        if (!finalize.Success)
            return finalize;

        return new FfmpegResult { Success = true, OutputPath = inputPath };
    }

    private static string BuildAtempoChain(double speed)
    {
        // atempo only accepts 0.5–2.0; chain for wider range
        var factors = new List<double>();
        var s = speed;
        while (s > 2.0)
        {
            factors.Add(2.0);
            s /= 2.0;
        }

        while (s < 0.5)
        {
            factors.Add(0.5);
            s /= 0.5;
        }

        factors.Add(s);
        return string.Join(",", factors.Select(f => $"atempo={f:0.####}"));
    }

    public async Task<string?> EnsurePlayablePreviewAsync(string input, CancellationToken ct = default)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(input) || !IoFile.Exists(input))
            return null;

        var cacheDir = AppResourceBudget.VideoPreviewCache;
        IoDirectory.CreateDirectory(cacheDir);
        var stamp = new System.IO.FileInfo(input).LastWriteTimeUtc.Ticks;
        var output = IoPath.Combine(cacheDir, $"{IoPath.GetFileNameWithoutExtension(input)}_{stamp}_h264.mp4");
        if (IoFile.Exists(output) && new System.IO.FileInfo(output).Length > 1000)
            return output;

        // Universal decode → H.264/AAC MP4 that WPF MediaElement can play (covers HEVC/MOV/etc.)
        var args =
            $"-y -i \"{input}\" -map 0:v:0 -map 0:a:0? -c:v libx264 -preset veryfast -crf 23 " +
            $"-pix_fmt yuv420p -c:a aac -ac 2 -movflags +faststart \"{output}\"";
        var result = await RunAsync(args, output, ct);
        return result.Success ? output : null;
    }

    /// <summary>Public ffmpeg runner for helpers (whisper audio extract, etc.).</summary>
    public async Task<bool> RunPublicAsync(string args, string outputPath, CancellationToken ct = default)
    {
        var r = await RunAsync(args, outputPath, ct);
        return r.Success;
    }

    private async Task<FfmpegResult> RunAsync(string args, string outputPath, CancellationToken ct) =>
        await RunAsync(args, outputPath, progress: null, durationSec: null, ct);

    private async Task<FfmpegResult> RunAsync(
        string args,
        string outputPath,
        IProgress<ExportProgressReport>? progress,
        double? durationSec,
        CancellationToken ct)
    {
        if (!IsAvailable)
            return Fail("FFmpeg not found — run scripts/download-ffmpeg.ps1");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath!,
                Arguments = args,
                WorkingDirectory = IoPath.GetDirectoryName(outputPath) ?? EnvLoader.FindProjectRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start ffmpeg.");

            var stderr = new StringBuilder();
            var readTask = Task.Run(async () =>
            {
                try
                {
                    while (await process.StandardError.ReadLineAsync(ct) is { } line)
                    {
                        stderr.AppendLine(line);
                        if (progress is null || durationSec is null or <= 0)
                            continue;
                        var t = TryParseFfmpegTime(line);
                        if (t.HasValue)
                        {
                            var pct = Math.Clamp(t.Value.TotalSeconds / durationSec.Value * 100, 0, 99);
                            progress.Report(new ExportProgressReport
                            {
                                Percent = pct,
                                Message = $"Encoding… {pct:0}%"
                            });
                        }
                    }
                }
                catch (OperationCanceledException) { }
            }, ct);

            await process.WaitForExitAsync(ct);
            await readTask;

            if (process.ExitCode != 0 || !IoFile.Exists(outputPath))
                return Fail(Trim(stderr.ToString()));

            progress?.Report(new ExportProgressReport { Percent = 100, Message = "Done" });
            return new FfmpegResult { Success = true, OutputPath = outputPath };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static TimeSpan? TryParseFfmpegTime(string line)
    {
        var idx = line.IndexOf("time=", StringComparison.Ordinal);
        if (idx < 0)
            return null;
        var rest = line[(idx + 5)..].Trim();
        var end = rest.IndexOfAny([' ', '\r', '\n']);
        var token = end > 0 ? rest[..end] : rest;
        if (TimeSpan.TryParse(token, out var ts))
            return ts;
        return null;
    }

    private static FfmpegResult Fail(string error) => new() { Success = false, Error = error };

    /// <summary>Re-encode flags matched to the destination file extension (muxer + codecs).</summary>
    private static string ReencodeArgsFor(string outputPath)
    {
        return IoPath.GetExtension(outputPath).ToLowerInvariant() switch
        {
            ".webm" => "-c:v libvpx-vp9 -crf 31 -b:v 0 -c:a libopus",
            ".mkv" => "-c:v libx264 -preset veryfast -crf 23 -c:a aac",
            ".mov" => "-c:v libx264 -preset veryfast -crf 23 -c:a aac -movflags +faststart",
            ".avi" => "-c:v libx264 -preset veryfast -crf 23 -c:a mp3",
            _ => "-c:v libx264 -preset veryfast -crf 23 -c:a aac -movflags +faststart"
        };
    }

    /// <summary>Write intermediate H.264 MP4 to the user-chosen output path (copy or transcode).</summary>
    private async Task<FfmpegResult> FinalizeToOutputAsync(
        string intermediatePath,
        string outputPath,
        CancellationToken ct)
    {
        TryDelete(outputPath);
        var ext = IoPath.GetExtension(outputPath).ToLowerInvariant();
        if (ext is ".mp4" or ".m4v")
        {
            IoFile.Copy(intermediatePath, outputPath, overwrite: true);
            return new FfmpegResult { Success = true, OutputPath = outputPath };
        }

        if (ext is ".mkv" or ".mov")
        {
            return await RunAsync(
                $"-y -i \"{intermediatePath}\" -c copy \"{outputPath}\"",
                outputPath, ct);
        }

        return await RunAsync(
            $"-y -i \"{intermediatePath}\" {ReencodeArgsFor(outputPath)} \"{outputPath}\"",
            outputPath, ct);
    }

    private static string SiblingOutput(string input, string tag)
    {
        var dir = IoPath.GetDirectoryName(input)!;
        var name = IoPath.GetFileNameWithoutExtension(input);
        return IoPath.Combine(dir, $"{name}_{tag}_{DateTime.Now:HHmmss}.mp4");
    }

    private static string FormatTs(TimeSpan t) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";

    private static string Trim(string s) =>
        s.Length > 500 ? s[^500..] : s;

    private static void TryDelete(string path)
    {
        try { if (IoFile.Exists(path)) IoFile.Delete(path); } catch { /* ignore */ }
    }

    private static string? ResolveFfmpeg()
    {
        var root = EnvLoader.FindProjectRoot();
        var bundled = IoPath.Combine(root, "tools", "ffmpeg", "ffmpeg.exe");
        if (IoFile.Exists(bundled))
            return bundled;

        var baseBundled = IoPath.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");
        if (IoFile.Exists(baseBundled))
            return baseBundled;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "ffmpeg",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            var output = p?.StandardOutput.ReadToEnd() ?? "";
            p?.WaitForExit(3000);
            var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(line) && IoFile.Exists(line))
                return line;
        }
        catch
        {
            // ignore
        }

        return null;
    }
}
