using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NetieSpace.Apps.Pdf;
using NetieSpace.Apps.Tools;
using NetieSpace.Helpers;

namespace NetieSpace.Apps.Shell.Panels.Pdf;

/// <summary>
/// Organize / Remove / Rotate as a grid of real page pictures: click, Shift+click and Ctrl+click select, rotate and
/// delete act on the selection, drag moves it, a "Blank?" badge flags pages that look empty. The grid is a
/// <see cref="PageGridModel"/>; Apply hands its plan (or page list) to the engine, which writes one file.
/// </summary>
internal sealed class PageGridPanel : PanelBase, IToolPanel
{
    private const string DragFormat = "netie.pdf.pages";
    private const int ThumbWidth = 200;

    private sealed class TilePart
    {
        public System.Windows.Controls.Image? Image;
        public TextBlock? Placeholder;
        public Border? BlankBadge;
    }

    private readonly PageGridRules _rules;
    private readonly PageGridModel _model;
    private readonly List<string> _files = new();
    private readonly Dictionary<(int File, int Page), (BitmapSource Image, bool Blank)> _thumbs = new();
    private readonly Dictionary<(int File, int Page), TilePart> _parts = new();
    private readonly StackPanel _root = new();
    private readonly TextBlock _summary;
    private readonly TextBlock _note;
    private readonly WrapPanel _toolbar = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly WrapPanel _wrap = new() { Margin = new Thickness(2, 2, 0, 0) };
    private readonly ScrollViewer _scroll;
    private readonly Border _zone;
    private readonly StackPanel _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 36, 0, 36) };
    private readonly Dictionary<string, Button> _buttons = new();
    private bool _loading;
    private int _cursor = -1;
    private int _pressIndex = -1;
    private bool _pressApplied;
    private Point _pressAt;
    private RegisteredTool? _runTool;

    public PageGridPanel(PanelContext ctx, PageGridRules rules) : base(ctx)
    {
        _rules = rules;
        _model = new PageGridModel(rules);
        _summary = Text("", "ShellMutedText");
        _note = Text("", "ShellMutedText");
        _note.Foreground = Brush("ShellDanger");
        _note.Visibility = Visibility.Collapsed;

        _root.Children.Add(Text(rules.CanReorder ? "Pages" : rules.CanDelete ? "Pages to keep" : "Pages", "ShellSection"));
        _summary.Margin = new Thickness(0, 2, 0, 10);
        _root.Children.Add(_summary);
        BuildToolbar();
        _root.Children.Add(_toolbar);

        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 560,
            Focusable = true,
            FocusVisualStyle = null,
            Content = _wrap,
        };
        _scroll.KeyDown += OnKey;
        _scroll.PreviewMouseWheel += ForwardWheelAtEnds;
        _scroll.DragOver += OnScrollDrag;
        var emptyText = Text("Drop a PDF here", "ShellMutedText", 13);
        emptyText.HorizontalAlignment = HorizontalAlignment.Center;
        var browse = Button("Browse", "Choose a PDF", () => Ctx.Browse());
        browse.HorizontalAlignment = HorizontalAlignment.Center;
        browse.Margin = new Thickness(0, 12, 0, 0);
        _empty.Children.Add(emptyText);
        _empty.Children.Add(browse);

        var inner = new Grid();
        inner.Children.Add(_scroll);
        inner.Children.Add(_empty);
        _zone = new Border
        {
            Background = Brush("ShellSidebar"),
            BorderBrush = Brush("ShellThumb"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 10, 2, 2),
            MinHeight = 150,
            AllowDrop = true,
            Child = inner,
        };
        _zone.DragOver += OnZoneDragOver;
        _zone.Drop += OnZoneDrop;
        _root.Children.Add(_zone);
        _root.Children.Add(_note);
        AutomationProperties.SetName(_zone, "Pages. Click to select, drag to reorder.");
        Refresh();
    }

    public FrameworkElement View => _root;
    public IReadOnlyList<string> Files => _files;

    public override IReadOnlyCollection<string> HiddenOptions =>
        _rules.CanReorder ? new[] { "plan" }
        : _rules.CanDelete ? new[] { "pages" }
        : new[] { "direction", "pages", "filter" };

    /// <summary>Rotate has no per-page directions of its own, so it writes through the organizer's plan.</summary>
    public override RegisteredTool? RunTool =>
        _rules.CanReorder || _rules.CanDelete ? null : _runTool ??= ToolRegistry.Default.Find("pdf-organize");

    public IReadOnlyDictionary<string, string> Options()
    {
        if (_rules.CanReorder || !_rules.CanDelete)
        {
            // Rotate with nothing turned yet does what the tool always did: every page a quarter turn right.
            var plan = !_rules.CanReorder && !_model.IsChanged
                ? PdfPagePlan.ToPlanString(_model.Pages.Select(p => p with { Rotation = 90 }).ToList())
                : _model.PlanString();
            return new Dictionary<string, string> { ["plan"] = plan };
        }
        return new Dictionary<string, string> { ["pages"] = _model.RemovedSpec() };
    }

    public bool Ready(out string? why)
    {
        why = null;
        if (_loading) { why = "Reading pages..."; return false; }
        if (_files.Count == 0 || _model.FileLengths.Sum() == 0) return false;   // the empty panel already says what to do
        if (_model.Pages.Count == 0) { why = "Every page is removed. Undo, or Reset."; return false; }
        if (!_rules.CanReorder && _rules.CanDelete)
        {
            if (_model.RemovedSpec().Length == 0) { why = "Select the pages to remove, then press Delete."; return false; }
            return true;
        }
        if (!_model.IsChanged && _files.Count < 2 && _rules.CanReorder)
        {
            why = "Rotate, delete or drag some pages first.";
            return false;
        }
        return true;
    }

    public string Describe()
    {
        var blanks = _model.Pages.Count(p => !p.IsBlankInsert && _thumbs.TryGetValue((p.File, p.Page), out var t) && t.Blank);
        var tool = _rules.CanReorder ? "organize" : _rules.CanDelete ? "remove" : "rotate";
        return $"PANEL {tool} files={_files.Count} pages={_model.Pages.Count} thumbs={_thumbs.Count} blank={blanks} selected={_model.Selected.Count} plan={_model.PlanString()}";
    }

    // ---- files in and out ----

    public void Show(IReadOnlyList<string> files)
    {
        if (files.Count == _files.Count && files.Zip(_files).All(p => string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase)))
            return;
        SettledTask = LoadAsync(files.ToList());
    }

    public void AddFiles(IEnumerable<string> dropped)
    {
        var verdict = PdfDropRules.Filter(dropped, IoFile.Exists);
        SetNote(verdict.Message);
        if (verdict.Accepted.Count == 0) return;
        if (_rules.CanReorder && _files.Count > 0)
        {
            var fresh = verdict.Accepted.Where(f => !_files.Contains(f, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (fresh.Count > 0) SettledTask = AppendAsync(fresh);
        }
        else
        {
            // One file at a time: the grid is that file's pages.
            SettledTask = LoadAsync(new List<string> { verdict.Accepted[0] });
        }
    }

    private async Task LoadAsync(List<string> files)
    {
        Cancel();
        var ct = Cts.Token;
        _loading = true;
        _thumbs.Clear();
        _files.Clear();
        _model.Load(Array.Empty<int>());
        Refresh();
        var kept = new List<string>();
        var counts = new List<int>();
        try
        {
            foreach (var f in files)
            {
                var (pages, problem) = await PdfThumbs.CountAsync(f);
                ct.ThrowIfCancellationRequested();
                if (problem is not null || pages == 0)
                {
                    SetNote(IoPath.GetFileName(f) + " cannot be read: " + (problem ?? "it has no pages"));
                    continue;
                }
                kept.Add(f);
                counts.Add(pages);
            }
            _files.AddRange(kept);
            _model.Load(counts);
            _loading = false;
            Refresh();
            RaiseChanged();
            for (var i = 0; i < kept.Count; i++)
                await DrawFileAsync(i + 1, kept[i], ct);
        }
        catch (OperationCanceledException)
        {
            // a newer file list replaced this one
        }
        finally
        {
            if (!ct.IsCancellationRequested) { _loading = false; RaiseChanged(); }
        }
    }

    private async Task AppendAsync(List<string> fresh)
    {
        var ct = Cts.Token;
        try
        {
            foreach (var f in fresh)
            {
                var (pages, problem) = await PdfThumbs.CountAsync(f);
                ct.ThrowIfCancellationRequested();
                if (problem is not null || pages == 0)
                {
                    SetNote(IoPath.GetFileName(f) + " cannot be read: " + (problem ?? "it has no pages"));
                    continue;
                }
                _files.Add(f);
                _model.AppendFile(pages);
                Refresh();
                RaiseChanged();
                await DrawFileAsync(_files.Count, f, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // replaced
        }
    }

    private async Task DrawFileAsync(int fileNo, string path, CancellationToken ct)
    {
        var ok = await PdfThumbs.RenderAllAsync(path, ThumbWidth, (i, bmp, blank) =>
        {
            _thumbs[(fileNo, i + 1)] = (bmp, blank);
            if (_parts.TryGetValue((fileNo, i + 1), out var part)) Fill(part, bmp, blank);
            UpdateSummary();
        }, ct);
        if (!ok) SetNote("No pictures for " + IoPath.GetFileName(path) + ". The pages are still there and the plan still works.");
    }

    private void SetNote(string? text)
    {
        _note.Text = text ?? "";
        _note.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- toolbar ----

    private void BuildToolbar()
    {
        void Add(string key, string label, string tip, Action act)
        {
            var b = Button(label, tip, act);
            _buttons[key] = b;
            _toolbar.Children.Add(b);
        }
        if (_rules.CanRotate)
        {
            Add("left", "Rotate left", "Rotate the selected pages a quarter turn left (Ctrl+Shift+R)", () => Do(() => _model.RotateSelected(-90)));
            Add("right", "Rotate right", "Rotate the selected pages a quarter turn right (Ctrl+R)", () => Do(() => _model.RotateSelected(90)));
        }
        if (_rules.CanDelete)
            Add("delete", "Delete", "Remove the selected pages (Delete)", () => Do(() => _model.DeleteSelected() > 0));
        if (_rules.CanInsertBlank)
            Add("blank", "Blank page", "Insert a blank page after the selection", () => Do(() => _model.InsertBlank()));
        if (_rules.CanDelete)
            Add("selblank", "Select blank", "Select every page that looks empty", SelectBlank);
        Add("all", "Select all", "Select every page (Ctrl+A)", () => { _model.SelectAll(); Refresh(false); });
        Add("undo", "Undo", "Undo the last change (Ctrl+Z)", () => Do(() => _model.Undo()));
        Add("reset", "Reset", "Back to the pages as they came", () => Do(() => { _model.Reset(); return true; }));
        if (_rules.CanReorder)
            Add("add", "Add PDFs", "Add the pages of more PDFs", () => Ctx.Browse());
    }

    private void Do(Func<bool> act)
    {
        if (!act()) return;
        _cursor = _model.Selected.Count > 0 ? _model.Selected.Min() : -1;
        Refresh();
        RaiseChanged();
    }

    private void SelectBlank()
    {
        _model.ClearSelection();
        for (var i = 0; i < _model.Pages.Count; i++)
        {
            var p = _model.Pages[i];
            if (!p.IsBlankInsert && _thumbs.TryGetValue((p.File, p.Page), out var t) && t.Blank)
                _model.Click(i, false, true);
        }
        Refresh(false);
        SetNote(_model.Selected.Count == 0 ? "No page looks blank." : null);
    }

    private void UpdateToolbar()
    {
        var any = _model.Selected.Count > 0;
        foreach (var k in new[] { "left", "right", "delete" })
            if (_buttons.TryGetValue(k, out var b)) b.IsEnabled = any;
        if (_buttons.TryGetValue("undo", out var u)) u.IsEnabled = _model.CanUndo;
        if (_buttons.TryGetValue("reset", out var r)) r.IsEnabled = _model.IsChanged;
        foreach (var k in new[] { "all", "selblank", "blank" })
            if (_buttons.TryGetValue(k, out var b)) b.IsEnabled = _model.Pages.Count > 0 || k == "blank" && _files.Count > 0;
    }

    private void UpdateSummary()
    {
        var blanks = _model.Pages.Count(p => !p.IsBlankInsert && _thumbs.TryGetValue((p.File, p.Page), out var t) && t.Blank);
        var n = _model.Pages.Count;
        var sel = _model.Selected.Count;
        var parts = new List<string>
        {
            _loading ? "Reading pages..." : $"{n} page{(n == 1 ? "" : "s")}" + (_files.Count > 1 ? $" from {_files.Count} files" : ""),
        };
        if (sel > 0) parts.Add($"{sel} selected");
        if (blanks > 0) parts.Add($"{blanks} look{(blanks == 1 ? "s" : "")} blank");
        if (_model.IsChanged && !_rules.CanReorder && _rules.CanDelete) parts.Add($"{_model.FileLengths.FirstOrDefault() - n} to remove");
        var hint = _rules.CanReorder
            ? "Click to select, Shift or Ctrl for more, drag to reorder."
            : _rules.CanDelete ? "Click to select, then Delete." : "Click to select, then rotate. With nothing turned, Rotate turns every page right.";
        _summary.Text = string.Join(" - ", parts) + (n > 0 ? ". " + hint : "");
    }

    // ---- drawing ----

    /// <summary>Redraws; <paramref name="rebuild"/> false only restyles the selection of the existing tiles.</summary>
    private void Refresh(bool rebuild = true)
    {
        if (rebuild) Rebuild();
        else
            for (var i = 0; i < _wrap.Children.Count; i++)
                if (_wrap.Children[i] is Border t) StyleSelection(t, _model.IsSelected(i));
        _empty.Visibility = _files.Count == 0 && !_loading ? Visibility.Visible : Visibility.Collapsed;
        UpdateToolbar();
        UpdateSummary();
    }

    private void Rebuild()
    {
        _wrap.Children.Clear();
        _parts.Clear();
        var multi = _files.Count > 1;
        for (var i = 0; i < _model.Pages.Count; i++)
            _wrap.Children.Add(Tile(i, _model.Pages[i], multi));
    }

    private Border Tile(int index, PlanPage page, bool multiFile)
    {
        var box = new Border
        {
            Width = 126,
            Height = 164,
            Background = Brushes.White,
            BorderBrush = Brush("ShellLine"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            ClipToBounds = true,
        };
        var inner = new Grid();
        var part = new TilePart();
        string what;
        if (page.IsBlankInsert)
        {
            box.BorderBrush = Brush("ShellThumb");
            var t = Text("New blank page", "ShellCaption");
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            inner.Children.Add(t);
            what = "blank page to insert";
        }
        else
        {
            var img = new System.Windows.Controls.Image { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            if (page.Rotation != 0) img.LayoutTransform = new RotateTransform(page.Rotation);
            var vb = new Viewbox { Stretch = Stretch.Uniform, Margin = new Thickness(3), Child = img };
            part.Image = img;
            var ph = Text("...", "ShellCaption");
            ph.HorizontalAlignment = HorizontalAlignment.Center;
            ph.VerticalAlignment = VerticalAlignment.Center;
            part.Placeholder = ph;
            inner.Children.Add(ph);
            inner.Children.Add(vb);
            part.BlankBadge = BlankBadge();
            inner.Children.Add(part.BlankBadge);
            if (_thumbs.TryGetValue((page.File, page.Page), out var th)) Fill(part, th.Image, th.Blank);
            _parts[(page.File, page.Page)] = part;
            what = $"page {page.Page} of {IoPath.GetFileName(_files.ElementAtOrDefault(page.File - 1) ?? "")}";
        }
        box.Child = inner;

        var label = page.IsBlankInsert ? "blank" : multiFile ? $"{index + 1}  ({IoPath.GetFileNameWithoutExtension(_files[page.File - 1])} p{page.Page})" : (index + 1).ToString(CultureInfo.InvariantCulture);
        var cap = Text(label, "ShellCaption");
        cap.HorizontalAlignment = HorizontalAlignment.Center;
        cap.TextTrimming = TextTrimming.CharacterEllipsis;
        cap.Margin = new Thickness(0, 5, 0, 0);
        if (page.Rotation != 0) cap.Text += $"  {page.Rotation} deg";

        var stack = new StackPanel();
        stack.Children.Add(box);
        stack.Children.Add(cap);
        var barL = InsertBar(vertical: true);
        var barR = InsertBar(vertical: true);
        barR.HorizontalAlignment = HorizontalAlignment.Right;
        var grid = new Grid();
        grid.Children.Add(stack);
        grid.Children.Add(barL);
        grid.Children.Add(barR);

        var tile = new Border
        {
            Width = 142,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(6),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Child = grid,
            AllowDrop = _rules.CanReorder,
            Tag = index,
        };
        StyleSelection(tile, _model.IsSelected(index));
        AutomationProperties.SetName(tile, $"{what}, position {index + 1} of {_model.Pages.Count}" + (page.Rotation != 0 ? $", turned {page.Rotation} degrees" : ""));

        tile.PreviewMouseLeftButtonDown += (_, e) => OnTileDown(index, e, tile);
        tile.PreviewMouseLeftButtonUp += (_, _) => OnTileUp(index);
        tile.PreviewMouseMove += (_, e) => OnTileMove(index, tile, e);
        tile.DragOver += (_, e) => OnTileDrag(tile, barL, barR, e);
        tile.DragLeave += (_, _) => { barL.Visibility = Visibility.Collapsed; barR.Visibility = Visibility.Collapsed; };
        tile.Drop += (_, e) => OnTileDrop(index, tile, e);
        return tile;
    }

    private Border BlankBadge()
    {
        var t = Text("Blank?", "ShellCaption", 10.5, FontWeights.SemiBold);
        t.Foreground = Brush("ShellInk");
        t.TextWrapping = TextWrapping.NoWrap;
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(255, 224, 130)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 1, 6, 2),
            Margin = new Thickness(5),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = t,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            ToolTip = "This page looks empty",
        };
    }

    private static void Fill(TilePart part, BitmapSource image, bool blank)
    {
        if (part.Image is not null) part.Image.Source = image;
        if (part.Placeholder is not null) part.Placeholder.Visibility = Visibility.Collapsed;
        if (part.BlankBadge is not null) part.BlankBadge.Visibility = blank ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StyleSelection(Border tile, bool selected)
    {
        tile.BorderBrush = selected ? Brush("ShellAccent") : Brushes.Transparent;
        tile.Background = selected ? Brush("ShellAccentTint") : Brushes.Transparent;
    }

    // ---- mouse ----

    private static bool Shift => (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
    private static bool Ctrl => (Keyboard.Modifiers & ModifierKeys.Control) != 0;

    private void OnTileDown(int index, MouseButtonEventArgs e, Border tile)
    {
        _scroll.Focus();
        _pressIndex = index;
        _pressAt = e.GetPosition(tile);
        _pressApplied = false;
        if (Shift || Ctrl || !_model.IsSelected(index))
        {
            _model.Click(index, Shift, Ctrl);
            _cursor = index;
            _pressApplied = true;
            Refresh(false);
        }
    }

    private void OnTileUp(int index)
    {
        // A plain click on a page that was already part of a selection narrows the selection to it,
        // unless the press turned into a drag.
        if (_pressIndex == index && !_pressApplied && !Shift && !Ctrl)
        {
            _model.Click(index, false, false);
            _cursor = index;
            Refresh(false);
        }
        _pressIndex = -1;
    }

    private void OnTileMove(int index, Border tile, MouseEventArgs e)
    {
        if (!_rules.CanReorder || e.LeftButton != MouseButtonState.Pressed || _pressIndex != index) return;
        var now = e.GetPosition(tile);
        if (Math.Abs(now.X - _pressAt.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(now.Y - _pressAt.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _pressIndex = -1;
        try { DragDrop.DoDragDrop(tile, new DataObject(DragFormat, "pages"), DragDropEffects.Move); }
        finally { HideBars(); }
    }

    private void HideBars()
    {
        foreach (var t in _wrap.Children.OfType<Border>())
            if (t.Child is Grid g)
                foreach (var b in g.Children.OfType<Border>().Where(b => b.Width == 3)) b.Visibility = Visibility.Collapsed;
    }

    private void OnTileDrag(Border tile, Border barL, Border barR, DragEventArgs e)
    {
        var mine = e.Data.GetDataPresent(DragFormat);
        if (!mine && !HasFiles(e)) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        e.Effects = mine ? DragDropEffects.Move : _rules.CanReorder ? DragDropEffects.Copy : DragDropEffects.None;
        var right = e.GetPosition(tile).X > tile.ActualWidth / 2;
        barL.Visibility = mine && !right ? Visibility.Visible : Visibility.Collapsed;
        barR.Visibility = mine && right ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnTileDrop(int index, Border tile, DragEventArgs e)
    {
        HideBars();
        if (e.Data.GetDataPresent(DragFormat))
        {
            var at = index + (e.GetPosition(tile).X > tile.ActualWidth / 2 ? 1 : 0);
            Do(() => _model.MoveSelected(at));
            e.Handled = true;
        }
        // Files fall through to the zone, which adds them.
    }

    /// <summary>At the top or bottom of the grid the wheel belongs to the page, not to a scroll box that cannot move.</summary>
    private void ForwardWheelAtEnds(object sender, MouseWheelEventArgs e)
    {
        var atTop = _scroll.VerticalOffset <= 0 && e.Delta > 0;
        var atBottom = _scroll.VerticalOffset >= _scroll.ScrollableHeight && e.Delta < 0;
        if (!atTop && !atBottom) return;
        e.Handled = true;
        var forwarded = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sender };
        (_scroll.Parent as UIElement)?.RaiseEvent(forwarded);
    }

    private void OnScrollDrag(object sender, DragEventArgs e)
    {
        // Scroll while a drag hovers near the top or bottom edge.
        var p = e.GetPosition(_scroll);
        if (p.Y < 36) _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset - 24);
        else if (p.Y > _scroll.ActualHeight - 36) _scroll.ScrollToVerticalOffset(_scroll.VerticalOffset + 24);
    }

    private void OnZoneDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : HasFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnZoneDrop(object sender, DragEventArgs e)
    {
        HideBars();
        if (e.Data.GetDataPresent(DragFormat))
            Do(() => _model.MoveSelected(_model.Pages.Count));
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
            AddFiles(dropped);
        e.Handled = true;
    }

    // ---- keyboard ----

    private void OnKey(object sender, KeyEventArgs e)
    {
        var n = _model.Pages.Count;
        if (n == 0) return;
        int Cols() => Math.Max(1, (int)(_wrap.ActualWidth / 150));
        if (Ctrl && e.Key == Key.A) { _model.SelectAll(); Refresh(false); e.Handled = true; return; }
        if (Ctrl && e.Key == Key.Z) { Do(() => _model.Undo()); e.Handled = true; return; }
        if (Ctrl && e.Key == Key.R && _rules.CanRotate) { Do(() => _model.RotateSelected(Shift ? -90 : 90)); e.Handled = true; return; }
        if (e.Key == Key.Delete) { Do(() => _model.DeleteSelected() > 0); e.Handled = true; return; }
        var to = e.Key switch
        {
            Key.Left => _cursor - 1,
            Key.Right => _cursor + 1,
            Key.Up => _cursor - Cols(),
            Key.Down => _cursor + Cols(),
            Key.Home => 0,
            Key.End => n - 1,
            _ => int.MinValue,
        };
        if (to == int.MinValue) return;
        to = Math.Clamp(to, 0, n - 1);
        if (_cursor < 0) to = 0;
        _model.Click(to, Shift, false);
        _cursor = to;
        Refresh(false);
        if (to < _wrap.Children.Count && _wrap.Children[to] is FrameworkElement el) el.BringIntoView();
        e.Handled = true;
    }

    // ---- snapshot state ----

    /// <summary>
    /// "select=2,3;rotate=90;delete;select=5;blank" applied in order to what is on screen; page numbers are 1-based
    /// positions in the grid at that moment. For CI renders: it is what a person's clicks would have done.
    /// </summary>
    public override void ApplyState(string state)
    {
        foreach (var raw in state.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = raw.IndexOf('=');
            var key = (eq < 0 ? raw : raw[..eq]).ToLowerInvariant();
            var val = eq < 0 ? "" : raw[(eq + 1)..];
            switch (key)
            {
                case "select":
                    _model.ClearSelection();
                    foreach (var tok in val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        if (int.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out var one))
                            _model.Click(one - 1, false, true);
                    break;
                case "selectblank": SelectBlank(); break;
                case "rotate": _model.RotateSelected(int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : 90); break;
                case "delete": _model.DeleteSelected(); break;
                case "blank": _model.InsertBlank(); break;
                case "move": if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var at)) _model.MoveSelected(at); break;
            }
        }
        Refresh();
        RaiseChanged();
    }
}
