using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NetieSpace.Apps.Pdf;
using NetieSpace.Helpers;

namespace NetieSpace.Apps.Shell.Panels.Pdf;

/// <summary>
/// Sign PDF: a page preview you click to say where the signature goes, instead of typing a position. The click becomes
/// the "at" option (centre of the mark, percent of the page) and "pages" is the page on show. A ghost of the mark follows
/// the click at the size the Size option will give it, so what is placed is what is written, give or take the font.
/// </summary>
internal sealed class SignPlacePanel : PanelBase, IToolPanel
{
    private const int PreviewWidth = 420;
    private const double EdgePt = 24;   // keep in step with PdfSignSelfTool.Edge

    private readonly List<string> _files = new();
    private readonly StackPanel _root = new();
    private readonly TextBlock _summary;
    private readonly TextBlock _pageLabel;
    private readonly Button _prev;
    private readonly Button _next;
    private readonly CheckBox _every;
    private readonly Grid _stage = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly System.Windows.Controls.Image _img = new() { Stretch = Stretch.Fill };
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent, Cursor = Cursors.Cross, Focusable = true, FocusVisualStyle = null };
    private readonly Border _ghost = new();
    private readonly TextBlock _ghostText = new();
    private readonly StackPanel _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 36, 0, 36) };
    private readonly Border _zone;
    private int _pageCount;
    private int _page = 1;
    private double _wPt = 612, _hPt = 792;
    private (double X, double Y)? _at;
    private int _atPage;
    private bool _drawn;

    public SignPlacePanel(PanelContext ctx) : base(ctx)
    {
        _root.Children.Add(Text("Place your signature", "ShellSection"));
        _summary = Text("", "ShellMutedText");
        _summary.Margin = new Thickness(0, 2, 0, 10);
        _root.Children.Add(_summary);

        _prev = Button("Previous page", "Show the previous page", () => Go(-1));
        _next = Button("Next page", "Show the next page", () => Go(1));
        _pageLabel = Text("", "ShellMutedText", 13);
        _pageLabel.VerticalAlignment = VerticalAlignment.Center;
        _pageLabel.Margin = new Thickness(0, 0, 14, 0);
        _every = new CheckBox { Content = "Sign every page", VerticalAlignment = VerticalAlignment.Center, ToolTip = "Put the same mark on every page" };
        _every.Click += (_, _) => { DrawGhost(); UpdateSummary(); RaiseChanged(); };
        var nav = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        nav.Children.Add(_prev);
        nav.Children.Add(_next);
        nav.Children.Add(_pageLabel);
        nav.Children.Add(_every);
        _root.Children.Add(nav);

        _ghostText.FontStyle = FontStyles.Italic;
        _ghostText.Foreground = new SolidColorBrush(Color.FromRgb(20, 40, 120));
        _ghostText.HorizontalAlignment = HorizontalAlignment.Center;
        _ghostText.VerticalAlignment = VerticalAlignment.Center;
        _ghostText.TextTrimming = TextTrimming.CharacterEllipsis;
        _ghost.Child = _ghostText;
        _ghost.BorderBrush = Brush("ShellAccent");
        _ghost.BorderThickness = new Thickness(1.5);
        _ghost.CornerRadius = new CornerRadius(3);
        _ghost.Background = new SolidColorBrush(Color.FromArgb(46, 94, 196, 184));
        _ghost.IsHitTestVisible = false;
        _ghost.Visibility = Visibility.Collapsed;
        _canvas.Children.Add(_ghost);
        _stage.Children.Add(_img);
        _stage.Children.Add(_canvas);
        RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);
        _canvas.MouseLeftButtonDown += OnClick;
        _canvas.KeyDown += OnKey;
        AutomationProperties.SetName(_canvas, "Page preview. Click where the signature goes, or use the arrow keys.");

        var shadow = new Border { Background = Brushes.White, BorderBrush = Brush("ShellLine"), BorderThickness = new Thickness(1), Child = _stage, HorizontalAlignment = HorizontalAlignment.Left };
        var emptyText = Text("Drop the PDF to sign", "ShellMutedText", 13);
        emptyText.HorizontalAlignment = HorizontalAlignment.Center;
        var browse = Button("Browse", "Choose a PDF", () => Ctx.Browse());
        browse.Margin = new Thickness(0, 12, 0, 0);
        browse.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.Children.Add(emptyText);
        _empty.Children.Add(browse);
        var inner = new Grid();
        inner.Children.Add(shadow);
        inner.Children.Add(_empty);
        _zone = new Border
        {
            Background = Brush("ShellSidebar"),
            BorderBrush = Brush("ShellThumb"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            MinHeight = 150,
            AllowDrop = true,
            Child = inner,
        };
        _zone.DragOver += (_, e) => { e.Effects = HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        _zone.Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] d) AddFiles(d); e.Handled = true; };
        _root.Children.Add(_zone);
        Reset();
    }

    public FrameworkElement View => _root;
    public IReadOnlyList<string> Files => _files;
    public override IReadOnlyCollection<string> HiddenOptions { get; } = new[] { "position", "pages", "at" };

    public bool Ready(out string? why)
    {
        why = null;
        if (_files.Count == 0) return false;   // the empty panel already says what to do
        if (_at is null) { why = "Click on the page where the signature goes."; return false; }
        return true;
    }

    public IReadOnlyDictionary<string, string> Options() =>
        _at is { } p
            ? new Dictionary<string, string>
            {
                ["at"] = PdfSignPlacement.Format(p.X, p.Y),
                ["pages"] = _every.IsChecked == true ? "1-end" : _atPage.ToString(CultureInfo.InvariantCulture),
            }
            : new Dictionary<string, string>();

    public string Describe() =>
        $"PANEL sign files={_files.Count} pages={_pageCount} page={_page} thumbs={(_drawn ? 1 : 0)} at={(_at is { } p ? PdfSignPlacement.Format(p.X, p.Y) : "none")} on={_atPage}";

    public void Show(IReadOnlyList<string> files)
    {
        if (files.Count == _files.Count && files.Zip(_files).All(p => string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase)))
            return;
        SettledTask = LoadAsync(files.Count > 0 ? files[0] : null);
    }

    public void AddFiles(IEnumerable<string> dropped)
    {
        var verdict = PdfDropRules.Filter(dropped, IoFile.Exists);
        if (verdict.Accepted.Count == 0)
        {
            _summary.Text = verdict.Message ?? "That is not a PDF.";
            return;
        }
        SettledTask = LoadAsync(verdict.Accepted[0]);
    }

    private async Task LoadAsync(string? path)
    {
        Cancel();
        var ct = Cts.Token;
        Reset();
        if (path is null) { RaiseChanged(); return; }
        try
        {
            var (pages, problem) = await PdfThumbs.CountAsync(path);
            ct.ThrowIfCancellationRequested();
            if (problem is not null || pages == 0)
            {
                _summary.Text = IoPath.GetFileName(path) + " cannot be read: " + (problem ?? "it has no pages");
                return;
            }
            _files.Add(path);
            _pageCount = pages;
            _page = pages;   // signatures go at the end
            _empty.Visibility = Visibility.Collapsed;
            RaiseChanged();
            await DrawPageAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // replaced by a newer file
        }
    }

    private void Reset()
    {
        _files.Clear();
        _pageCount = 0;
        _page = 1;
        _at = null;
        _atPage = 0;
        _drawn = false;
        _img.Source = null;
        _img.Width = _img.Height = 0;
        _canvas.Width = _canvas.Height = 0;
        _ghost.Visibility = Visibility.Collapsed;
        _empty.Visibility = Visibility.Visible;
        UpdateSummary();
    }

    private async Task DrawPageAsync(CancellationToken ct)
    {
        if (_files.Count == 0) return;
        _pageLabel.Text = $"Page {_page} of {_pageCount}";
        var r = await PdfThumbs.RenderSizedAsync(_files[0], _page - 1, PreviewWidth * 2, ct);
        ct.ThrowIfCancellationRequested();
        if (r is null)
        {
            _drawn = false;
            _summary.Text = "No preview for this page. You can still sign: press an arrow key to place the mark.";
            _img.Width = PreviewWidth;
            _img.Height = PreviewWidth * 792 / 612;
            _canvas.Width = _img.Width;
            _canvas.Height = _img.Height;
            DrawGhost();
            return;
        }
        var (bmp, w, h) = r.Value;
        _wPt = w;
        _hPt = h;
        _img.Source = bmp;
        _img.Width = PreviewWidth;
        _img.Height = Math.Round(PreviewWidth * h / Math.Max(1, w));
        _canvas.Width = _img.Width;
        _canvas.Height = _img.Height;
        _drawn = true;
        DrawGhost();
        UpdateSummary();
    }

    private void Go(int delta)
    {
        if (_files.Count == 0) return;
        var to = Math.Clamp(_page + delta, 1, _pageCount);
        if (to == _page) return;
        _page = to;
        SettledTask = DrawPageAsync(Cts.Token);
    }

    // ---- placing ----

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (_files.Count == 0) return;
        _canvas.Focus();
        var p = e.GetPosition(_canvas);
        Place(p.X / Math.Max(1, _canvas.ActualWidth) * 100, p.Y / Math.Max(1, _canvas.ActualHeight) * 100);
        e.Handled = true;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_files.Count == 0) return;
        var step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 5.0 : 1.0;
        var (x, y) = _at ?? (50, 85);
        switch (e.Key)
        {
            case Key.Left: x -= step; break;
            case Key.Right: x += step; break;
            case Key.Up: y -= step; break;
            case Key.Down: y += step; break;
            default: return;
        }
        Place(Math.Clamp(x, 0, 100), Math.Clamp(y, 0, 100));
        e.Handled = true;
    }

    private void Place(double xPercent, double yPercent)
    {
        _at = (Math.Clamp(xPercent, 0, 100), Math.Clamp(yPercent, 0, 100));
        _atPage = _page;
        DrawGhost();
        UpdateSummary();
        RaiseChanged();
    }

    private double WidthFraction => Ctx.ReadOption("size") switch { "small" => 0.16, "large" => 0.36, _ => 0.25 };

    private void DrawGhost()
    {
        var visible = _at is not null && (_every.IsChecked == true || _atPage == _page) && _canvas.Width > 0;
        _ghost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible || _at is not { } at) return;

        var image = Ctx.ReadOption("mode") == "image";
        var name = Ctx.ReadOption("name").Trim();
        _ghostText.Text = image ? "(your picture)" : name.Length > 0 ? name : "Your name";
        var blockW = _wPt * WidthFraction;
        var blockH = blockW * (image ? 0.4 : 0.25) + (Ctx.ReadOption("date") == "true" ? 12 : 0);
        var (left, top) = PdfSignPlacement.BlockOrigin(at.X, at.Y, _wPt, _hPt, blockW, blockH, EdgePt);
        var k = _canvas.Width / _wPt;
        _ghost.Width = blockW * k;
        _ghost.Height = blockH * k;
        _ghostText.FontSize = Math.Max(8, _ghost.Height * 0.6);
        Canvas.SetLeft(_ghost, left * k);
        Canvas.SetTop(_ghost, top * k);
    }

    private void UpdateSummary()
    {
        _prev.IsEnabled = _files.Count > 0 && _page > 1;
        _next.IsEnabled = _files.Count > 0 && _page < _pageCount;
        _every.IsEnabled = _files.Count > 0;
        if (_files.Count == 0) { _pageLabel.Text = ""; _summary.Text = "Drop a PDF, then click on the page where the signature goes."; return; }
        _pageLabel.Text = $"Page {_page} of {_pageCount}";
        _summary.Text = _at is { } p
            ? $"Signature on {(_every.IsChecked == true ? "every page" : "page " + _atPage)}, {Math.Round(p.X)}% across and {Math.Round(p.Y)}% down. Click again to move it."
            : "Click on the page where the signature goes. The mark is centred on the spot you click.";
    }

    /// <summary>"page=3;at=62.5,88;all": what a click and the page buttons would have done.</summary>
    public override void ApplyState(string state)
    {
        foreach (var raw in state.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = raw.IndexOf('=');
            var key = (eq < 0 ? raw : raw[..eq]).ToLowerInvariant();
            var val = eq < 0 ? "" : raw[(eq + 1)..];
            if (key == "page" && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pg) && _files.Count > 0)
            {
                _page = Math.Clamp(pg, 1, _pageCount);
                SettledTask = DrawPageAsync(Cts.Token);
            }
            else if (key == "at" && PdfSignPlacement.TryParse(val, out var x, out var y))
                Place(x, y);
            else if (key == "all")
                _every.IsChecked = true;
        }
        DrawGhost();
        UpdateSummary();
    }
}
