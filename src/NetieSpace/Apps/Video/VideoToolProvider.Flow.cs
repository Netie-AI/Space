using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using NetieSpace.Apps.Tools.Catalog;
using static NetieSpace.Apps.Video.VideoFfmpeg;

namespace NetieSpace.Apps.Video;

// W6-4: the quick edit behind the filmstrip. One ffmpeg run applies, in this order: the selection (in/out), a speed
// section, a caption, the preset's size and codec, and a mute section on the sound. It is not in the tool catalog: the
// page of vid-trim / vid-to-gif / vid-speed / vid-mute / vid-compress runs it through ToolRunner as "vid-flow", so the
// output checks, the output-folder lock, progress and cancel are the ones every tool already has.
public sealed partial class VideoToolProvider
{
    internal const string FlowToolId = "vid-flow";

    /// <summary>The synthetic entry the filmstrip page runs. Times are seconds or mm:ss.f in the source's own timeline.</summary>
    internal static readonly ToolEntry FlowEntry = new(
        FlowToolId, ToolApp.Video, "Cut and assemble", "Quick edit", "Export", "flow",
        VideoExts, 1, 1,
        new[]
        {
            new ToolOption("in", ToolOptionKind.Text, "Start of the selection", "0"),
            new ToolOption("out", ToolOptionKind.Text, "End of the selection", null),
            new ToolOption("preset", ToolOptionKind.Choice, "Export for", VideoFlowPresets.Original, VideoFlowPresets.Ids),
            new ToolOption("mute_from", ToolOptionKind.Text, "Mute from", null),
            new ToolOption("mute_to", ToolOptionKind.Text, "Mute to", null),
            new ToolOption("speed_from", ToolOptionKind.Text, "Speed section from", null),
            new ToolOption("speed_to", ToolOptionKind.Text, "Speed section to", null),
            new ToolOption("speed", ToolOptionKind.Int, "Speed percent", "200", null, 25, 400),
            new ToolOption("caption", ToolOptionKind.Text, "Caption", null),
        },
        Engines.Ffmpeg, ToolVerdict.Local);

    /// <summary>The flow as a registered tool, with the availability of the page's own tool (the same ffmpeg).</summary>
    internal static RegisteredTool FlowTool(ToolAvailability availability) => new(FlowEntry, FlowExportAsync, availability);

    internal sealed record FlowSpec(
        double In, double Out, string Preset,
        double? MuteFrom, double? MuteTo,
        double? SpeedFrom, double? SpeedTo, int SpeedPercent,
        string? Caption)
    {
        public double Length => Out - In;

        /// <summary>Length of the result: the selection with the speed section shortened or stretched.</summary>
        public double OutSeconds => SpeedFrom is double a && SpeedTo is double b
            ? Length - (b - a) + (b - a) * 100.0 / SpeedPercent
            : Length;
    }

    internal static FlowSpec ReadFlowSpec(ToolRequest r, VideoInfo info)
    {
        var inT = ParseTime(Opt(r, "in") ?? "0", "in");
        var outText = Opt(r, "out");
        var outT = outText is null ? info.Duration : Math.Min(ParseTime(outText, "out"), info.Duration);
        if (inT >= info.Duration)
            throw new ArgumentException("option 'in' (" + Num(inT) + " s) is at or past the end of the video (" + Num(info.Duration) + " s)");
        if (outT - inT < 0.1)
            throw new ArgumentException("the selection must be at least 0.1 s long (in " + Num(inT) + " s, out " + Num(outT) + " s)");

        var preset = Pick(r, "preset", VideoFlowPresets.Original, VideoFlowPresets.Ids.ToArray());

        (double? From, double? To) Section(string a, string b, string what)
        {
            var fa = Opt(r, a);
            var fb = Opt(r, b);
            if (fa is null && fb is null) return (null, null);
            if (fa is null || fb is null) throw new ArgumentException("option '" + (fa is null ? a : b) + "' is missing: a " + what + " needs both a start and an end");
            var from = Math.Max(inT, ParseTime(fa, a));
            var to = Math.Min(outT, ParseTime(fb, b));
            if (to - from < 0.05)
                throw new ArgumentException("the " + what + " (" + Num(from) + " to " + Num(to) + " s) must lie inside the selection and be at least 0.05 s long");
            return (from, to);
        }

        var (mf, mt) = Section("mute_from", "mute_to", "mute section");
        if (mf is not null && !info.HasAudio)
            throw new ArgumentException("this video has no sound, so there is nothing to mute");
        var (sf, st) = Section("speed_from", "speed_to", "speed section");
        var pct = IntOpt(r, "speed", 25, 400, 200);

        var caption = r.Options.TryGetValue("caption", out var raw) && !string.IsNullOrWhiteSpace(raw) ? raw.Trim() : null;
        if (caption is not null)
        {
            if (caption.Length > 300) throw new ArgumentException("option 'caption' is longer than 300 characters");
            if (caption.Any(c => c == '\0')) throw new ArgumentException("option 'caption' contains a NUL character");
        }
        return new FlowSpec(inT, outT, preset, mf, mt, sf, st, pct, caption);
    }

    /// <summary>Greedy word wrap so a long caption stays inside the frame (drawtext does not wrap).</summary>
    internal static string WrapCaption(string text, int maxChars)
    {
        maxChars = Math.Max(8, maxChars);
        var lines = new List<string>();
        foreach (var para in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = new StringBuilder();
            foreach (var word in para.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > maxChars)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
            lines.Add(line.ToString());
        }
        return string.Join("\n", lines);
    }

    private sealed record FlowGraph(string Graph, string VideoLabel, string? AudioLabel);

    private static FlowGraph BuildFlowGraph(FlowSpec s, VideoInfo info, bool withAudio, string? captionFile, string? font)
    {
        var g = new List<string>();
        var n = 0;
        var cur = "[0:v:0]";
        void Stage(string filters)
        {
            var o = "[x" + (++n).ToString(CultureInfo.InvariantCulture) + "]";
            g.Add(cur + filters + o);
            cur = o;
        }

        var len = s.Length;
        var preset = s.Preset;
        var gif = preset == VideoFlowPresets.Gif;
        var srcW = Math.Max(2, info.Width);
        var srcH = Math.Max(2, info.Height);
        var outH = VideoFlowPresets.OutputHeight(preset, s.OutSeconds, srcW, srcH);

        Stage(SquarePixels);
        if ((preset == VideoFlowPresets.WhatsApp || preset == VideoFlowPresets.Reel) && info.Fps > 30.5)
            Stage("fps=30");

        // The speed section: split the selection into before / section / after, retime the middle, join again.
        double? relFrom = s.SpeedFrom is double sf ? sf - s.In : null;
        double? relTo = s.SpeedTo is double st ? st - s.In : null;
        var segs = new List<(double A, double B, double K)>();
        if (relFrom is double a0 && relTo is double b0)
        {
            if (a0 > 0.001) segs.Add((0, a0, 1.0));
            segs.Add((a0, b0, 100.0 / s.SpeedPercent));
            if (b0 < len - 0.001) segs.Add((b0, len, 1.0));
            if (segs.Count == 1)
            {
                Stage("setpts=PTS*" + Num(segs[0].K));
            }
            else
            {
                g.Add(cur + "split=" + segs.Count.ToString(CultureInfo.InvariantCulture)
                    + string.Concat(segs.Select((_, i) => "[sv" + i.ToString(CultureInfo.InvariantCulture) + "]")));
                for (var i = 0; i < segs.Count; i++)
                    g.Add("[sv" + i.ToString(CultureInfo.InvariantCulture) + "]trim=start=" + Num(segs[i].A) + ":end=" + Num(segs[i].B)
                        + ",setpts=(PTS-STARTPTS)*" + Num(segs[i].K) + "[sw" + i.ToString(CultureInfo.InvariantCulture) + "]");
                var joined = "[x" + (++n).ToString(CultureInfo.InvariantCulture) + "]";
                g.Add(string.Concat(segs.Select((_, i) => "[sw" + i.ToString(CultureInfo.InvariantCulture) + "]"))
                    + "concat=n=" + segs.Count.ToString(CultureInfo.InvariantCulture) + ":v=1:a=0" + joined);
                cur = joined;
            }
        }

        // The preset's size and shape.
        switch (preset)
        {
            case VideoFlowPresets.WhatsApp:
            {
                var (_, h) = VideoFlowPresets.WhatsAppPlan(s.OutSeconds, srcH);
                Stage("scale=-2:min(" + h.ToString(CultureInfo.InvariantCulture) + "\\,ih)," + EvenScale);
                break;
            }
            case VideoFlowPresets.Reel:
            {
                var rw = VideoFlowPresets.ReelWidth.ToString(CultureInfo.InvariantCulture);
                var rh = VideoFlowPresets.ReelHeight.ToString(CultureInfo.InvariantCulture);
                g.Add(cur + "split=2[ra][rb]");
                g.Add("[ra]scale=" + rw + ":" + rh + ":force_original_aspect_ratio=increase,crop=" + rw + ":" + rh + ",boxblur=24:3[rbg]");
                g.Add("[rb]scale=" + rw + ":" + rh + ":force_original_aspect_ratio=decrease[rfg]");
                var o = "[x" + (++n).ToString(CultureInfo.InvariantCulture) + "]";
                g.Add("[rbg][rfg]overlay=(W-w)/2:(H-h)/2" + o);
                cur = o;
                Stage("setsar=1");
                break;
            }
            case VideoFlowPresets.YouTube:
                Stage("scale=-2:min(1080\\,ih)," + EvenScale);
                break;
            case VideoFlowPresets.Gif:
            {
                var fit = GifFitWidth(info, VideoFlowPresets.GifWidth, s.OutSeconds, VideoFlowPresets.GifFps, upscale: false);
                Stage("fps=" + VideoFlowPresets.GifFps.ToString(CultureInfo.InvariantCulture)
                    + ",scale=min(" + fit.ToString(CultureInfo.InvariantCulture) + "\\,iw):-1:flags=lanczos");
                break;
            }
            default:
                Stage(EvenScale);
                break;
        }

        // The caption goes on after the size is known, so its size is a share of the picture the viewer sees.
        if (captionFile is not null && font is not null)
        {
            var fs = Math.Max(14, (int)Math.Round(outH / 16.0));
            Stage("drawtext=fontfile=" + W2FilterPath(font) + ":textfile=" + W2FilterPath(captionFile)
                + ":expansion=none:fontsize=" + fs.ToString(CultureInfo.InvariantCulture)
                + ":fontcolor=white:x=(w-text_w)/2:y=h-text_h-h*0.06:line_spacing=4"
                + ":box=1:boxcolor=black@0.55:boxborderw=" + Math.Max(4, fs / 3).ToString(CultureInfo.InvariantCulture));
        }

        if (gif)
        {
            g.Add(cur + "split[ga][gb]");
            g.Add("[ga]palettegen[gp]");
            g.Add("[gb][gp]paletteuse[gifv]");
            cur = "[gifv]";
        }
        else
        {
            Stage("format=yuv420p");
        }
        var videoLabel = cur;

        // The sound: a muted section first (in the selection's own timeline), then the same retiming as the picture.
        string? audioLabel = null;
        if (withAudio && info.HasAudio && !gif)
        {
            var ac = "[0:a:0]";
            var an = 0;
            void AStage(string filters)
            {
                var o = "[y" + (++an).ToString(CultureInfo.InvariantCulture) + "]";
                g.Add(ac + filters + o);
                ac = o;
            }
            if (s.MuteFrom is double mf && s.MuteTo is double mt)
                AStage("volume=0:enable='between(t," + Num(mf - s.In) + "," + Num(mt - s.In) + ")'");
            if (segs.Count == 1)
            {
                AStage(AtempoChain(s.SpeedPercent / 100.0));
            }
            else if (segs.Count > 1)
            {
                g.Add(ac + "asplit=" + segs.Count.ToString(CultureInfo.InvariantCulture)
                    + string.Concat(segs.Select((_, i) => "[sa" + i.ToString(CultureInfo.InvariantCulture) + "]")));
                for (var i = 0; i < segs.Count; i++)
                {
                    var tempo = Math.Abs(segs[i].K - 1.0) < 1e-9 ? "" : "," + AtempoChain(s.SpeedPercent / 100.0);
                    g.Add("[sa" + i.ToString(CultureInfo.InvariantCulture) + "]atrim=start=" + Num(segs[i].A) + ":end=" + Num(segs[i].B)
                        + ",asetpts=PTS-STARTPTS" + tempo + "[sb" + i.ToString(CultureInfo.InvariantCulture) + "]");
                }
                var joined = "[y" + (++an).ToString(CultureInfo.InvariantCulture) + "]";
                g.Add(string.Concat(segs.Select((_, i) => "[sb" + i.ToString(CultureInfo.InvariantCulture) + "]"))
                    + "concat=n=" + segs.Count.ToString(CultureInfo.InvariantCulture) + ":v=0:a=1" + joined);
                ac = joined;
            }
            if (ac == "[0:a:0]") AStage("anull");
            audioLabel = ac;
        }
        return new FlowGraph(string.Join(";", g), videoLabel, audioLabel);
    }

    internal static async Task<IReadOnlyList<string>> FlowExportAsync(ToolRequest r, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var input = Full(r.Files[0]);
        var info = RequireVideo(input);
        var s = ReadFlowSpec(r, info);
        var gif = s.Preset == VideoFlowPresets.Gif;
        if (gif && s.OutSeconds > 120)
            throw new ArgumentException("a GIF longer than 120 s is not sensible (this one would be " + Num(s.OutSeconds) + " s); pick a shorter selection");

        string? font = null;
        if (s.Caption is not null)
            font = W2FindFont() ?? throw new InvalidOperationException("no system font was found to draw the caption with (looked in the Windows Fonts folder and the usual Linux font folders)");

        var temps = new List<string>();
        string? outPath = null;
        string? logPrefix = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            string? captionFile = null;
            if (s.Caption is not null)
            {
                var outH = VideoFlowPresets.OutputHeight(s.Preset, s.OutSeconds, info.Width, info.Height);
                var outW = s.Preset == VideoFlowPresets.Reel ? VideoFlowPresets.ReelWidth : Math.Max(2, (int)Math.Round(outH * (double)Math.Max(2, info.Width) / Math.Max(2, info.Height)));
                var size = Math.Max(14, (int)Math.Round(outH / 16.0));
                var wrapped = WrapCaption(s.Caption, (int)(outW * 0.9 / (size * 0.55)));
                captionFile = W2NewTemp("caption", ".txt");
                temps.Add(captionFile);
                IoFile.WriteAllText(captionFile, wrapped, new UTF8Encoding(false));
            }

            var ext = gif ? ".gif" : s.Preset == VideoFlowPresets.Original ? OutExt(input) : ".mp4";
            var tag = s.Preset == VideoFlowPresets.Original ? "edit" : s.Preset;
            outPath = ReserveOutput(r.OutDir, input, tag, ext);

            var head = new List<string> { "-ss", Num(s.In), "-t", Num(s.Length), "-i", input };
            var withSound = BuildFlowGraph(s, info, true, captionFile, font);
            var name = IoPath.GetFileName(input);

            if (s.Preset == VideoFlowPresets.WhatsApp)
            {
                var (vbps, _) = VideoFlowPresets.WhatsAppPlan(s.OutSeconds, info.Height);
                logPrefix = W2NewTemp("pass", ".log");
                temps.Add(logPrefix);
                var v = vbps.ToString(CultureInfo.InvariantCulture);
                var video = new[] { "-c:v", "libx264", "-preset", "veryfast", "-b:v", v, "-pix_fmt", "yuv420p", "-passlogfile", logPrefix };

                var silent = BuildFlowGraph(s, info, false, captionFile, font);
                var p1 = new List<string>(head) { "-filter_complex", silent.Graph, "-map", silent.VideoLabel };
                p1.AddRange(video);
                p1.AddRange(new[] { "-pass", "1", "-an", "-f", "null", "-" });
                await RunAsync(p1, s.OutSeconds, new SlicedProgress(progress, 0, 2), name + ": pass 1 of 2", Array.Empty<string>(), ct).ConfigureAwait(false);

                ct.ThrowIfCancellationRequested();
                var p2 = new List<string>(head) { "-filter_complex", withSound.Graph, "-map", withSound.VideoLabel };
                if (withSound.AudioLabel is not null) p2.AddRange(new[] { "-map", withSound.AudioLabel });
                p2.AddRange(video);
                p2.AddRange(new[] { "-pass", "2" });
                if (withSound.AudioLabel is not null)
                    p2.AddRange(new[] { "-c:a", "aac", "-b:a", (VideoFlowPresets.WhatsAppAudioBps / 1000).ToString(CultureInfo.InvariantCulture) + "k" });
                p2.AddRange(new[] { "-movflags", "+faststart", outPath });
                await RunAsync(p2, s.OutSeconds, new SlicedProgress(progress, 1, 2), name + ": pass 2 of 2", new[] { outPath }, ct).ConfigureAwait(false);

                var size = new System.IO.FileInfo(outPath).Length;
                if (size >= VideoFlowPresets.WhatsAppLimitBytes)
                    throw new InvalidOperationException("the WhatsApp export came out at " + size.ToString(CultureInfo.InvariantCulture)
                        + " bytes, not under 16 MB; choose a shorter selection");
                return new[] { outPath };
            }

            var a = new List<string>(head) { "-filter_complex", withSound.Graph, "-map", withSound.VideoLabel };
            if (withSound.AudioLabel is not null) a.AddRange(new[] { "-map", withSound.AudioLabel });
            switch (s.Preset)
            {
                case VideoFlowPresets.Gif:
                    a.AddRange(new[] { "-an", "-loop", "0" });
                    break;
                case VideoFlowPresets.Reel:
                    a.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "22", "-maxrate", "8M", "-bufsize", "16M", "-pix_fmt", "yuv420p",
                        "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart" });
                    break;
                case VideoFlowPresets.YouTube:
                    a.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-maxrate", "12M", "-bufsize", "24M", "-pix_fmt", "yuv420p",
                        "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart" });
                    break;
                default:
                    a.AddRange(Encode(IsWebM(input)));
                    break;
            }
            a.Add(outPath);
            await RunAsync(a, s.OutSeconds, progress, name + ": encoding", new[] { outPath }, ct).ConfigureAwait(false);
            return new[] { outPath };
        }
        catch
        {
            if (outPath is not null) TryDelete(outPath);
            throw;
        }
        finally
        {
            foreach (var t in temps) TryDelete(t);
            if (logPrefix is not null)
            {
                try
                {
                    var dir = IoPath.GetDirectoryName(logPrefix);
                    var stem = IoPath.GetFileName(logPrefix);
                    if (dir is not null)
                        foreach (var f in IoDirectory.GetFiles(dir, stem + "*")) TryDelete(f);
                }
                catch { /* a pass log left behind is a temp file, not a result */ }
            }
        }
    }
}
