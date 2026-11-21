using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using static NetieSpace.Apps.Video.VideoFfmpeg;

namespace NetieSpace.Apps.Video;

/// <summary>
/// Netie Video tool runners, all on FFmpeg run out of process. A tool is registered here only when it was
/// driven on a real file and its result read back with ffprobe (see tests/NetieSpace.Tests/VideoTool*.cs).
/// Recording, the editor, AI and cloud tools are deliberately absent.
/// </summary>
public sealed partial class VideoToolProvider : IToolProvider
{
    public ToolApp App => ToolApp.Video;

    public IEnumerable<(string ToolId, ToolRunnerDelegate Run)> Runners()
    {
        yield return ("vid-trim", TrimAsync);
        yield return ("vid-split", SplitAsync);
        yield return ("vid-merge", MergeAsync);
        yield return ("vid-mute", MuteAsync);
        yield return ("vid-extract-audio", ExtractAudioAsync);
        yield return ("vid-convert", ConvertAsync);
        yield return ("vid-compress", CompressAsync);
        yield return ("vid-resize", ResizeAsync);
        yield return ("vid-crop", CropAsync);
        yield return ("vid-rotate", RotateAsync);
        yield return ("vid-flip", FlipAsync);
        yield return ("vid-speed", SpeedAsync);
        yield return ("vid-reverse", ReverseAsync);
        yield return ("vid-loop", LoopAsync);
        yield return ("vid-volume", VolumeAsync);
        yield return ("vid-to-gif", ToGifAsync);
        yield return ("vid-add-audio", AddAudioAsync);
    }

    // ------------------------------------------------------------------ scaffolding

    internal sealed record Plan(IReadOnlyList<string> Args, double ExpectedSeconds);

    /// <summary>
    /// One output per input file. <paramref name="build"/> returns the ffmpeg arguments without the output path
    /// (it throws a clear message for bad options). Every output made by this call is deleted if any file fails.
    /// </summary>
    private static async Task<IReadOnlyList<string>> OneToOne(
        ToolRequest r, IProgress<ToolProgress>? progress, CancellationToken ct, string tag,
        Func<string, string> extFor, Func<string, VideoInfo, Plan> build,
        bool needVideo = true, Action<string, string>? verify = null)
    {
        var outs = new List<string>();
        try
        {
            for (var i = 0; i < r.Files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var input = Full(r.Files[i]);
                var info = needVideo ? RequireVideo(input) : Probe(input);
                var plan = build(input, info);
                var outPath = ReserveOutput(r.OutDir, input, tag, extFor(input));
                outs.Add(outPath);
                var args = plan.Args.Concat(new[] { outPath }).ToList();
                await RunAsync(args, plan.ExpectedSeconds, new SlicedProgress(progress, i, r.Files.Count),
                    IoPath.GetFileName(input), new[] { outPath }, ct).ConfigureAwait(false);
                verify?.Invoke(input, outPath);
            }
            return outs;
        }
        catch
        {
            foreach (var o in outs) TryDelete(o);
            throw;
        }
    }

    private static string SameExt(string input) => IoPath.GetExtension(input);

    private static string Lower(string? v, string fallback) => (v ?? fallback).Trim().ToLowerInvariant();

    private static string Pick(ToolRequest r, string key, string fallback, params string[] allowed)
    {
        var v = Lower(Opt(r, key), fallback);
        if (!allowed.Contains(v))
            throw new ArgumentException("option '" + key + "': '" + v + "' is not one of [" + string.Join(", ", allowed) + "]");
        return v;
    }

    private static readonly Regex HexColor = new(@"^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);

    // ------------------------------------------------------------------ vid-trim

    private static Task<IReadOnlyList<string>> TrimAsync(ToolRequest r, IProgress<ToolProgress>? p, CancellationToken ct)
    {
        var start = ParseTime(Opt(r, "start") ?? "0", "start");
        var endText = Opt(r, "end");
        double? end = endText is null ? null : ParseTime(endText, "end");
        var mode = Pick(r, "mode", "extract", "extract", "delete");
        if (end is double e0 && e0 <= start)
            throw new ArgumentException("option 'end' must be later than option 'start'");
        if (mode == "delete" && end is null)
            throw new ArgumentException("option 'end' is required when mode is delete");

        return OneToOne(r, p, ct, mode == "delete" ? "cut" : "trim", OutExt, (input, info) =>
        {
            if (start >= info.Duration)
                throw new ArgumentException("option 'start' (" + Num(start) + " s) is at or past the end of the video (" + Num(info.Duration) + " s)");
            var web = IsWebM(input);
            var a = new List<string>();
            if (mode == "extract")
            {
                var stop = Math.Min(end ?? info.Duration, info.Duration);
                var len = stop - start;
                a.AddRange(new[] { "-ss", Num(start), "-i", input, "-t", Num(len) });
                a.AddRange(Maps);
                a.AddRange(Vf());
                a.AddRange(Encode(web));
                return new Plan(a, len);
            }

            var cutEnd = Math.Min(end!.Value, info.Duration);
            if (start <= 0.001 && cutEnd >= info.Duration - 0.001)
                throw new ArgumentException("deleting the whole video leaves nothing");
            var s = Num(start);
            var e = Num(cutEnd);
            a.AddRange(new[] { "-i", input });
            a.AddRange(Maps);
            a.AddRange(Vf("select=lt(t\\," + s + ")+gt(t\\," + e + ")", "setpts=if(gt(T\\," + e + ")\\,PTS-(" + Num(cutEnd - start) + ")/TB\\,PTS)"));
            if (info.HasAudio) a.AddRange(new[] { "-af", "aselect=lt(t\\," + s + ")+gt(t\\," + e + "),asetpts=if(gt(T\\," + e + ")\\,PTS-(" + Num(cutEnd - start) + ")/TB\\,PTS)" });
            a.AddRange(Encode(web));
            return new Plan(a, info.Duration - (cutEnd - start));
        });
    }

    // ------------------------------------------------------------------ vid-split

    private static async Task<IReadOnlyList<string>> SplitAsync(ToolRequest r, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var input = Full(r.Files[0]);
        var atText = Opt(r, "at");
        var parts = IntOpt(r, "parts", 2, 20, 2);
        var info = RequireVideo(input);

        var cuts = new List<double>();
        if (atText is not null)
        {
            foreach (var token in atText.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                cuts.Add(ParseTime(token, "at"));
            cuts = cuts.Distinct().OrderBy(x => x).ToList();
            if (cuts.Count == 0) throw new ArgumentException("option 'at' has no cut points");
            if (cuts[0] <= 0 || cuts[^1] >= info.Duration)
                throw new ArgumentException("option 'at': every cut point must be inside the video (0 to " + Num(info.Duration) + " s)");
        }
        else
        {
            for (var i = 1; i < parts; i++) cuts.Add(info.Duration * i / parts);
        }

        var bounds = new List<double> { 0 };
        bounds.AddRange(cuts);
        bounds.Add(info.Duration);

        var outs = new List<string>();
        try
        {
            var web = IsWebM(input);
            for (var i = 0; i + 1 < bounds.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var from = bounds[i];
                var len = bounds[i + 1] - from;
                var outPath = ReserveOutput(r.OutDir, input, "part", OutExt(input), (i + 1).ToString("00", CultureInfo.InvariantCulture));
                outs.Add(outPath);
                var a = new List<string> { "-ss", Num(from), "-i", input };
                if (i + 2 < bounds.Count) a.AddRange(new[] { "-t", Num(len) });
                a.AddRange(Maps);
                a.AddRange(Vf());
                a.AddRange(Encode(web));
                a.Add(outPath);
                await RunAsync(a, len, new SlicedProgress(progress, i, bounds.Count - 1), "part " + (i + 1), new[] { outPath }, ct).ConfigureAwait(false);
            }
            return outs;
        }
        catch
        {
            foreach (var o in outs) TryDelete(o);
            throw;
        }
    }

    // ------------------------------------------------------------------ vid-merge

    private static async Task<IReadOnlyList<string>> MergeAsync(ToolRequest r, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        if (r.Files.Count < 2) throw new ArgumentException("vid-merge needs at least 2 videos");
        foreach (var f in r.Files)
            if (!VideoExts.Contains(IoPath.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                throw new NotSupportedException("merging images or audio files is not built in this version: " + f);

        var inputs = r.Files.Select(Full).ToList();
        var infos = inputs.Select(RequireVideo).ToList();
        // The canvas is the first clip as displayed: rotation tags are already applied in Width and Height, pixel shape here.
        var sar0 = infos[0].Sar;
        var w = (int)Math.Round(infos[0].Width * Math.Max(1.0, sar0)) / 2 * 2;
        var h = (int)Math.Round(infos[0].Height / Math.Min(1.0, sar0)) / 2 * 2;
        if (w < 2 || h < 2) throw new InvalidOperationException("first video has no usable frame size");
        var fps = infos[0].Fps >= 1 && infos[0].Fps <= 120 ? infos[0].Fps : 30;
        var anyAudio = infos.Any(i => i.HasAudio);

        var a = new List<string>();
        foreach (var f in inputs) a.AddRange(new[] { "-i", f });
        var g = new List<string>();
        var concatIn = new System.Text.StringBuilder();
        for (var i = 0; i < inputs.Count; i++)
        {
            g.Add($"[{i}:v:0]{SquarePixels},scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:color=black,setsar=1,fps={Num(fps)},format=yuv420p[v{i}]");
            concatIn.Append($"[v{i}]");
            if (anyAudio)
            {
                var d = Num(infos[i].Duration);
                g.Add(infos[i].HasAudio
                    ? $"[{i}:a:0]aresample=44100,aformat=sample_fmts=fltp:channel_layouts=stereo,apad,atrim=duration={d},asetpts=PTS-STARTPTS[a{i}]"
                    : $"anullsrc=r=44100:cl=stereo,atrim=duration={d},asetpts=PTS-STARTPTS[a{i}]");
                concatIn.Append($"[a{i}]");
            }
        }
        g.Add($"{concatIn}concat=n={inputs.Count}:v=1:a={(anyAudio ? 1 : 0)}[v]" + (anyAudio ? "[a]" : ""));
        a.AddRange(new[] { "-filter_complex", string.Join(";", g), "-map", "[v]" });
        if (anyAudio) a.AddRange(new[] { "-map", "[a]" });
        a.AddRange(Encode(false));

        var outPath = ReserveOutput(r.OutDir, inputs[0], "merged", ".mp4");
        a.Add(outPath);
        await RunAsync(a, infos.Sum(i => i.Duration), progress, "merging", new[] { outPath }, ct).ConfigureAwait(false);
        return new[] { outPath };
    }

    // ------------------------------------------------------------------ vid-mute

    private static Task<IReadOnlyList<string>> MuteAsync(ToolRequest r, IProgress<ToolProgress>? p, CancellationToken ct) =>
        OneToOne(r, p, ct, "muted", SameExt, (input, info) =>
        {
            if (!info.HasAudio) throw new InvalidOperationException(input + " has no audio stream to remove");
            return new Plan(new[] { "-i", input, "-map", "0:v:0", "-c:v", "copy", "-an" }, info.Duration);
        });

    // ------------------------------------------------------------------ audio formats

    private static string[] AudioCodec(string format) => format switch
    {
        "mp3" => new[] { "-c:a", "libmp3lame", "-q:a", "2" },
        "wav" => new[] { "-c:a", "pcm_s16le" },
        "m4a" => new[] { "-c:a", "aac", "-b:a", "192k" },
        "flac" => new[] { "-c:a", "flac" },
        "ogg" => new[] { "-c:a", "libvorbis", "-q:a", "5" },
        _ => throw new ArgumentException("option 'format': '" + format + "' is not an audio format"),
    };

    private static Plan AudioPlan(string input, VideoInfo info, string format)
    {
        if (!info.HasAudio) throw new InvalidOperationException(input + " has no audio stream");
        var a = new List<string> { "-i", input, "-vn", "-map", "0:a:0" };
        a.AddRange(AudioCodec(format));
        return new Plan(a, info.Duration);
    }

    // ------------------------------------------------------------------ vid-extract-audio

    private static Task<IReadOnlyList<string>> ExtractAudioAsync(ToolRequest r, IProgress<ToolProgress>? p, CancellationToken ct)
    {
        var format = Pick(r, "format", "mp3", "mp3", "wav", "m4a", "flac", "ogg");
        return OneToOne(r, p, ct, "audio", _ => "." + format, (input, info) => AudioPlan(input, info, format), needVideo: false);
    }

    // ------------------------------------------------------------------ vid-convert

    private static readonly string[] AudioTargets = { "mp3", "wav", "ogg", "flac" };

    private static Task<IReadOnlyList<string>> ConvertAsync(ToolRequest r, IProgress<ToolProgress>? p, CancellationToken ct)
    {
        var format = Pick(r, "format", "mp4", "mp4", "avi", "mov", "webm", "wmv", "mkv", "3gp", "gif", "mp3", "wav", "ogg", "flac");
        var audioOnly = AudioTargets.Contains(format);
        return OneToOne(r, p, ct, "converted", _ => "." + format, (input, info) =>
        {
            if (audioOnly) return AudioPlan(input, info, format);
            if (!info.HasVideo) throw new InvalidOperationException(input + " has no video stream");
            if (format == "gif") return GifPlan(input, info, 0, null, 12, 480);
            var a = new List<string> { "-i", input };
            a.AddRange(Maps);
            a.AddRange(Vf());
            switch (format)
            {
                case "mp4":
                case "mov":
                    a.AddRange(Encode(false));
                    break;
                case "mkv":
                    a.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "160k" });
                    break;
                case "webm":
                    a.AddRange(Encode(true));
                    break;
                case "avi":
                    a.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p", "-c:a", "libmp3lame", "-q:a", "4" });
                    break;
                case "wmv":
                    a.AddRange(new[] { "-c:v", "wmv2", "-b:v", "2500k", "-pix_fmt", "yuv420p", "-c:a", "wmav2", "-b:a", "160k" });
                    break;
                case "3gp":
                    a.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "24", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "128k", "-ar", "44100", "-ac", "2" });
                    break;
            }
            return new Plan(a, info.Duration);
        }, needVideo: false);
    }

    // ------------------------------------------------------------------ vid-compress

    private static Task<IReadOnlyList<string>> CompressAsync(ToolRequest r, IProgress<ToolProgress>? p, CancellationToken ct)
    {
        var quality = Pick(r, "quality", "medium", "high", "medium", "low");
        var height = Pick(r, "height", "original", "original", "1080", "720", "480", "360");
        var crf = quality switch { "high" => 23, "low" => 34, _ => 28 };
        return OneToOne(r, p, ct, "compressed", OutExt, (input, info) =>
        {
            var a = new List<string> { "-i", input };
            a.AddRange(Maps);
            a.AddRange(height == "original" ? Vf() : Vf("scale=-2:min(" + height + "\\,ih)"));
            a.AddRange(Encode(IsWebM(input), crf));
            return new Plan(a, info.Duration);
        }, verify: (input, output) =>
        {
            var before = new System.IO.FileInfo(input).Length;
            var after = new System.IO.FileInfo(output).Length;
            if (after >= before)
                throw new InvalidOperationException("compressing " + IoPath.GetFileName(input) + " would not make it smaller (" + before + " -> " + after
                    + " bytes); try quality low or a smaller height");
        });
    }

    // ------------------------------------------------------------------ vid-resize

    private static Task<IReadOnlyList<string>> ResizeAsync(ToolRequest r, IProgress<ToolProgress>? p, CancellationToken ct)
    {
        var w = IntOpt(r, "width", 0, 7680, 0);
        var h = IntOpt(r, "height", 0, 7680, 0);
        var mode = Pick(r, "mode", "fit", "fit", "pad", "crop", "stretch");
        var color = Opt(r, "color") ?? "#000000";
        if (w == 0 && h == 0) throw new ArgumentException("set option 'width' or option 'height' (or both)");
        if (w % 2 != 0) throw new ArgumentException("option 'width': " + w + " must be even");
        if (h % 2 != 0) throw new ArgumentException("option 'height': " + h + " must be even");
        if (!HexColor.IsMatch(color)) throw new ArgumentException("option 'color': '" + color + "' is not #RRGGBB");
        var rgb = "0x" + color.Substring(1, 6);

        string[] filters;
        if (w == 0 || h == 0)
            filters = new[] { w == 0 ? "scale=-2:" + h : "scale=" + w + ":-2", "setsar=1" };
        else
            filters = mode switch
            {
                "pad" => new[] { $"scale={w}:{h}:force_original_aspect_ratio=decrease", $"pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:color={rgb}", "setsar=1" },
                "crop" => new[] { $"scale={w}:{h}:force_original_aspect_ratio=increase", $"crop={w}:{h}", "setsar=1" },
                "stretch" => new[] { $"scale={w}:{h}", "setsar=1" },
                _ => new[] { $"scale={w}:{h}:force_original_aspect_ratio=decrease", "setsar=1" },
            };

        return OneToOne(r, p, ct, "resized", OutExt, (input, info) =>
        {
            var a = new List<string> { "-i", input };
            a.AddRange(Maps);
            a.AddRange(Vf(new[] { SquarePixels }.Concat(filters).ToArray()));
            a.AddRange(Encode(IsWebM(input)));
            return new Plan(a, info.Duration);
        });
    }

    // ------------------------------------------------------------------ vid-crop

    private static Task<IReadOnlyList<string>> CropAsync(ToolRequest r, IProgress<ToolProgress>? p, CancellationToken ct)
    {
        var x = IntOpt(r, "x", 0, 16384, 0);
        var y = IntOpt(r, "y", 0, 16384, 0);
        var w = IntOpt(r, "width", 2, 16384);
        var h = IntOpt(r, "height", 2, 16384);
        if (w % 2 != 0) throw new ArgumentException("option 'width': " + w + " must be even");
        if (h % 2 != 0) throw new ArgumentException("option 'height': " + h + " must be even");

        return OneToOne(r, p, ct, "cropped", OutExt, (input, info) =>
        {
            if (x + w > info.Width || y + h > info.Height)
                throw new ArgumentException($"crop area {w}x{h} at {x},{y} does not fit inside the {info.Width}x{info.Height} frame");
            var a = new List<string> { "-i", input };
            a.AddRange(Maps);
            a.AddRange(Vf($"crop={w}:{h}:{x}:{y}:exact=1"));
            a.AddRange(Encode(IsWebM(input)));
            return new Plan(a, info.Duration);
        });
    }

    // ------------------------------------------------------------------ vid-rotate / vid-flip

    private static Task<IReadOnlyList<string>> RotateAsync(ToolRequest r, IProgress<ToolProgress>? p, CancellationToken ct)
    {
        var filter = Pick(r, "angle", "90", "90", "180", "270") switch
        {
            "90" => "transpose=1",
            "270" => "transpose=2",
            _ => "hflip,vflip",
        };
        return OneToOne(r, p, ct, "rotated", OutExt, (input, info) =>
        {
            var a = new List<string> { "-i", input };
            a.AddRange(Maps);
            a.AddRange(Vf(filter));
            a.AddRange(Encode(IsWebM(input)));
            return new Plan(a, info.Duration);
        });
    }

    private static Task<IReadOnlyList<string>> FlipAsync(ToolRequest r, IProgress<ToolProgress>? p, CancellationToken ct)
    {
        var axis = Pick(r, "axis", "horizontal", "horizontal", "vertical");
        var filter = axis == "vertical" ? "vflip" : "hflip";
        return OneToOne(r, p, ct, "flipped", OutExt, (input, info) =>
        {
            var a = new List<string> { "-i", input };
            a.AddRange(Maps);
            a.AddRange(Vf(filter));
            a.AddRange(Encode(IsWebM(input)));
            return new Plan(a, info.Duration);
        });
    }

    // ------------------------------------------------------------------ vid-speed

    internal static string AtempoChain(double factor)
    {
        var stages = new List<string>();
        while (factor > 2.0) { stages.Add("atempo=2.0"); factor /= 2.0; }
        while (factor < 0.5) { stages.Add("atempo=0.5"); factor *= 2.0; }
        stages.Add("atempo=" + Num(factor));
        return string.Join(",", stages);
    }

    private static Task<IReadOnlyList<string>> SpeedAsync(ToolRequest r, IProgress<ToolProgress>? p, CancellationToken ct)
    {
        var speed = IntOpt(r, "speed", 25, 400, 200);
        var audio = Pick(r, "audio", "keep", "keep", "mute");
        return OneToOne(r, p, ct, "speed", OutExt, (input, info) =>
        {
            var a = new List<string> { "-i", input, "-map", "0:v:0" };
            var keep = audio == "keep" && info.HasAudio;
            if (keep) a.AddRange(new[] { "-map", "0:a" });
            a.AddRange(Vf("setpts=PTS*100/" + speed));
            if (keep) a.AddRange(new[] { "-af", AtempoChain(speed / 100.0) });
            else a.Add("-an");
            a.AddRange(Encode(IsWebM(input)));
            return new Plan(a, info.Duration * 100.0 / speed);
        });
    }

    // ------------------------------------------------------------------ vid-reverse

    private const double ReverseMaxSeconds = 60;
    private const double ReverseMaxBytes = 1.5e9;

    private static Task<IReadOnlyList<string>> ReverseAsync(ToolRequest r, IProgress<ToolProgress>? p, CancellationToken ct) =>
        OneToOne(r, p, ct, "reversed", OutExt, (input, info) =>
        {
            if (info.Duration > ReverseMaxSeconds)
                throw new NotSupportedException("reversing a video longer than " + ReverseMaxSeconds + " s is not built in this version (this one is "
                    + Num(info.Duration) + " s; trim it first)");
            var bytes = info.Duration * Math.Max(info.Fps, 1) * info.Width * info.Height * 1.5;
            if (bytes > ReverseMaxBytes)
                throw new NotSupportedException("this video is too large to reverse in memory; resize or trim it first");
            var a = new List<string> { "-i", input };
            a.AddRange(Maps);
            a.AddRange(Vf("reverse"));
            if (info.HasAudio) a.AddRange(new[] { "-af", "areverse" });
            a.AddRange(Encode(IsWebM(input)));
            return new Plan(a, info.Duration);
        });

    // ------------------------------------------------------------------ vid-loop

    private static Task<IReadOnlyList<string>> LoopAsync(ToolRequest r, IProgress<ToolProgress>? p, CancellationToken ct)
    {
        var count = IntOpt(r, "count", 2, 16, 2);
        return OneToOne(r, p, ct, "looped", OutExt, (input, info) =>
        {
            if (IoPath.GetExtension(input).Equals(".gif", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("looping a GIF is not built in this version");
            var a = new List<string> { "-stream_loop", (count - 1).ToString(CultureInfo.InvariantCulture), "-i", input };
            a.AddRange(Maps);
            a.AddRange(Vf());
            // Newer FFmpeg leaves a timestamp gap at each loop seam, which drifts the frame
            // rate (24 became 23.88 on FFmpeg 9). Pin the output to the source rate.
            if (info.Fps > 0)
                a.AddRange(new[] { "-r", info.Fps.ToString("0.###", CultureInfo.InvariantCulture) });
            a.AddRange(Encode(IsWebM(input)));
            return new Plan(a, info.Duration * count);
        });
    }

    // ------------------------------------------------------------------ vid-volume

    private static Task<IReadOnlyList<string>> VolumeAsync(ToolRequest r, IProgress<ToolProgress>? p, CancellationToken ct)
    {
        var pct = IntOpt(r, "volume", 0, 200, 100);
        var vol = Num(pct / 100.0);
        return OneToOne(r, p, ct, "volume", input => CopyOk(input) ? SameExt(input) : OutExt(input), (input, info) =>
        {
            if (!info.HasAudio) throw new InvalidOperationException(input + " has no audio stream to change");
            var a = new List<string> { "-i", input };
            if (CopyOk(input))
            {
                a.AddRange(new[] { "-map", "0:v:0", "-map", "0:a", "-c:v", "copy", "-af", "volume=" + vol, "-c:a", "aac", "-b:a", "160k" });
            }
            else
            {
                a.AddRange(Maps);
                a.AddRange(Vf());
                a.AddRange(new[] { "-af", "volume=" + vol });
                a.AddRange(Encode(IsWebM(input)));
            }
            return new Plan(a, info.Duration);
        });
    }

    // ------------------------------------------------------------------ vid-to-gif

    /// <summary>
    /// ffmpeg holds every frame of the clip between palettegen and paletteuse (the palette needs the whole clip before
    /// the first frame can be written), up to 4 bytes a pixel. The picture is scaled down until all of them fit here.
    /// </summary>
    internal const long GifQueueBudgetBytes = 512L * 1024 * 1024;

    /// <summary>
    /// Output width for a GIF of <paramref name="outSeconds"/> at <paramref name="fps"/>: the width asked for
    /// (0 = the picture's own width), shrunk until the queued frames fit <see cref="GifQueueBudgetBytes"/>.
    /// Never below 16 pixels (or the source, if smaller). <paramref name="upscale"/> lets an explicit width exceed the source.
    /// </summary>
    internal static int GifFitWidth(VideoInfo info, int wanted, double outSeconds, int fps, bool upscale)
    {
        var sar = info.Sar > 0 ? info.Sar : 1.0;
        double effW = sar >= 1 ? info.Width * sar : info.Width;
        double effH = sar < 1 ? info.Height / sar : info.Height;
        var w = wanted > 0 ? (upscale ? wanted : Math.Min(wanted, effW)) : effW;
        var frames = Math.Max(1.0, Math.Ceiling(outSeconds * fps));
        var fit = Math.Sqrt(GifQueueBudgetBytes * effW / (frames * 4.0 * Math.Max(1.0, effH)));
        var result = Math.Min(w, fit);
        result = Math.Max(result, Math.Min(16.0, w));
        var floor = Math.Min(16.0, w);
        var px = (int)Math.Floor(result);
        // Heights round up, so step down until the rounded frame size fits as well.
        while (px > floor && frames * px * Math.Ceiling(px * effH / effW) * 4.0 > GifQueueBudgetBytes) px--;
        return Math.Max(1, px);
    }

    internal static Plan GifPlan(string input, VideoInfo info, double start, double? length, int fps, int width)
    {
        if (start >= info.Duration)
            throw new ArgumentException("option 'start' (" + Num(start) + " s) is at or past the end of the video");
        var len = Math.Min(length ?? (info.Duration - start), info.Duration - start);
        if (len > 120)
            throw new ArgumentException("option 'duration': a GIF longer than 120 s is not sensible (this would be " + Num(len) + " s); set a shorter duration");
        var a = new List<string>();
        if (start > 0) a.AddRange(new[] { "-ss", Num(start) });
        a.AddRange(new[] { "-t", Num(len), "-i", input });
        var fit = GifFitWidth(info, width, len, fps, upscale: false);
        var graph = $"[0:v:0]{SquarePixels},fps={fps},scale=min({fit}\\,iw):-1:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse[g]";
        a.AddRange(new[] { "-filter_complex", graph, "-map", "[g]", "-an", "-loop", "0" });
        return new Plan(a, len);
    }

    private static Task<IReadOnlyList<string>> ToGifAsync(ToolRequest r, IProgress<ToolProgress>? p, CancellationToken ct)
    {
        var fps = IntOpt(r, "fps", 1, 30, 10);
        var width = IntOpt(r, "width", 64, 1280, 480);
        var start = ParseTime(Opt(r, "start") ?? "0", "start");
        var durText = Opt(r, "duration");
        double? length = durText is null ? null : ParseTime(durText, "duration");
        if (length is double l && l <= 0) throw new ArgumentException("option 'duration' must be greater than 0");

        foreach (var f in r.Files)
            if (!VideoExts.Contains(IoPath.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                throw new NotSupportedException("turning images or GIFs into a GIF is not built in this version: " + f);

        return OneToOne(r, p, ct, "gif", _ => ".gif", (input, info) => GifPlan(input, info, start, length, fps, width));
    }

    // ------------------------------------------------------------------ vid-add-audio

    private static async Task<IReadOnlyList<string>> AddAudioAsync(ToolRequest r, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var mode = Pick(r, "mode", "replace", "replace", "mix");
        var pct = IntOpt(r, "volume", 0, 200, 100);
        var vol = Num(pct / 100.0);

        // The video is the first file with a video extension; the other file is the sound source.
        var files = r.Files.Select(Full).ToList();
        var vIdx = files.FindIndex(f => VideoExts.Contains(IoPath.GetExtension(f), StringComparer.OrdinalIgnoreCase));
        if (vIdx < 0) throw new ArgumentException("vid-add-audio needs one video file and one audio file; no video file was given");
        var video = files[vIdx];
        var audio = files[1 - vIdx];
        var vInfo = RequireVideo(video);
        var aInfo = Probe(audio);
        if (!aInfo.HasAudio) throw new InvalidOperationException(audio + " has no audio stream");

        var copy = CopyOk(video);
        var mix = mode == "mix" && vInfo.HasAudio;
        var g = new List<string>();
        if (!copy) g.Add("[0:v:0]" + EvenScale + "[v]");
        g.Add(mix
            ? $"[1:a:0]volume={vol}[a1];[0:a:0][a1]amix=inputs=2:duration=first:dropout_transition=0:normalize=0[a]"
            : $"[1:a:0]volume={vol},apad[a]");

        var a = new List<string> { "-i", video, "-i", audio, "-filter_complex", string.Join(";", g),
            "-map", copy ? "0:v:0" : "[v]", "-map", "[a]" };
        if (copy) a.AddRange(new[] { "-c:v", "copy", "-c:a", "aac", "-b:a", "160k" });
        else a.AddRange(Encode(IsWebM(video)));
        a.AddRange(new[] { "-t", Num(vInfo.Duration) });

        var outPath = ReserveOutput(r.OutDir, video, "with-audio", copy ? SameExt(video) : OutExt(video));
        a.Add(outPath);
        await RunAsync(a, vInfo.Duration, progress, "adding audio", new[] { outPath }, ct).ConfigureAwait(false);
        return new[] { outPath };
    }
}
