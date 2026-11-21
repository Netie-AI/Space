using System.Globalization;
using System.Text.RegularExpressions;
using NetieSpace.Apps.Tools;
using NetieSpace.Apps.Video;

namespace NetieSpace.Apps.Shell.Panels.Video;

// The decisions of the Netie Video quick edit (W6-4), with no WPF in them: time text, what the handles may do,
// where a click lands on the filmstrip, the options the engine receives, the size line and the progress line.
// The views only draw this and forward the mouse, so each rule here has a plain test.

/// <summary>Times as a person types and reads them: <c>mm:ss.f</c> (<c>1:02:03.4</c> past an hour). Seconds ("8.5") are accepted too.</summary>
public static class FlowTime
{
    private static readonly Regex Clock = new(@"^(?:(\d{1,3}):)?(\d{1,3}):(\d{1,2}(?:[.,]\d+)?)$", RegexOptions.Compiled);

    public static string Format(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        var tenths = (long)Math.Round(seconds * 10, MidpointRounding.AwayFromZero);
        var f = tenths % 10;
        var whole = tenths / 10;
        var s = whole % 60;
        var m = whole / 60 % 60;
        var h = whole / 3600;
        var inv = CultureInfo.InvariantCulture;
        return h > 0
            ? $"{h.ToString(inv)}:{m.ToString("00", inv)}:{s.ToString("00", inv)}.{f.ToString(inv)}"
            : $"{m.ToString("00", inv)}:{s.ToString("00", inv)}.{f.ToString(inv)}";
    }

    /// <summary>False for anything that is not a time; never throws.</summary>
    public static bool TryParse(string? text, out double seconds)
    {
        seconds = 0;
        var t = (text ?? "").Trim();
        if (t.Length == 0) return false;
        var inv = CultureInfo.InvariantCulture;
        if (double.TryParse(t.Replace(',', '.'), NumberStyles.AllowDecimalPoint, inv, out var plain))
        {
            if (plain < 0 || plain > 360_000) return false;
            seconds = plain;
            return true;
        }
        var m = Clock.Match(t);
        if (!m.Success) return false;
        double h = m.Groups[1].Success ? double.Parse(m.Groups[1].Value, inv) : 0;
        double mi = double.Parse(m.Groups[2].Value, inv);
        double s = double.Parse(m.Groups[3].Value.Replace(',', '.'), inv);
        if (s >= 60 || (m.Groups[1].Success && mi >= 60)) return false;
        seconds = h * 3600 + mi * 60 + s;
        return seconds <= 360_000;
    }
}

public enum FlowHit { None, In, Out, MuteFrom, MuteTo, SpeedFrom, SpeedTo, Seek }

/// <summary>Everything the person can change on the filmstrip, and the rules that keep it valid after every change.</summary>
public sealed class FlowState
{
    public const double MinSpan = 0.5;
    public const double MinSection = 0.1;

    public FlowState(double duration, bool hasAudio)
    {
        Duration = Math.Max(0.2, duration);
        HasAudio = hasAudio;
        In = 0;
        Out = Duration;
        MuteFrom = SpeedFrom = In;
        MuteTo = SpeedTo = Out;
    }

    public double Duration { get; }
    public bool HasAudio { get; }
    public double In { get; private set; }
    public double Out { get; private set; }
    public double Playhead { get; private set; }
    public bool MuteOn { get; private set; }
    public double MuteFrom { get; private set; }
    public double MuteTo { get; private set; }
    public bool SpeedOn { get; private set; }
    public double SpeedFrom { get; private set; }
    public double SpeedTo { get; private set; }
    public int SpeedPercent { get; set; } = 200;
    public string Caption { get; set; } = "";
    public string PresetId { get; set; } = VideoFlowPresets.Original;

    public double Length => Out - In;
    private double Span => Math.Min(MinSpan, Duration / 2);

    /// <summary>Length of the result: the selection with the speed section shortened or stretched.</summary>
    public double OutSeconds => SpeedOn
        ? Length - (SpeedTo - SpeedFrom) + (SpeedTo - SpeedFrom) * 100.0 / Math.Max(1, SpeedPercent)
        : Length;

    public void SetIn(double t)
    {
        In = Math.Clamp(t, 0, Math.Max(0, Out - Span));
        Settle();
    }

    public void SetOut(double t)
    {
        Out = Math.Clamp(t, Math.Min(Duration, In + Span), Duration);
        Settle();
    }

    public void SetPlayhead(double t) => Playhead = Math.Clamp(t, 0, Duration);

    public void SetMuteOn(bool on)
    {
        if (on && !HasAudio) on = false;
        if (on && !MuteOn) (MuteFrom, MuteTo) = Middle();
        MuteOn = on;
    }

    public void SetSpeedOn(bool on)
    {
        if (on && !SpeedOn) (SpeedFrom, SpeedTo) = Middle();
        SpeedOn = on;
    }

    /// <summary>The whole selection, for pages whose single job is this section (Change speed, Mute video).</summary>
    public void SectionCoversSelection(bool mute)
    {
        if (mute) { SetMuteOn(true); MuteFrom = In; MuteTo = Out; }
        else { SetSpeedOn(true); SpeedFrom = In; SpeedTo = Out; }
    }

    public void SetMuteFrom(double t) => MuteFrom = Math.Clamp(t, In, Math.Max(In, MuteTo - MinSection));
    public void SetMuteTo(double t) => MuteTo = Math.Clamp(t, Math.Min(Out, MuteFrom + MinSection), Out);
    public void SetSpeedFrom(double t) => SpeedFrom = Math.Clamp(t, In, Math.Max(In, SpeedTo - MinSection));
    public void SetSpeedTo(double t) => SpeedTo = Math.Clamp(t, Math.Min(Out, SpeedFrom + MinSection), Out);

    private (double From, double To) Middle()
    {
        var third = Length / 3.0;
        return (In + third, In + 2 * third);
    }

    /// <summary>Sections may not stick out of the selection when its handles move.</summary>
    private void Settle()
    {
        Playhead = Math.Clamp(Playhead, 0, Duration);
        (MuteFrom, MuteTo) = Fit(MuteFrom, MuteTo);
        (SpeedFrom, SpeedTo) = Fit(SpeedFrom, SpeedTo);
    }

    private (double, double) Fit(double from, double to)
    {
        var a = Math.Clamp(from, In, Out);
        var b = Math.Clamp(to, In, Out);
        if (b - a < MinSection)
        {
            if (a + MinSection <= Out) b = a + MinSection;
            else { b = Out; a = Math.Max(In, Out - MinSection); }
        }
        return (a, b);
    }

    /// <summary>Options of the "vid-flow" tool for this state. Blank times are left out so the engine's defaults apply.</summary>
    public IReadOnlyDictionary<string, string> ToOptions()
    {
        var o = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["in"] = Sec(In),
            ["out"] = Sec(Out),
            ["preset"] = PresetId,
        };
        if (MuteOn && HasAudio)
        {
            o["mute_from"] = Sec(MuteFrom);
            o["mute_to"] = Sec(MuteTo);
        }
        if (SpeedOn)
        {
            o["speed_from"] = Sec(SpeedFrom);
            o["speed_to"] = Sec(SpeedTo);
            o["speed"] = Math.Clamp(SpeedPercent, 25, 400).ToString(CultureInfo.InvariantCulture);
        }
        var cap = Caption.Trim();
        if (cap.Length > 0) o["caption"] = cap.Length > 300 ? cap[..300] : cap;
        return o;
    }

    private static string Sec(double t) => t.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>Where things are on the filmstrip control: pixels and seconds, and what a mouse press at a point grabs.</summary>
public static class FlowGeometry
{
    public const double StripHeight = 64;
    public const double MuteTop = 70, MuteBottom = 84;
    public const double SpeedTop = 88, SpeedBottom = 102;
    public const double HandleSlop = 9;
    /// <summary>The drawn grip is 10 px wide, beside the edge it moves.</summary>
    public const double GripOffset = 5;
    public const double Padding = 8;

    public static double TimeToX(double t, double width, double duration) =>
        Padding + Math.Clamp(t / Math.Max(0.001, duration), 0, 1) * Math.Max(1, width - 2 * Padding);

    public static double XToTime(double x, double width, double duration) =>
        Math.Clamp((x - Padding) / Math.Max(1, width - 2 * Padding), 0, 1) * duration;

    /// <summary>The handle a press at (x, y) grabs. Section handles only exist while their section is on.</summary>
    public static FlowHit HitTest(double x, double y, double width, FlowState s)
    {
        double X(double t) => TimeToX(t, width, s.Duration);
        if (s.MuteOn && y >= MuteTop - 3 && y <= MuteBottom + 3)
        {
            var a = Math.Abs(x - X(s.MuteFrom));
            var b = Math.Abs(x - X(s.MuteTo));
            if (Math.Min(a, b) <= HandleSlop) return a <= b ? FlowHit.MuteFrom : FlowHit.MuteTo;
        }
        if (s.SpeedOn && y >= SpeedTop - 3 && y <= SpeedBottom + 3)
        {
            var a = Math.Abs(x - X(s.SpeedFrom));
            var b = Math.Abs(x - X(s.SpeedTo));
            if (Math.Min(a, b) <= HandleSlop) return a <= b ? FlowHit.SpeedFrom : FlowHit.SpeedTo;
        }
        if (y <= StripHeight + 3)
        {
            // The grips are drawn outside the kept part (In to the left of its edge, Out to the right), so that is where they are grabbed.
            var a = Math.Abs(x - (X(s.In) - GripOffset));
            var b = Math.Abs(x - (X(s.Out) + GripOffset));
            if (Math.Min(a, b) <= HandleSlop)
                return a <= b ? FlowHit.In : FlowHit.Out;
            return FlowHit.Seek;
        }
        return FlowHit.None;
    }

    /// <summary>Moves the grabbed handle to the time under the mouse.</summary>
    public static void Drag(FlowState s, FlowHit hit, double seconds)
    {
        switch (hit)
        {
            case FlowHit.In: s.SetIn(seconds); break;
            case FlowHit.Out: s.SetOut(seconds); break;
            case FlowHit.MuteFrom: s.SetMuteFrom(seconds); break;
            case FlowHit.MuteTo: s.SetMuteTo(seconds); break;
            case FlowHit.SpeedFrom: s.SetSpeedFrom(seconds); break;
            case FlowHit.SpeedTo: s.SetSpeedTo(seconds); break;
            case FlowHit.Seek: s.SetPlayhead(seconds); break;
        }
    }

    /// <summary>Tick spacing for the time ruler under the strip: the smallest step that keeps labels about 80 px apart.</summary>
    public static double TickStep(double duration, double width)
    {
        var steps = new[] { 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600 };
        var perPx = duration / Math.Max(1, width - 2 * Padding);
        foreach (var s in steps)
            if (s / perPx >= 80) return s;
        return steps[^1];
    }
}

/// <summary>What the clip is, as the size estimate needs it.</summary>
public sealed record FlowSource(int Width, int Height, double Seconds, long Bytes, double Fps, bool HasAudio);

public static class FlowLines
{
    /// <summary>"About 13.5 MB - 1280x720 - fits WhatsApp's 16 MB" for the chosen preset and selection.</summary>
    public static string Estimate(FlowState s, FlowSource src)
    {
        var preset = VideoFlowPresets.Find(s.PresetId);
        var bytes = VideoFlowPresets.EstimateBytes(preset.Id, s.OutSeconds, src.Width, src.Height, src.Seconds, src.Bytes);
        var size = ToolPageModel.FormatSize(bytes);
        var h = VideoFlowPresets.OutputHeight(preset.Id, s.OutSeconds, src.Width, src.Height);
        var shape = preset.Id switch
        {
            VideoFlowPresets.Reel => "1080x1920",
            VideoFlowPresets.Gif => $"{Math.Min(VideoFlowPresets.GifWidth, Math.Max(16, src.Width))} px wide, {VideoFlowPresets.GifFps} fps",
            VideoFlowPresets.WhatsApp => $"{h} p",
            VideoFlowPresets.YouTube => $"{h} p",
            _ => $"{src.Width}x{src.Height}",
        };
        var line = $"About {size} - {shape} - {FlowTime.Format(s.OutSeconds)} long";
        if (preset.Id == VideoFlowPresets.WhatsApp)
            line += bytes < VideoFlowPresets.WhatsAppLimitBytes ? " - fits the 16 MB limit" : " - too long for 16 MB";
        if (preset.IsGif && s.OutSeconds > 15)
            line += " - long for a GIF";
        return line;
    }

    /// <summary>Size line of one preset card (same arithmetic, short).</summary>
    internal static string CardSize(FlowPreset preset, FlowState s, FlowSource src) =>
        "~" + ToolPageModel.FormatSize(VideoFlowPresets.EstimateBytes(preset.Id, s.OutSeconds, src.Width, src.Height, src.Seconds, src.Bytes));

    /// <summary>"42% - about 18 s left - pass 2 of 2". Time left is shown once there is enough run to judge it by.</summary>
    public static string Progress(int percent, TimeSpan elapsed, string message)
    {
        var tail = string.IsNullOrWhiteSpace(message) ? "" : " - " + message;
        if (percent < 3 || elapsed < TimeSpan.FromSeconds(1.5))
            return "Starting" + tail;
        var left = elapsed.TotalSeconds * (100 - percent) / percent;
        return $"{percent}% - {Left(left)}{tail}";
    }

    public static string Left(double seconds)
    {
        if (seconds < 1.5) return "almost done";
        if (seconds < 90) return $"about {Math.Round(seconds).ToString("0", CultureInfo.InvariantCulture)} s left";
        return $"about {Math.Ceiling(seconds / 60).ToString("0", CultureInfo.InvariantCulture)} min left";
    }

    /// <summary>The line above the filmstrip: "Keeping 00:08.0 to 01:55.0 (01:47.0)".</summary>
    public static string Selection(FlowState s) =>
        $"Keeping {FlowTime.Format(s.In)} to {FlowTime.Format(s.Out)} ({FlowTime.Format(s.Length)})";
}

/// <summary>Which tool pages get the filmstrip, and what each starts with. Everything else keeps its form.</summary>
public static class FlowPages
{
    public static readonly IReadOnlyList<string> ToolIds = new[] { "vid-trim", "vid-to-gif", "vid-speed", "vid-mute", "vid-compress" };

    public static bool Applies(string toolId) => ToolIds.Contains(toolId, StringComparer.Ordinal);

    /// <summary>The state a page opens with for a clip of this length.</summary>
    public static FlowState Start(string toolId, double duration, bool hasAudio)
    {
        var s = new FlowState(duration, hasAudio);
        switch (toolId)
        {
            case "vid-to-gif":
                s.PresetId = VideoFlowPresets.Gif;
                s.SetOut(Math.Min(6.0, s.Duration));
                break;
            case "vid-compress":
                s.PresetId = VideoFlowPresets.WhatsApp;
                break;
            case "vid-speed":
                s.SectionCoversSelection(mute: false);
                break;
            case "vid-mute":
                s.SectionCoversSelection(mute: true);
                break;
        }
        return s;
    }
}
