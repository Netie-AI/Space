using System.Text;
using NetieSpace.Services.Timeline;

namespace NetieSpace.Services;

/// <summary>
/// ffmpeg graphs Space burns on V1 export. Kept free of WPF / process IO so CI can
/// compile-link and assert Select→Split→Drag→Delete→Fade→Wipe export math.
/// Does not replace the PARKING_LOT human Export gate.
/// </summary>
public static class FfmpegExportGraph
{
    public static string ScalePad(int width, int height)
    {
        var w = width > 0 ? width : 1920;
        var h = height > 0 ? height : 1080;
        return $"scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,setsar=1,setpts=PTS-STARTPTS,fps=30,format=yuv420p";
    }

    public static double XfadeOffsetSeconds(TimeSpan leftDuration, TimeSpan xfadeDur) =>
        Math.Max(0, leftDuration.TotalSeconds - xfadeDur.TotalSeconds);

    public static string XfadeWithAudio(TimeSpan leftDuration, TimeSpan xfadeDur, int width, int height)
    {
        var offset = XfadeOffsetSeconds(leftDuration, xfadeDur);
        var d = xfadeDur.TotalSeconds;
        var scale = ScalePad(width, height);
        var xfade = TimelineProject.XfadeWipeName;
        return
            $"[0:v]{scale}[v0];[1:v]{scale}[v1];" +
            $"[v0][v1]xfade=transition={xfade}:duration={d:0.###}:offset={offset:0.###}[vout];" +
            $"[0:a]aformat=sample_fmts=fltp:sample_rates=44100:channel_layouts=stereo[a0];" +
            $"[1:a]aformat=sample_fmts=fltp:sample_rates=44100:channel_layouts=stereo[a1];" +
            $"[a0][a1]acrossfade=d={d:0.###}[aout]";
    }

    public static string XfadeVideoOnly(TimeSpan leftDuration, TimeSpan xfadeDur, int width, int height)
    {
        var offset = XfadeOffsetSeconds(leftDuration, xfadeDur);
        var d = xfadeDur.TotalSeconds;
        var scale = ScalePad(width, height);
        var xfade = TimelineProject.XfadeWipeName;
        return
            $"[0:v]{scale}[v0];[1:v]{scale}[v1];" +
            $"[v0][v1]xfade=transition={xfade}:duration={d:0.###}:offset={offset:0.###}[vout]";
    }

    /// <summary>
    /// Per-clip -vf chain. Wipe edges skip opacity fade so xfade is not double-darkened.
    /// </summary>
    public static string ClipVideoFilters(
        TimelineClip clip,
        bool normalizeSize,
        int width,
        int height)
    {
        var vf = new StringBuilder();
        if (clip.Speed > 0 && Math.Abs(clip.Speed - 1.0) > 0.01)
            vf.Append($"setpts={1.0 / clip.Speed:0.####}*PTS");

        if (normalizeSize)
        {
            var nw = width > 0 ? width : 1920;
            var nh = height > 0 ? height : 1080;
            if (vf.Length > 0) vf.Append(',');
            vf.Append($"scale={nw}:{nh}:force_original_aspect_ratio=decrease,pad={nw}:{nh}:(ow-iw)/2:(oh-ih)/2,setsar=1");
        }

        if (clip.FadeIn > TimeSpan.Zero && clip.TransitionIn != TransitionKind.Wipe)
        {
            if (vf.Length > 0) vf.Append(',');
            vf.Append($"fade=t=in:st=0:d={clip.FadeIn.TotalSeconds:0.###}");
        }

        if (clip.FadeOut > TimeSpan.Zero && clip.TransitionOut != TransitionKind.Wipe)
        {
            var dur = (clip.SourceOut - clip.SourceIn).TotalSeconds / Math.Max(0.01, clip.Speed);
            var st = Math.Max(0, dur - clip.FadeOut.TotalSeconds);
            if (vf.Length > 0) vf.Append(',');
            vf.Append($"fade=t=out:st={st:0.###}:d={clip.FadeOut.TotalSeconds:0.###}");
        }

        return vf.ToString();
    }

    /// <summary>
    /// Append black after V1 so a title card past the last clip still burns.
    /// stop_mode=add is black; clone would repeat the last leftover frame.
    /// </summary>
    public static string PadBlackAfterVideo(double padSeconds) =>
        $"tpad=stop_mode=add:stop_duration={Math.Max(0, padSeconds):0.###}";

    /// <summary>
    /// ffmpeg filter path (subtitles / drawtext). Drive colon and
    /// apostrophe must be escaped or a folder like Mom's clips fails burn-in.
    /// </summary>
    public static string EscapeFilterPath(string path) =>
        path.Replace('\\', '/').Replace(":", "\\:").Replace("'", "\\'");

    /// <summary>
    /// concat demuxer line. Absolute paths with ' fail even with '\'':
    /// ffmpeg opens Mom's clips as Moms. The list file must sit next to
    /// the media; only the file name is listed.
    /// </summary>
    public static string ConcatDemuxerLine(string mediaPathInListDir)
    {
        var name = mediaPathInListDir ?? "";
        var slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
        if (slash >= 0 && slash + 1 < name.Length)
            name = name[(slash + 1)..];
        return $"file '{name}'";
    }
}
