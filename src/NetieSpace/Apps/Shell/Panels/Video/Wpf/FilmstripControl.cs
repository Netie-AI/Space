using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;

namespace NetieSpace.Apps.Shell.Panels.Video;

/// <summary>
/// The strip of thumbnails with the playhead and the draggable handles (W6-4). It draws a <see cref="FlowState"/> and forwards
/// the mouse to <see cref="FlowGeometry"/>; every rule (clamping, what a press grabs) lives in the WPF-free model.
/// Everything is drawn in <see cref="OnRender"/>: one element, no layout to get wrong.
/// </summary>
internal sealed class FilmstripControl : FrameworkElement
{
    public const double ControlHeight = 122;

    private FlowState? _state;
    private IReadOnlyList<ImageSource?> _frames = Array.Empty<ImageSource?>();
    private FlowHit _drag = FlowHit.None;

    /// <summary>A handle or the playhead moved. <c>final</c> is true on mouse up.</summary>
    public event Action<FlowHit, bool>? Edited;

    public FilmstripControl()
    {
        Height = ControlHeight;
        Focusable = false;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        AutomationProperties.SetName(this, "Filmstrip");
        AutomationProperties.SetHelpText(this, "Drag the two end handles to choose what to keep. Click to move the playhead.");
    }

    public void Bind(FlowState state, IReadOnlyList<ImageSource?> frames)
    {
        _state = state;
        _frames = frames;
        InvalidateVisual();
    }

    public void Redraw() => InvalidateVisual();

    protected override Size MeasureOverride(Size available) =>
        new(double.IsInfinity(available.Width) ? 600 : available.Width, ControlHeight);

    private Brush Res(string key, Color fallback) =>
        TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    private FormattedText Text(string s, double size, Brush brush, FontWeight? weight = null) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        if (w < 40) return;
        var accent = Res("ShellAccent", Color.FromRgb(0x2F, 0x6F, 0xED));
        var muted = Res("ShellMuted", Color.FromRgb(0x6E, 0x6E, 0x6E));
        var line = Res("ShellLine", Color.FromRgb(0xD6, 0xD6, 0xD6));
        var sidebar = Res("ShellSidebar", Color.FromRgb(0xF4, 0xF5, 0xF7));
        var accentInk = Res("ShellAccentInk", Colors.White);

        var x0 = FlowGeometry.Padding;
        var x1 = w - FlowGeometry.Padding;
        var strip = new Rect(x0, 0, Math.Max(1, x1 - x0), FlowGeometry.StripHeight);
        dc.DrawRoundedRectangle(sidebar, null, strip, 4, 4);

        var s = _state;
        // Thumbnails, each cropped to fill its cell.
        if (_frames.Count > 0)
        {
            var cellW = strip.Width / _frames.Count;
            for (var i = 0; i < _frames.Count; i++)
            {
                var cell = new Rect(strip.X + i * cellW, 0, cellW, strip.Height);
                if (_frames[i] is not { } img || img.Height <= 0) continue;
                var cellAspect = cell.Width / cell.Height;
                var imgAspect = img.Width / img.Height;
                var dw = imgAspect > cellAspect ? cell.Height * imgAspect : cell.Width;
                var dh = imgAspect > cellAspect ? cell.Height : cell.Width / imgAspect;
                dc.PushClip(new RectangleGeometry(cell));
                dc.DrawImage(img, new Rect(cell.X + (cell.Width - dw) / 2, cell.Y + (cell.Height - dh) / 2, dw, dh));
                dc.Pop();
            }
        }
        dc.DrawRoundedRectangle(null, new Pen(line, 1), strip, 4, 4);
        if (s is null) return;

        double X(double t) => FlowGeometry.TimeToX(t, w, s.Duration);
        var xi = X(s.In);
        var xo = X(s.Out);

        // What is cut away is dimmed; what is kept is framed in the accent colour with a grip on each end.
        var dim = new SolidColorBrush(Color.FromArgb(150, 20, 20, 24));
        if (xi > x0) dc.DrawRectangle(dim, null, new Rect(x0, 0, xi - x0, strip.Height));
        if (xo < x1) dc.DrawRectangle(dim, null, new Rect(xo, 0, x1 - xo, strip.Height));
        dc.DrawRectangle(accent, null, new Rect(xi, 0, Math.Max(1, xo - xi), 3));
        dc.DrawRectangle(accent, null, new Rect(xi, strip.Height - 3, Math.Max(1, xo - xi), 3));
        DrawGrip(dc, accent, accentInk, xi - 10, 0, 10, strip.Height, left: true);
        DrawGrip(dc, accent, accentInk, xo, 0, 10, strip.Height, left: false);

        // Playhead.
        var xp = X(s.Playhead);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), null, new Rect(xp - 2, 0, 4, strip.Height + 6));
        dc.DrawRectangle(Brushes.White, null, new Rect(xp - 1, 0, 2, strip.Height + 6));
        dc.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), 1), new Point(xp, strip.Height + 8), 4, 4);

        // Sections under the strip.
        if (s.MuteOn) DrawSection(dc, FlowGeometry.MuteTop, FlowGeometry.MuteBottom, X(s.MuteFrom), X(s.MuteTo),
            Color.FromRgb(0xC6, 0x3B, 0x34), "Muted", sidebar, x0, x1);
        if (s.SpeedOn) DrawSection(dc, FlowGeometry.SpeedTop, FlowGeometry.SpeedBottom, X(s.SpeedFrom), X(s.SpeedTo),
            Color.FromRgb(0xC9, 0x84, 0x0B), s.SpeedPercent.ToString(CultureInfo.InvariantCulture) + "%", sidebar, x0, x1);

        // Time ruler.
        var step = FlowGeometry.TickStep(s.Duration, w);
        for (var t = 0.0; t <= s.Duration + 1e-6; t += step)
        {
            var x = X(t);
            dc.DrawRectangle(muted, null, new Rect(x, 106, 1, 4));
            var label = Text(Tick(t), 10, muted);
            var lx = Math.Clamp(x - label.Width / 2, 0, Math.Max(0, w - label.Width));
            dc.DrawText(label, new Point(lx, 108));
        }
    }

    private static string Tick(double t)
    {
        var whole = (long)Math.Round(t);
        var h = whole / 3600;
        var m = whole / 60 % 60;
        var sec = whole % 60;
        var inv = CultureInfo.InvariantCulture;
        return h > 0 ? $"{h.ToString(inv)}:{m.ToString("00", inv)}:{sec.ToString("00", inv)}" : $"{(whole / 60).ToString(inv)}:{sec.ToString("00", inv)}";
    }

    private void DrawGrip(DrawingContext dc, Brush accent, Brush ink, double x, double y, double width, double height, bool left)
    {
        var r = new Rect(x, y, width, height);
        dc.DrawRoundedRectangle(accent, null, r, left ? 4 : 1, left ? 4 : 1);
        var cx = x + width / 2;
        dc.DrawRectangle(ink, null, new Rect(cx - 2, y + height / 2 - 9, 1.5, 18));
        dc.DrawRectangle(ink, null, new Rect(cx + 0.5, y + height / 2 - 9, 1.5, 18));
    }

    private void DrawSection(DrawingContext dc, double top, double bottom, double xa, double xb, Color color, string label,
        Brush track, double x0, double x1)
    {
        var height = bottom - top;
        dc.DrawRoundedRectangle(track, null, new Rect(x0, top, x1 - x0, height), 3, 3);
        var bar = new Rect(xa, top, Math.Max(2, xb - xa), height);
        dc.DrawRoundedRectangle(new SolidColorBrush(color), null, bar, 3, 3);
        dc.DrawRectangle(Brushes.White, null, new Rect(xa, top - 1, 3, height + 2));
        dc.DrawRectangle(Brushes.White, null, new Rect(xb - 3, top - 1, 3, height + 2));
        if (bar.Width > 56)
        {
            var t = Text(label, 10.5, Brushes.White, FontWeights.SemiBold);
            dc.DrawText(t, new Point(bar.X + (bar.Width - t.Width) / 2, top + (height - t.Height) / 2));
        }
    }

    // ---------------------------------------------------------------- mouse

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_state is null) return;
        var p = e.GetPosition(this);
        _drag = FlowGeometry.HitTest(p.X, p.Y, ActualWidth, _state);
        if (_drag == FlowHit.None) return;
        CaptureMouse();
        Apply(p.X, final: false);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_state is null) return;
        var p = e.GetPosition(this);
        if (_drag != FlowHit.None)
        {
            Apply(p.X, final: false);
            return;
        }
        var hover = FlowGeometry.HitTest(p.X, p.Y, ActualWidth, _state);
        Cursor = hover switch
        {
            FlowHit.None => Cursors.Arrow,
            FlowHit.Seek => Cursors.Hand,
            _ => Cursors.SizeWE,
        };
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag == FlowHit.None) return;
        var p = e.GetPosition(this);
        Apply(p.X, final: true);
        _drag = FlowHit.None;
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_drag == FlowHit.None) return;
        var hit = _drag;
        _drag = FlowHit.None;
        Edited?.Invoke(hit, true);
    }

    private void Apply(double x, bool final)
    {
        if (_state is null) return;
        FlowGeometry.Drag(_state, _drag, FlowGeometry.XToTime(x, ActualWidth, _state.Duration));
        InvalidateVisual();
        Edited?.Invoke(_drag, final);
    }
}
