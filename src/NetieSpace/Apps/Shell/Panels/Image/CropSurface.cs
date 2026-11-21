using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NetieSpace.Apps.Image;

namespace NetieSpace.Apps.Shell.ImagePanels;

/// <summary>
/// The picture with a crop box on it: drag the inside to move it, drag a corner (or, with a free shape, an edge) to
/// resize it, arrow keys nudge it. All of the geometry is <see cref="CropBox"/>; this only draws and forwards the pointer.
/// </summary>
internal sealed class CropSurface : FrameworkElement
{
    private const double Grab = 11;
    private ImageSource? _picture;
    private CropHandle _drag = CropHandle.None;
    private Point _start;
    private CropRect _startBox;

    public CropSurface()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        Cursor = Cursors.Arrow;
        System.Windows.Automation.AutomationProperties.SetName(this,
            "Crop box. Drag the box to move it, drag a corner to resize it, or use the arrow keys to nudge it.");
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    public CropRect Box { get; private set; } = new(0.1, 0.1, 0.8, 0.8);
    public string Shape { get; private set; } = "free";
    public int PixelWidth { get; private set; } = 1;
    public int PixelHeight { get; private set; } = 1;

    /// <summary>The box moved or changed size (raised on every pointer step of a drag).</summary>
    public event Action<CropRect>? BoxChanged;
    /// <summary>The pointer was released or a key was pressed: the box is settled.</summary>
    public event Action? Settled;

    public void Show(ImageSource? picture, int pixelWidth, int pixelHeight)
    {
        _picture = picture;
        PixelWidth = Math.Max(1, pixelWidth);
        PixelHeight = Math.Max(1, pixelHeight);
        InvalidateVisual();
    }

    public void SetBox(CropRect box, string shape)
    {
        Box = box;
        Shape = shape;
        InvalidateVisual();
    }

    /// <summary>The rectangle the picture occupies inside this element: as large as fits, centred.</summary>
    public Rect PictureArea()
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0)
            return Rect.Empty;
        var scale = Math.Min(w / PixelWidth, h / PixelHeight);
        var pw = PixelWidth * scale;
        var ph = PixelHeight * scale;
        return new Rect((w - pw) / 2, (h - ph) / 2, pw, ph);
    }

    private Rect BoxArea(Rect area) =>
        new(area.X + Box.X * area.Width, area.Y + Box.Y * area.Height, Box.W * area.Width, Box.H * area.Height);

    protected override void OnRender(DrawingContext dc)
    {
        var area = PictureArea();
        if (area.IsEmpty)
            return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_picture is not null)
            dc.DrawImage(_picture, area);

        var box = BoxArea(area);
        var dim = new SolidColorBrush(Color.FromArgb(150, 20, 22, 26));
        dim.Freeze();
        dc.DrawRectangle(dim, null, new Rect(area.X, area.Y, area.Width, Math.Max(0, box.Y - area.Y)));
        dc.DrawRectangle(dim, null, new Rect(area.X, box.Bottom, area.Width, Math.Max(0, area.Bottom - box.Bottom)));
        dc.DrawRectangle(dim, null, new Rect(area.X, box.Y, Math.Max(0, box.X - area.X), box.Height));
        dc.DrawRectangle(dim, null, new Rect(box.Right, box.Y, Math.Max(0, area.Right - box.Right), box.Height));

        var white = new Pen(Brushes.White, 2);
        white.Freeze();
        dc.DrawRectangle(null, white, box);

        var thirds = new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1);
        thirds.Freeze();
        for (var i = 1; i <= 2; i++)
        {
            dc.DrawLine(thirds, new Point(box.X + box.Width * i / 3, box.Y), new Point(box.X + box.Width * i / 3, box.Bottom));
            dc.DrawLine(thirds, new Point(box.X, box.Y + box.Height * i / 3), new Point(box.Right, box.Y + box.Height * i / 3));
        }

        var accent = TryFindResource("ShellAccent") as Brush ?? Brushes.Teal;
        var edge = new Pen(accent, 1.5);
        edge.Freeze();
        var cornersOnly = CropBox.Ratio(Shape) is not null;
        foreach (var p in HandlePoints(box, cornersOnly))
            dc.DrawRectangle(Brushes.White, edge, new Rect(p.X - 5, p.Y - 5, 10, 10));
    }

    private static IEnumerable<Point> HandlePoints(Rect b, bool cornersOnly)
    {
        yield return b.TopLeft;
        yield return b.TopRight;
        yield return b.BottomLeft;
        yield return b.BottomRight;
        if (cornersOnly) yield break;
        yield return new Point(b.X + b.Width / 2, b.Y);
        yield return new Point(b.X + b.Width / 2, b.Bottom);
        yield return new Point(b.X, b.Y + b.Height / 2);
        yield return new Point(b.Right, b.Y + b.Height / 2);
    }

    private CropHandle HitAt(Point p)
    {
        var area = PictureArea();
        if (area.IsEmpty) return CropHandle.None;
        var box = BoxArea(area);
        return CropBox.HitTest(box.X, box.Y, box.Width, box.Height, p.X, p.Y, Grab, CropBox.Ratio(Shape) is not null);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var p = e.GetPosition(this);
        _drag = HitAt(p);
        if (_drag == CropHandle.None)
            return;
        _start = p;
        _startBox = Box;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        if (_drag == CropHandle.None || !IsMouseCaptured)
        {
            Cursor = CursorFor(HitAt(p));
            return;
        }
        var area = PictureArea();
        if (area.IsEmpty) return;
        var next = CropBox.Resize(_startBox, _drag, (p.X - _start.X) / area.Width, (p.Y - _start.Y) / area.Height,
            PixelWidth, PixelHeight, Shape);
        Box = next;
        InvalidateVisual();
        BoxChanged?.Invoke(next);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag == CropHandle.None) return;
        _drag = CropHandle.None;
        ReleaseMouseCapture();
        Settled?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 0.05 : 0.01;
        double dx = 0, dy = 0;
        switch (e.Key)
        {
            case Key.Left: dx = -step; break;
            case Key.Right: dx = step; break;
            case Key.Up: dy = -step; break;
            case Key.Down: dy = step; break;
            default: return;
        }
        Box = CropBox.Move(Box, dx, dy);
        InvalidateVisual();
        BoxChanged?.Invoke(Box);
        Settled?.Invoke();
        e.Handled = true;
    }

    private static Cursor CursorFor(CropHandle h) => h switch
    {
        CropHandle.TopLeft or CropHandle.BottomRight => Cursors.SizeNWSE,
        CropHandle.TopRight or CropHandle.BottomLeft => Cursors.SizeNESW,
        CropHandle.Top or CropHandle.Bottom => Cursors.SizeNS,
        CropHandle.Left or CropHandle.Right => Cursors.SizeWE,
        CropHandle.Move => Cursors.SizeAll,
        _ => Cursors.Arrow,
    };
}
