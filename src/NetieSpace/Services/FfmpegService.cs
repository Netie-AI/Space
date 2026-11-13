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

public sealed class ExportProgressReport
{
    public double Percent { get; init; }
    public string Message { get; init; } = "";
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

    public async Task<FfmpegResult> MergeAsync(IReadOnlyList<string> inputs, string outputPath, CancellationToken ct = default)
    {
        if (inputs.Count < 2)
            return Fail("Need at least two videos to merge.");

        var dir = IoPath.GetDirectoryName(outputPath)!;
        IoDirectory.CreateDirectory(dir);
        var list = IoPath.Combine(dir, $"_ns_merge_{Guid.NewGuid():N}.txt");
        var copies = new List<string>();

        try
        {
            var sb = new StringBuilder();
            var listDir = IoPath.GetFullPath(dir);
            foreach (var input in inputs)
            {
                var local = input;
                var inputDir = IoPath.GetDirectoryName(input);
                var sameDir = !string.IsNullOrWhiteSpace(inputDir) &&
                    string.Equals(IoPath.GetFullPath(inputDir), listDir, StringComparison.OrdinalIgnoreCase);
                if (!sameDir)
                {
                    local = IoPath.Combine(dir, $"_ns_mrg_{Guid.NewGuid():N}{IoPath.GetExtension(input)}");
                    IoFile.Copy(input, local, overwrite: true);
                    copies.Add(local);
                }

                sb.AppendLine(FfmpegExportGraph.ConcatDemuxerLine(local));
            }
            await IoFile.WriteAllTextAsync(list, sb.ToString(), ct);

            // Re-encode for safer merges across different codecs
            return await RunAsync(
                $"-y -f concat -safe 0 -i \"{list}\" {ReencodeArgsFor(outputPath)} \"{outputPath}\"",
                outputPath, ct);
        }
        finally
        {
            foreach (var c in copies)
                TryDelete(c);
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
                sb.AppendLine(FfmpegExportGraph.ConcatDemuxerLine(p));
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

        // UI packed time ignores wipe. PreviewWindow clones before this
        // call; remap captions/OV so clip-2 text still sits on clip 2.
        project.ApplyWipeExportRemap();

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

        var estDuration = project.ExportDuration().TotalSeconds;
        if (estDuration <= 0)
            estDuration = vTrack?.Clips.Sum(c => c.Duration.TotalSeconds) ?? 0;
        if (estDuration <= 0)
            estDuration = project.SourceDuration.TotalSeconds;
        if (estDuration <= 0 && kept.Count > 0)
            estDuration = kept.Sum(r => (r.End - r.Start).TotalSeconds);
        progress?.Report(new ExportProgressReport { Percent = 2, Message = "Preparing export…" });

        var hasMultipleSources = project.HasMultipleVideoSources;
        // Single-source keep list can stream-copy/concat. Mixed files must use per-clip sources
        // so a second/third V1 clip is not exported from the primary path.
        if (!hasEffects && !hasOverlay && !hasLut && !hasA2 && !hasMultipleSources && kept.Count > 0)
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
        var exportClips = new List<Timeline.TimelineClip>();
        var list = IoPath.Combine(dir, $"_ns_tl_{Guid.NewGuid():N}.txt");
        string? concatTempPath = null;
        string? padTempPath = null;
        var joinTemps = new List<string>();

        try
        {
            for (var i = 0; i < vTrack.Clips.Count; i++)
            {
                var clip = vTrack.Clips[i];
                if (clip.Overlay != Timeline.OverlayKind.None)
                    continue;

                var src = string.IsNullOrWhiteSpace(clip.SourcePath) ? project.PrimarySourcePath : clip.SourcePath;
                if (Timeline.TimelineProject.IsBlackPlaceholder(clip.SourcePath) ||
                    Timeline.TimelineProject.IsBlackPlaceholder(src))
                {
                    var black = IoPath.Combine(dir, $"_ns_hole_{Guid.NewGuid():N}.mp4");
                    var dur = clip.Duration.TotalSeconds;
                    if (dur < 0.04)
                        dur = 1;
                    var w = project.Width > 0 ? project.Width : 1920;
                    var h = project.Height > 0 ? project.Height : 1080;
                    var blackRes = await RunAsync(
                        $"-y -f lavfi -i color=c=black:s={w}x{h}:d={dur:0.###} " +
                        $"-f lavfi -i anullsrc=r=44100:cl=stereo -shortest " +
                        $"-c:v libx264 -tune stillimage -pix_fmt yuv420p -c:a aac -t {dur:0.###} \"{black}\"",
                        black, ct);
                    if (!blackRes.Success)
                        return blackRes;
                    parts.Add(black);
                    exportClips.Add(clip);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(src) || !IoFile.Exists(src))
                    return Fail($"Missing source for V1 clip: {src}");
                var part = IoPath.Combine(dir, $"_ns_tlseg_{Guid.NewGuid():N}.mp4");
                var vf = new StringBuilder(FfmpegExportGraph.ClipVideoFilters(
                    clip, hasMultipleSources, project.Width, project.Height));

                string? lutTemp = null;
                if (!string.IsNullOrWhiteSpace(clip.LutPath) && IoFile.Exists(clip.LutPath))
                {
                    // lut3d strips ' the same way subtitles= does (Mom's clips -> Moms).
                    lutTemp = CopyFilterFileToTemp(clip.LutPath, "lut");
                    if (vf.Length > 0) vf.Append(',');
                    vf.Append($"lut3d='{EscapeFilterPath(lutTemp)}'");
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
                if (lutTemp is not null)
                    TryDelete(lutTemp);
                if (!cut.Success)
                    return cut;
                parts.Add(part);
                exportClips.Add(clip);
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
                var joined = await JoinVideoPartsAsync(
                    parts, exportClips, list, concatPath,
                    project.Width, project.Height, joinTemps, ct);
                if (!joined.Success)
                    return joined;
            }

            // Text / black / white overlays
            var current = concatPath;
            var pad = project.BlackPadAfterVideo();
            if (pad > TimeSpan.FromMilliseconds(40))
            {
                // Last-clip Leave black parks the title card past remaining
                // V1. Overlay enable after EOF never painted, so Export
                // dropped the card (1.6s leftover, picture not black).
                var padded = IoPath.Combine(dir, $"_ns_pad_{Guid.NewGuid():N}.mp4");
                padTempPath = padded;
                var vf = FfmpegExportGraph.PadBlackAfterVideo(pad.TotalSeconds);
                var total = project.ExportDuration().TotalSeconds;
                var padWithAudio = await RunAsync(
                    $"-y -i \"{current}\" -vf \"{vf}\" -af \"apad=pad_dur={pad.TotalSeconds:0.###}\" " +
                    $"-t {total:0.###} -c:v libx264 -preset veryfast -crf 23 -c:a aac -movflags +faststart \"{padded}\"",
                    padded, ct);
                var padRes = padWithAudio;
                if (!padRes.Success)
                {
                    padRes = await RunAsync(
                        $"-y -i \"{current}\" -vf \"{vf}\" -an " +
                        $"-t {total:0.###} -c:v libx264 -preset veryfast -crf 23 -movflags +faststart \"{padded}\"",
                        padded, ct);
                }

                if (!padRes.Success)
                    return padRes;
                current = padded;
            }

            if (project.OverlayTrack is { Clips.Count: > 0 } ov)
            {
                foreach (var o in ov.Clips.OrderBy(c => c.TimelineStart))
                {
                    var next = IoPath.Combine(dir, $"_ns_ov_{Guid.NewGuid():N}.mp4");
                    FfmpegResult r;
                    if (o.Overlay == Timeline.OverlayKind.Text && !string.IsNullOrWhiteSpace(o.Text))
                    {
                        r = await BurnTextOverlayAsync(current, next, o, ct);
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
                var lutTemp = CopyFilterFileToTemp(project.GlobalLutPath, "lut");
                var lutPath = EscapeFilterPath(lutTemp);
                var lut = await RunAsync(
                    $"-y -i \"{current}\" -vf \"lut3d='{lutPath}'\" -c:v libx264 -preset veryfast -crf 23 -c:a copy -movflags +faststart \"{lutOut}\"",
                    lutOut, ct);
                TryDelete(lutTemp);
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
            foreach (var p in joinTemps)
                TryDelete(p);
            TryDelete(list);
            if (concatTempPath is not null)
                TryDelete(concatTempPath);
            if (padTempPath is not null)
                TryDelete(padTempPath);
        }
    }

    /// <summary>
    /// Concat packed V1 parts, using ffmpeg xfade wipeleft at Wipe junctions.
    /// Hard cuts still use the concat demuxer.
    /// </summary>
    private async Task<FfmpegResult> JoinVideoPartsAsync(
        IReadOnlyList<string> parts,
        IReadOnlyList<Timeline.TimelineClip> clips,
        string concatListPath,
        string outputPath,
        int width,
        int height,
        List<string> temps,
        CancellationToken ct)
    {
        var hasXfade = false;
        for (var i = 0; i < clips.Count - 1 && i < parts.Count - 1; i++)
        {
            if (Timeline.TimelineProject.XfadeJoinDuration(clips[i], clips[i + 1]) > TimeSpan.Zero)
            {
                hasXfade = true;
                break;
            }
        }

        if (!hasXfade)
        {
            var sb = new StringBuilder();
            foreach (var p in parts)
                sb.AppendLine(FfmpegExportGraph.ConcatDemuxerLine(p));
            await IoFile.WriteAllTextAsync(concatListPath, sb.ToString(), ct);
            return await RunAsync(
                $"-y -f concat -safe 0 -i \"{concatListPath}\" -c:v libx264 -preset veryfast -crf 23 -c:a aac -movflags +faststart \"{outputPath}\"",
                outputPath, ct);
        }

        var current = parts[0];
        var currentDur = clips[0].Duration;
        var dir = IoPath.GetDirectoryName(outputPath)!;
        for (var i = 1; i < parts.Count; i++)
        {
            var nextPath = i == parts.Count - 1
                ? outputPath
                : IoPath.Combine(dir, $"_ns_xfade_{Guid.NewGuid():N}.mp4");
            var xfadeDur = i - 1 < clips.Count - 1
                ? Timeline.TimelineProject.XfadeJoinDuration(clips[i - 1], clips[i])
                : TimeSpan.Zero;

            FfmpegResult join;
            if (xfadeDur > TimeSpan.Zero)
            {
                join = await XfadeJoinAsync(current, parts[i], currentDur, xfadeDur, nextPath, width, height, ct);
                currentDur += clips[i].Duration - xfadeDur;
            }
            else
            {
                join = await ConcatTwoAsync(current, parts[i], concatListPath, nextPath, ct);
                currentDur += clips[i].Duration;
            }

            if (!join.Success)
                return join;

            if (current != parts[0] && current != outputPath)
                TryDelete(current);
            current = nextPath;
            if (nextPath != outputPath)
                temps.Add(nextPath);
        }

        return new FfmpegResult { Success = true, OutputPath = outputPath };
    }

    private async Task<FfmpegResult> ConcatTwoAsync(
        string left, string right, string listPath, string outputPath, CancellationToken ct)
    {
        await IoFile.WriteAllTextAsync(listPath,
            $"{FfmpegExportGraph.ConcatDemuxerLine(left)}\n{FfmpegExportGraph.ConcatDemuxerLine(right)}\n", ct);
        return await RunAsync(
            $"-y -f concat -safe 0 -i \"{listPath}\" -c:v libx264 -preset veryfast -crf 23 -c:a aac -movflags +faststart \"{outputPath}\"",
            outputPath, ct);
    }

    private async Task<FfmpegResult> XfadeJoinAsync(
        string left,
        string right,
        TimeSpan leftDuration,
        TimeSpan xfadeDur,
        string outputPath,
        int width,
        int height,
        CancellationToken ct)
    {
        var withAudio = FfmpegExportGraph.XfadeWithAudio(leftDuration, xfadeDur, width, height);
        var r = await RunAsync(
            $"-y -i \"{left}\" -i \"{right}\" -filter_complex \"{withAudio}\" -map \"[vout]\" -map \"[aout]\" " +
            $"-c:v libx264 -preset veryfast -crf 23 -c:a aac -movflags +faststart \"{outputPath}\"",
            outputPath, ct);
        if (r.Success)
            return r;

        var videoOnly = FfmpegExportGraph.XfadeVideoOnly(leftDuration, xfadeDur, width, height);
        return await RunAsync(
            $"-y -i \"{left}\" -i \"{right}\" -filter_complex \"{videoOnly}\" -map \"[vout]\" -an " +
            $"-c:v libx264 -preset veryfast -crf 23 -movflags +faststart \"{outputPath}\"",
            outputPath, ct);
    }

    /// <summary>
    /// Burn overlay text onto the canvas. Prefer drawtext with a system font;
    /// fall back to a timed SRT so export still gets burned-in glyphs.
    /// </summary>
    private async Task<FfmpegResult> BurnTextOverlayAsync(
        string input,
        string output,
        Timeline.TimelineClip overlay,
        CancellationToken ct)
    {
        // drawtext/subtitles strip apostrophes. Export to Mom's clips used
        // to write these next to the output and fail after #113 only fixed
        // the whisper sidecar path.
        var dir = IoPath.GetTempPath();
        var textFile = IoPath.Combine(dir, $"_ns_txt_{Guid.NewGuid():N}.txt");
        try
        {
            await IoFile.WriteAllTextAsync(textFile, overlay.Text ?? "", ct);
            var start = overlay.TimelineStart.TotalSeconds;
            var end = (overlay.TimelineStart + overlay.Duration).TotalSeconds;
            var font = ResolveDrawtextFont();
            var fontArg = font is null ? "" : $":fontfile='{EscapeFilterPath(font)}'";
            var vf =
                $"drawtext=textfile='{EscapeFilterPath(textFile)}'{fontArg}:fontsize=48:fontcolor=white:" +
                $"borderw=2:bordercolor=black@0.65:x=(w-text_w)/2:y=(h-text_h)/2:" +
                $"enable='between(t,{start:0.###},{end:0.###})'";
            var r = await RunAsync(
                $"-y -i \"{input}\" -vf \"{vf}\" -c:v libx264 -preset veryfast -crf 23 -c:a copy -movflags +faststart \"{output}\"",
                output, ct);
            if (r.Success)
                return r;

            var srt = IoPath.Combine(dir, $"_ns_ovtxt_{Guid.NewGuid():N}.srt");
            try
            {
                await IoFile.WriteAllTextAsync(srt, ToTimedSrt(overlay.Text ?? "", overlay.TimelineStart, overlay.Duration), ct);
                var escaped = EscapeFilterPath(srt);
                return await RunAsync(
                    $"-y -i \"{input}\" -vf \"subtitles='{escaped}'\" " +
                    $"-c:v libx264 -preset veryfast -crf 23 -c:a copy -movflags +faststart \"{output}\"",
                    output, ct);
            }
            finally
            {
                TryDelete(srt);
            }
        }
        finally
        {
            TryDelete(textFile);
        }
    }

    private static string ToTimedSrt(string text, TimeSpan start, TimeSpan duration)
    {
        var end = start + duration;
        var body = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return $"1\n{FormatSrtTs(start)} --> {FormatSrtTs(end)}\n{body}\n";
    }

    private static string FormatSrtTs(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
            t = TimeSpan.Zero;
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}";
    }

    private static string EscapeFilterPath(string path) =>
        FfmpegExportGraph.EscapeFilterPath(path);

    /// <summary>
    /// ffmpeg lut3d/subtitles strip apostrophes. Copy next to %TEMP% so a
    /// folder like Mom's clips does not open as Moms.
    /// </summary>
    private static string CopyFilterFileToTemp(string path, string kind)
    {
        var ext = IoPath.GetExtension(path);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".bin";
        var safe = IoPath.Combine(IoPath.GetTempPath(), $"_ns_{kind}_{Guid.NewGuid():N}{ext}");
        IoFile.Copy(path, safe, overwrite: true);
        return safe;
    }

    private static string? ResolveDrawtextFont()
    {
        var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string[] candidates =
        [
            IoPath.Combine(fontsDir, "arial.ttf"),
            IoPath.Combine(fontsDir, "segoeui.ttf"),
            IoPath.Combine(fontsDir, "calibri.ttf"),
            @"C:\Windows\Fonts\arial.ttf",
            @"C:\Windows\Fonts\segoeui.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
            "/usr/share/fonts/truetype/freefont/FreeSans.ttf"
        ];
        return candidates.FirstOrDefault(IoFile.Exists);
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
        // subtitles= strips ' even with \'. A sidecar under Mom's clips
        // must be copied to a quote-free temp, then colon-escaped.
        var safeSrt = IoPath.Combine(IoPath.GetTempPath(), $"_ns_burn_{Guid.NewGuid():N}.srt");
        IoFile.Copy(srtPath, safeSrt, overwrite: true);
        try
        {
            var escaped = FfmpegExportGraph.EscapeFilterPath(safeSrt);
            var r = await RunAsync(
                $"-y -i \"{videoPath}\" -vf \"subtitles='{escaped}'\" " +
                $"-c:v libx264 -preset veryfast -crf 23 -c:a copy -movflags +faststart \"{burned}\"",
                burned, ct);
            if (!r.Success)
                return r;
        }
        finally
        {
            TryDelete(safeSrt);
        }

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

        var cacheDir = IoPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetieSpace", "cache", "video-preview");
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

    /// <summary>Read media duration for packing a second/third clip onto V1.</summary>
    public async Task<TimeSpan?> ProbeDurationAsync(string input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input) || !IoFile.Exists(input))
            return null;

        var probe = ResolveFfprobe();
        if (!string.IsNullOrWhiteSpace(probe))
        {
            var secondsText = await RunTextAsync(
                probe,
                $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{input}\"",
                ct);
            if (double.TryParse(
                    secondsText?.Trim(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var sec) &&
                sec > 0 && !double.IsNaN(sec) && !double.IsInfinity(sec))
                return TimeSpan.FromSeconds(sec);
        }

        if (!IsAvailable)
            return null;

        var stderr = await RunTextAsync(_ffmpegPath!, $"-hide_banner -i \"{input}\"", ct);
        if (string.IsNullOrWhiteSpace(stderr))
            return null;
        var idx = stderr.IndexOf("Duration:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;
        var rest = stderr[(idx + 9)..].TrimStart();
        var end = rest.IndexOfAny([',', ' ', '\r', '\n']);
        var token = end > 0 ? rest[..end] : rest;
        if (TimeSpan.TryParse(token, System.Globalization.CultureInfo.InvariantCulture, out var ts) &&
            ts > TimeSpan.Zero)
            return ts;
        return null;
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
                WorkingDirectory = IoPath.GetDirectoryName(outputPath) ?? ProjectRoot.Find(),
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

    /// <summary>
    /// Same locate as Export: tools/ffmpeg next to the repo or the exe, then PATH.
    /// Used by the export toast so duration is not missing when ffprobe is not on PATH.
    /// </summary>
    internal static string? ResolveFfprobe()
    {
        static string? SiblingProbe(string ffmpegPath)
        {
            var dir = IoPath.GetDirectoryName(ffmpegPath);
            if (string.IsNullOrWhiteSpace(dir))
                return null;
            foreach (var name in new[] { "ffprobe.exe", "ffprobe" })
            {
                var p = IoPath.Combine(dir, name);
                if (IoFile.Exists(p))
                    return p;
            }

            return null;
        }

        var root = ProjectRoot.Find();
        var bundled = SiblingProbe(IoPath.Combine(root, "tools", "ffmpeg", "ffmpeg.exe"))
                      ?? SiblingProbe(IoPath.Combine(root, "tools", "ffmpeg", "ffmpeg"));
        if (bundled is not null)
            return bundled;

        var baseBundled = SiblingProbe(IoPath.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe"))
                          ?? SiblingProbe(IoPath.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg"));
        if (baseBundled is not null)
            return baseBundled;

        var fromFfmpeg = SiblingProbe(ResolveFfmpeg() ?? "");
        if (fromFfmpeg is not null)
            return fromFfmpeg;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(IoPath.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            foreach (var name in new[] { "ffprobe.exe", "ffprobe" })
            {
                var candidate = IoPath.Combine(dir, name);
                if (IoFile.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static async Task<string?> RunTextAsync(string fileName, string args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null)
                return null;
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var outText = await stdout;
            var errText = await stderr;
            return string.IsNullOrWhiteSpace(outText) ? errText : outText;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveFfmpeg()
    {
        static string? Probe(string dir)
        {
            foreach (var name in new[] { "ffmpeg.exe", "ffmpeg" })
            {
                var p = IoPath.Combine(dir, name);
                if (IoFile.Exists(p))
                    return p;
            }

            return null;
        }

        var root = ProjectRoot.Find();
        var bundled = Probe(IoPath.Combine(root, "tools", "ffmpeg"))
                      ?? Probe(IoPath.Combine(AppContext.BaseDirectory, "tools", "ffmpeg"));
        if (bundled is not null)
            return bundled;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(IoPath.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            var hit = Probe(dir);
            if (hit is not null)
                return hit;
        }

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
            // ignore -- Linux CI has no `where`; PATH walk above is the gate
        }

        return null;
    }
}
