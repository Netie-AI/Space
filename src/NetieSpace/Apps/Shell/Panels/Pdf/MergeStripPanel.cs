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
/// Merge PDF as a strip of file cards: first-page picture, page-count badge, drag to reorder, Ctrl+Left / Ctrl+Right
/// for the keyboard, drop files from Explorer anywhere on the page. The order of the cards is the order of the pages.
/// </summary>
internal sealed class MergeStripPanel : PanelBase, IToolPanel
{
    private const string DragFormat = "netie.pdf.file";

    private sealed class Info
    {
        public int Pages = -1;            // -1 = still reading
        public string? Problem;
        public BitmapSource? Thumb;
        public long Size = -1;
    }

    private readonly List<string> _files = new();
    private readonly Dictionary<string, Info> _info = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Task> _loads = new();
    private readonly StackPanel _root = new();
    private readonly TextBlock _summary;
    private readonly TextBlock _note;
    private readonly WrapPanel _strip = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly Border _zone;
    private readonly StackPanel _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 28, 0, 28) };
    private Point _downAt;
    private string? _downPath;
    private string? _focusAfter;

    public MergeStripPanel(PanelContext ctx) : base(ctx)
    {
        _summary = Text("", "ShellMutedText");
        _note = Text("", "ShellMutedText");
        _note.Foreground = Brush("ShellDanger");
        _note.Visibility = Visibility.Collapsed;

        var title = Text("Files to combine", "ShellSection");
        _root.Children.Add(title);
        _summary.Margin = new Thickness(0, 2, 0, 8);
        _root.Children.Add(_summary);

        var emptyText = Text("Drop PDFs here, in the order you want them", "ShellMutedText", 13);
        emptyText.HorizontalAlignment = HorizontalAlignment.Center;
        var browse = Button("Browse", "Choose PDF files", () => Ctx.Browse(), action: false);
        browse.HorizontalAlignment = HorizontalAlignment.Center;
        browse.Margin = new Thickness(0, 12, 0, 0);
        _empty.Children.Add(emptyText);
        _empty.Children.Add(browse);

        var inner = new Grid();
        inner.Children.Add(_strip);
        inner.Children.Add(_empty);
        _zone = new Border
        {
            Background = Brush("ShellSidebar"),
            BorderBrush = Brush("ShellThumb"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 12, 4, 0),
            MinHeight = 150,
            AllowDrop = true,
            Child = inner,
        };
        _zone.DragOver += OnZoneDragOver;
        _zone.Drop += OnZoneDrop;
        _root.Children.Add(_zone);
        _root.Children.Add(_note);
        AutomationProperties.SetName(_zone, "Files to combine. Drag cards to reorder.");
        Redraw();
    }

    public FrameworkElement View => _root;
    public IReadOnlyList<string> Files => _files;

    public override IReadOnlyCollection<string> HiddenOptions { get; } = Array.Empty<string>();

    public bool Ready(out string? why)
    {
        why = null;
        if (_files.Count < 2)
        {
            if (_files.Count == 1) why = "Add at least one more PDF to combine.";
            return false;
        }
        var bad = _files.FirstOrDefault(f => _info.TryGetValue(f, out var i) && i.Problem is not null);
        if (bad is not null)
        {
            why = IoPath.GetFileName(bad) + " cannot be read: " + _info[bad].Problem;
            return false;
        }
        return true;
    }

    public IReadOnlyDictionary<string, string> Options() => new Dictionary<string, string>();

    public string Describe() =>
        $"PANEL merge files={_files.Count} pages={_files.Sum(f => Math.Max(0, _info.GetValueOrDefault(f)?.Pages ?? 0))} " +
        $"thumbs={_files.Count(f => _info.GetValueOrDefault(f)?.Thumb is not null)} order={string.Join(",", _files.Select(IoPath.GetFileName))}";

    public void Show(IReadOnlyList<string> files)
    {
        if (files.Count == _files.Count && files.Zip(_files).All(p => string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase)))
            return;
        _files.Clear();
        Insert(files, 0, quiet: true);
    }

    public void AddFiles(IEnumerable<string> dropped) => Insert(dropped, _files.Count, quiet: false);

    private void Insert(IEnumerable<string> dropped, int at, bool quiet)
    {
        var verdict = PdfDropRules.Filter(dropped, IoFile.Exists);
        var fresh = verdict.Accepted.Where(f => !_files.Contains(f, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        at = Math.Clamp(at, 0, _files.Count);
        _files.InsertRange(at, fresh);
        foreach (var f in fresh) Read(f);
        var dup = verdict.Accepted.Count - fresh.Count;
        var message = verdict.Message ?? (dup > 0 ? (dup == 1 ? "1 file was already in the list." : dup + " files were already in the list.") : null);
        _note.Text = message ?? "";
        _note.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        Redraw();
        SettledTask = Task.WhenAll(_loads.ToArray());
        if (!quiet && fresh.Count > 0) RaiseChanged();
    }

    private void Read(string path)
    {
        var info = _info[path] = new Info { Size = SizeOf(path) };
        _loads.Add(ReadAsync(path, info, Cts.Token));
    }

    private async Task ReadAsync(string path, Info info, CancellationToken ct)
    {
        try
        {
            var (pages, problem) = await PdfThumbs.CountAsync(path);
            info.Pages = pages;
            info.Problem = problem;
            Redraw();
            RaiseChanged();   // the action state depends on a readable file
            if (problem is null)
            {
                info.Thumb = await PdfThumbs.RenderPageAsync(path, 0, 240, ct);
                Redraw();
            }
        }
        catch (OperationCanceledException)
        {
            // a newer list replaced this one
        }
    }

    private static long SizeOf(string path)
    {
        try { return new System.IO.FileInfo(path).Length; }
        catch (Exception) { return -1; }
    }

    // ---- drawing ----

    private void Redraw()
    {
        _strip.Children.Clear();
        _empty.Visibility = _files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < _files.Count; i++) _strip.Children.Add(Card(i));
        if (_files.Count > 0) _strip.Children.Add(AddCard());

        var pages = _files.Sum(f => Math.Max(0, _info.GetValueOrDefault(f)?.Pages ?? 0));
        var reading = _files.Any(f => _info.GetValueOrDefault(f) is { Pages: -1 });
        _summary.Text = _files.Count == 0
            ? "Drag PDFs onto this window, or press Browse. Drag the cards to put them in order."
            : $"{_files.Count} file{(_files.Count == 1 ? "" : "s")}" + (reading ? "" : $", {pages} page{(pages == 1 ? "" : "s")} in all")
              + ". Drag a card to reorder (or Ctrl+Left / Ctrl+Right). The first card comes first.";

        if (_focusAfter is { } p)
        {
            _focusAfter = null;
            var card = _strip.Children.OfType<Border>().FirstOrDefault(c => string.Equals(c.Tag as string, p, StringComparison.OrdinalIgnoreCase));
            card?.Dispatcher.BeginInvoke(new Action(() => card.Focus()), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private FrameworkElement Card(int index)
    {
        var path = _files[index];
        var info = _info.GetValueOrDefault(path) ?? new Info();
        var name = IoPath.GetFileName(path);

        var thumbBox = new Border { Height = 168, Background = Brushes.White, BorderBrush = Brush("ShellLine"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true };
        var thumbGrid = new Grid();
        if (info.Thumb is not null)
        {
            var img = new System.Windows.Controls.Image { Source = info.Thumb, Stretch = Stretch.Uniform, Margin = new Thickness(3) };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            thumbGrid.Children.Add(img);
        }
        else
        {
            var t = Text(info.Problem is not null ? "Cannot read" : info.Pages < 0 ? "Reading..." : "No preview", "ShellCaption");
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            if (info.Problem is not null) { t.Foreground = Brush("ShellDanger"); thumbBox.ToolTip = info.Problem; }
            thumbGrid.Children.Add(t);
        }
        thumbGrid.Children.Add(Badge((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), HorizontalAlignment.Left, accent: true));
        if (info.Pages >= 0)
            thumbGrid.Children.Add(Badge(info.Pages == 1 ? "1 page" : info.Pages + " pages", HorizontalAlignment.Right, accent: false));
        thumbBox.Child = thumbGrid;

        var title = Text(name, "ShellMutedText", 12.5, FontWeights.SemiBold);
        title.Foreground = Brush("ShellInk");
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.TextWrapping = TextWrapping.NoWrap;
        title.Margin = new Thickness(0, 8, 0, 0);
        title.ToolTip = path;
        var size = Text(info.Size >= 0 ? ToolPageModel.FormatSize(info.Size) : "", "ShellCaption");

        var remove = new Button { Content = "Remove", MinHeight = 24, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(remove, "Remove " + name);
        remove.Click += (_, _) => RemoveAt(_files.FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase)));

        var stack = new StackPanel();
        stack.Children.Add(thumbBox);
        stack.Children.Add(title);
        stack.Children.Add(size);
        stack.Children.Add(remove);

        var barLeft = InsertBar(vertical: true);
        var barRight = InsertBar(vertical: true);
        barRight.HorizontalAlignment = HorizontalAlignment.Right;
        var grid = new Grid();
        grid.Children.Add(stack);
        grid.Children.Add(barLeft);
        grid.Children.Add(barRight);

        var card = new Border
        {
            Width = 150,
            Margin = new Thickness(0, 0, 10, 12),
            Padding = new Thickness(8),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Tag = path,
            Focusable = true,
            AllowDrop = true,
            Cursor = Cursors.SizeAll,
            Child = grid,
        };
        AutomationProperties.SetName(card, $"{name}, {(info.Pages >= 0 ? info.Pages + " pages, " : "")}position {index + 1} of {_files.Count}. Ctrl+Left or Ctrl+Right moves it, Delete removes it.");

        card.PreviewMouseLeftButtonDown += (_, e) =>
        {
            card.Focus();
            _downAt = e.GetPosition(card);
            _downPath = path;
        };
        card.PreviewMouseLeftButtonUp += (_, _) => _downPath = null;
        card.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _downPath is null || !string.Equals(_downPath, path, StringComparison.OrdinalIgnoreCase)) return;
            if (e.OriginalSource is DependencyObject d && InsideButton(d, card)) return;
            var now = e.GetPosition(card);
            if (Math.Abs(now.X - _downAt.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(now.Y - _downAt.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _downPath = null;
            card.Opacity = 0.55;
            try { DragDrop.DoDragDrop(card, new DataObject(DragFormat, path), DragDropEffects.Move); }
            finally { card.Opacity = 1; HideBars(); }
        };
        card.DragOver += (_, e) => OnCardDrag(card, barLeft, barRight, e);
        card.DragLeave += (_, _) => { barLeft.Visibility = Visibility.Collapsed; barRight.Visibility = Visibility.Collapsed; };
        card.Drop += (_, e) => OnCardDrop(card, path, e);
        card.KeyDown += (_, e) => OnCardKey(path, e);
        card.GotKeyboardFocus += (_, _) => card.BorderBrush = Brush("ShellAccent");
        card.LostKeyboardFocus += (_, _) => card.BorderBrush = Brushes.Transparent;
        return card;
    }

    private FrameworkElement AddCard()
    {
        var plus = Text("+", "ShellSection", 28);
        plus.HorizontalAlignment = HorizontalAlignment.Center;
        var label = Text("Add PDFs", "ShellMutedText");
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(plus);
        stack.Children.Add(label);
        var b = new Button { Content = stack, Width = 118, Height = 168, Margin = new Thickness(8, 8, 10, 12), VerticalAlignment = VerticalAlignment.Top };
        b.ToolTip = "Add more PDFs";
        AutomationProperties.SetName(b, "Add more PDFs");
        b.Click += (_, _) => Ctx.Browse();
        return b;
    }

    private Border Badge(string text, HorizontalAlignment side, bool accent)
    {
        var t = Text(text, "ShellCaption", 11, FontWeights.SemiBold);
        t.Foreground = accent ? Brush("ShellAccentInk") : Brush("ShellInk");
        t.TextWrapping = TextWrapping.NoWrap;
        return new Border
        {
            Background = accent ? Brush("ShellAccent") : new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
            BorderBrush = accent ? Brushes.Transparent : Brush("ShellLine"),
            BorderThickness = new Thickness(accent ? 0 : 1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(7, 1, 7, 2),
            Margin = new Thickness(6),
            HorizontalAlignment = side,
            VerticalAlignment = VerticalAlignment.Top,
            Child = t,
            IsHitTestVisible = false,
        };
    }

    private static bool InsideButton(DependencyObject d, DependencyObject stopAt)
    {
        for (var cur = d; cur is not null && !ReferenceEquals(cur, stopAt); cur = System.Windows.Media.VisualTreeHelper.GetParent(cur) ?? LogicalTreeHelper.GetParent(cur))
            if (cur is Button) return true;
        return false;
    }

    // ---- editing ----

    private void RemoveAt(int index)
    {
        if (index < 0 || index >= _files.Count) return;
        _info.Remove(_files[index]);
        _files.RemoveAt(index);
        _note.Visibility = Visibility.Collapsed;
        Redraw();
        RaiseChanged();
    }

    private void MoveTo(int from, int insertBefore)
    {
        if (from < 0 || from >= _files.Count) return;
        var path = _files[from];
        var before = _files.ToList();
        PdfPagePlan.MoveBlock(_files, new[] { from }, insertBefore);
        if (_files.SequenceEqual(before)) return;
        _focusAfter = path;
        Redraw();
        RaiseChanged();
    }

    private void OnCardKey(string path, KeyEventArgs e)
    {
        var i = _files.FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (ctrl && e.Key == Key.Left) { MoveTo(i, i - 1); e.Handled = true; }
        else if (ctrl && e.Key == Key.Right) { MoveTo(i, i + 2); e.Handled = true; }
        else if (e.Key == Key.Delete) { RemoveAt(i); e.Handled = true; }
        else if (e.Key == Key.Left && i > 0) { FocusCard(i - 1); e.Handled = true; }
        else if (e.Key == Key.Right && i < _files.Count - 1) { FocusCard(i + 1); e.Handled = true; }
    }

    private void FocusCard(int i)
    {
        if (i < 0 || i >= _strip.Children.Count) return;
        if (_strip.Children[i] is UIElement c) c.Focus();
    }

    // ---- drag and drop ----

    private void HideBars()
    {
        foreach (var c in _strip.Children.OfType<Border>())
            if (c.Child is Grid g)
                foreach (var b in g.Children.OfType<Border>()) b.Visibility = Visibility.Collapsed;
    }

    private static bool RightHalf(FrameworkElement card, DragEventArgs e) => e.GetPosition(card).X > card.ActualWidth / 2;

    private void OnCardDrag(Border card, Border barLeft, Border barRight, DragEventArgs e)
    {
        var internalDrag = e.Data.GetDataPresent(DragFormat);
        if (!internalDrag && !HasFiles(e)) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        e.Effects = internalDrag ? DragDropEffects.Move : DragDropEffects.Copy;
        var right = RightHalf(card, e);
        barLeft.Visibility = right ? Visibility.Collapsed : Visibility.Visible;
        barRight.Visibility = right ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnCardDrop(Border card, string path, DragEventArgs e)
    {
        var target = _files.FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        if (target < 0) return;
        var at = target + (RightHalf(card, e) ? 1 : 0);
        HideBars();
        if (e.Data.GetData(DragFormat) is string moved)
        {
            MoveTo(_files.FindIndex(f => string.Equals(f, moved, StringComparison.OrdinalIgnoreCase)), at);
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
        {
            Insert(dropped, at, quiet: false);
        }
        e.Handled = true;
    }

    private void OnZoneDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnZoneDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DragFormat) is string moved)
            MoveTo(_files.FindIndex(f => string.Equals(f, moved, StringComparison.OrdinalIgnoreCase)), _files.Count);
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
            Insert(dropped, _files.Count, quiet: false);
        e.Handled = true;
    }
}
