using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NetieSpace.Apps.Image;
using NetieSpace.Apps.Tools;
using NetieSpace.Helpers;
using WpfImage = System.Windows.Controls.Image;

namespace NetieSpace.Apps.Shell.ImagePanels;

/// <summary>
/// The picture side of an image tool page: the dropped photo with what the tool will make of it drawn live beside it
/// (resize, compress, rotate, convert), a draggable crop box, or the watermark with a click-to-place pad; quick-setting
/// chips; a thumbnail strip with a run state per photo. Options stay in the page's own controls (this reads and writes
/// them), so a run does exactly what the picture shows. All the computing is in <see cref="ImageFlow"/> and
/// <see cref="FlowSource"/>; this class only arranges it and keeps stale results from landing.
/// </summary>
public sealed class ImageFlowHost : UserControl
{
    private static readonly string[] CropKeys = { "x", "y", "width", "height" };

    private ToolPageModel? _model;
    private Action<IReadOnlyList<string>>? _setFiles;
    private Dictionary<string, OptionBinding> _bind = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _files = Array.Empty<string>();
    private int _selected;
    private FlowSource? _source;
    private int _sourceVersion;
    private int _previewVersion;
    private bool _updating;
    private bool _snapshot;
    private string _shape = "free";
    private PreviewDebouncer? _debouncer;
    private readonly SemaphoreSlim _measureGate = new(1, 1);

    // visuals
    private FlowStage _stage;
    private TextBlock _caption = null!;
    private WpfImage _beforeImg = new();
    private WpfImage _afterImg = new();
    private TextBlock _beforeCap = new();
    private TextBlock _afterCap = new();
    private TextBlock _beforeNote = new();
    private TextBlock _afterNote = new();
    private CropSurface? _crop;
    private PositionPad? _pad;
    private Grid? _padHost;
    private Grid? _afterBox;
    private ThumbStrip? _strip;
    private readonly List<(ImagePreset Preset, Button Chip)> _presetChips = new();
    private readonly Dictionary<string, Button> _shapeChips = new(StringComparer.Ordinal);
    private StackPanel? _numbers;

    public ImageFlowHost()
    {
        Visibility = Visibility.Collapsed;
    }

    /// <summary>True while the page shows an image tool this panel draws; the page then leaves its own preview collapsed.</summary>
    public bool Active => _model is not null && _model.Available && ImageFlow.Supports(_model.ToolId);

    // ---- page hooks --------------------------------------------------------------------------------

    /// <summary>Called by the tool page after it has built its option controls for <paramref name="model"/>.</summary>
    public void Attach(ToolPageModel model, Panel optionsHost, UIElement legacyPreview, Action<IReadOnlyList<string>> setFiles)
    {
        Reset();
        _model = model;
        _setFiles = setFiles;
        if (!Active)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        legacyPreview.Visibility = Visibility.Collapsed;
        _stage = ImageFlow.StageOf(model.ToolId);
        _bind = OptionBinding.Bind(model, optionsHost, OnOptionChanged);
        _shape = "free";
        Build();
        ArrangeOptions(optionsHost);
        Visibility = Visibility.Collapsed; // shown once there are photos
    }

    /// <summary>Called by the tool page whenever its file list changes.</summary>
    public void SetFiles(IReadOnlyList<string> files)
    {
        if (!Active) return;
        _files = files;
        _selected = 0;
        _strip!.SetFiles(files, _snapshot);
        if (files.Count == 0)
        {
            Visibility = Visibility.Collapsed;
            DropSource();
            _sourceVersion++;
            _previewVersion++;
            return;
        }
        Visibility = Visibility.Visible;
        SelectFile(Math.Min(_snapshot ? (FlowSnapshot.Current?.Select ?? 0) : 0, files.Count - 1));
    }

    /// <summary>Called by the tool page while a run is in progress or finished: one state per photo.</summary>
    public void ShowQueue(RunQueue queue)
    {
        if (Active) _strip?.ShowQueue(queue);
    }

    /// <summary>
    /// Only for <c>--snapshot</c> with <c>--flow-files</c>: puts those photos and options on the page and draws everything at
    /// once (no debounce, no background work), so the headless render shows the panel in a real state.
    /// </summary>
    public void LoadSnapshot()
    {
        if (!Active || FlowSnapshot.Current is not { } snap || _setFiles is null) return;
        _snapshot = true;
        _updating = true;
        foreach (var (key, value) in snap.Options)
            if (_bind.TryGetValue(key, out var b)) b.Set(value);
        _updating = false;
        _setFiles(snap.Files);
        if (snap.Run) RunSnapshot(snap);
    }

    private void RunSnapshot(FlowSnapshot snap)
    {
        var model = _model!;
        var outDir = snap.OutDir ?? IoPath.Combine(IoPath.GetTempPath(), "netie-flow-snapshot");
        var values = CurrentValues();
        var queue = RunQueue.For(model.Tool.Entry, _files);
        // Off the dispatcher: this blocks the UI thread, so the run must not need it.
        Task.Run(() => queue.RunAsync((batch, ct) =>
            ToolRunner.RunAsync(model.Tool, model.BuildRequest(batch, outDir, values), null, ct))).GetAwaiter().GetResult();
        _strip!.ShowQueue(queue);
        foreach (var item in queue.Items)
            Console.Out.WriteLine($"FLOW_STATE {item.Name}|{item.State}|{item.Message}");
    }

    private void Reset()
    {
        _debouncer?.Dispose();
        _debouncer = null;
        _sourceVersion++;
        _previewVersion++;
        DropSource();
        _files = Array.Empty<string>();
        _bind = new Dictionary<string, OptionBinding>(StringComparer.Ordinal);
        _presetChips.Clear();
        _shapeChips.Clear();
        _crop = null;
        _pad = null;
        _padHost = null;
        _numbers = null;
        _afterBox = null;
        _strip = null;
        Content = null;
    }

    private void DropSource()
    {
        var old = _source;
        _source = null;
        old?.Dispose();
    }

    // ---- building the panel -------------------------------------------------------------------------

    private Style Res(string key) => (Style)FindResource(key);
    private Brush Brush(string key) => (Brush)FindResource(key);

    private TextBlock Caption(string text = "") =>
        new() { Text = text, Style = Res("ShellCaption"), TextWrapping = TextWrapping.Wrap };

    private void Build()
    {
        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = "Preview", Style = Res("ShellSection"), FontSize = 13 });
        _caption = Caption();
        _caption.Margin = new Thickness(0, 2, 0, 0);
        _caption.TextTrimming = TextTrimming.CharacterEllipsis;
        _caption.TextWrapping = TextWrapping.NoWrap;
        root.Children.Add(_caption);

        _beforeImg = new WpfImage(); _afterImg = new WpfImage();
        _beforeCap = Caption(); _afterCap = Caption();
        _beforeNote = Caption(); _afterNote = Caption();

        FrameworkElement stage = _stage switch
        {
            FlowStage.Crop => BuildCropStage(),
            FlowStage.Watermark => BuildWatermarkStage(),
            _ => BuildPairStage(),
        };
        stage.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(stage);

        var presets = ImagePresets.For(_model!.ToolId);
        if (presets.Count > 0)
        {
            root.Children.Add(Label("Quick settings"));
            var wrap = new WrapPanel();
            foreach (var p in presets)
            {
                var preset = p;
                var chip = Chip(preset.Label, () => ApplyPreset(preset));
                _presetChips.Add((preset, chip));
                wrap.Children.Add(chip);
            }
            root.Children.Add(wrap);
        }

        if (_stage == FlowStage.Crop)
        {
            root.Children.Add(Label("Shape"));
            var wrap = new WrapPanel();
            foreach (var (shape, text) in new[] { ("free", "Free"), ("1:1", "1:1 Square"), ("4:5", "4:5 Portrait"), ("16:9", "16:9 Wide") })
            {
                var s = shape;
                var chip = Chip(text, () => SetShape(s));
                _shapeChips[shape] = chip;
                wrap.Children.Add(chip);
            }
            root.Children.Add(wrap);

            _numbers = new StackPanel();
            var fold = new Expander
            {
                Header = new TextBlock { Text = "Exact numbers", Style = Res("ShellMutedText") },
                Content = _numbers,
                Margin = new Thickness(0, 4, 0, 0),
                IsExpanded = false,
            };
            root.Children.Add(fold);
        }

        if (_stage == FlowStage.Watermark)
        {
            var hint = Caption("Click the picture to place the mark. Text, colour, size and opacity are on the left.");
            hint.Margin = new Thickness(0, 8, 0, 0);
            root.Children.Add(hint);
        }

        var status = Caption();
        status.Margin = new Thickness(0, 6, 0, 0);
        _strip = new ThumbStrip(status) { Margin = new Thickness(0, 14, 0, 0) };
        _strip.Picked += SelectFile;
        root.Children.Add(_strip);

        Width = 480;
        Margin = new Thickness(32, 24, 0, 0);
        VerticalAlignment = VerticalAlignment.Top;
        Content = new Border { Style = Res("ShellCard"), Padding = new Thickness(14), Child = root };
        _debouncer = new PreviewDebouncer();
        _debouncer.Failed += ex => AppLogger.Warn("image preview failed: " + ex.Message);
    }

    private TextBlock Label(string text) =>
        new() { Text = text, Style = Res("ShellMutedText"), Margin = new Thickness(0, 12, 0, 6) };

    private Button Chip(string text, Action click)
    {
        var b = new Button
        {
            Content = text, FontSize = 12, MinHeight = 28, Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 6),
        };
        b.Click += (_, _) => click();
        MarkChip(b, false);
        return b;
    }

    private void MarkChip(Button b, bool on)
    {
        b.Background = on ? Brush("ShellAccentTint") : Brushes.White;
        b.BorderBrush = on ? Brush("ShellAccent") : Brush("ShellLine");
    }

    private FrameworkElement Pane(string label, WpfImage img, TextBlock cap, TextBlock note)
    {
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        img.Stretch = Stretch.Uniform;
        img.Margin = new Thickness(6);
        note.HorizontalAlignment = HorizontalAlignment.Center;
        note.VerticalAlignment = VerticalAlignment.Center;
        note.TextAlignment = TextAlignment.Center;
        note.Margin = new Thickness(12);
        var chip = new Border
        {
            Background = Brush("ShellAccentTint"), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = label, Style = Res("ShellCaption"), FontWeight = FontWeights.SemiBold, Foreground = Brush("ShellInk") },
        };
        var box = new Grid { Height = 232 };
        box.Children.Add(img);
        box.Children.Add(note);
        box.Children.Add(chip);
        var frame = new Border
        {
            Background = Brush("ShellSidebar"), CornerRadius = (CornerRadius)FindResource("ShellRadiusControl"),
            ClipToBounds = true, Child = box,
        };
        cap.Margin = new Thickness(0, 6, 0, 0);
        var stack = new StackPanel();
        stack.Children.Add(frame);
        stack.Children.Add(cap);
        return stack;
    }

    private FrameworkElement BuildPairStage()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var left = Pane("Before", _beforeImg, _beforeCap, _beforeNote);
        var right = Pane("After", _afterImg, _afterCap, _afterNote);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    private FrameworkElement BuildCropStage()
    {
        _crop = new CropSurface { Height = 292 };
        _crop.BoxChanged += _ => OnCropDragged();
        _crop.Settled += () => SchedulePreview();
        var frame = new Border
        {
            Background = Brush("ShellSidebar"), CornerRadius = (CornerRadius)FindResource("ShellRadiusControl"),
            ClipToBounds = true, Child = _crop,
        };
        _afterCap.Margin = new Thickness(0, 6, 0, 0);
        _afterNote.Margin = new Thickness(0, 6, 0, 0);
        var stack = new StackPanel();
        stack.Children.Add(frame);
        stack.Children.Add(_afterCap);
        stack.Children.Add(_afterNote);
        return stack;
    }

    private FrameworkElement BuildWatermarkStage()
    {
        RenderOptions.SetBitmapScalingMode(_afterImg, BitmapScalingMode.HighQuality);
        _afterImg.Stretch = Stretch.Uniform;
        _pad = new PositionPad();
        _pad.Picked += v =>
        {
            if (_bind.TryGetValue("position", out var b)) b.Set(v);
        };
        _padHost = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        _padHost.Children.Add(_pad);
        _afterNote.HorizontalAlignment = HorizontalAlignment.Center;
        _afterNote.VerticalAlignment = VerticalAlignment.Center;
        _afterNote.TextAlignment = TextAlignment.Center;
        _afterNote.Margin = new Thickness(16);
        _afterBox = new Grid { Height = 292 };
        _afterBox.Children.Add(_afterImg);
        _afterBox.Children.Add(_padHost);
        _afterBox.Children.Add(_afterNote);
        var frame = new Border
        {
            Background = Brush("ShellSidebar"), CornerRadius = (CornerRadius)FindResource("ShellRadiusControl"),
            ClipToBounds = true, Child = _afterBox,
        };
        _afterCap.Margin = new Thickness(0, 6, 0, 0);
        var stack = new StackPanel();
        stack.Children.Add(frame);
        stack.Children.Add(_afterCap);
        return stack;
    }

    /// <summary>The option rows this panel draws itself are hidden or folded away; every other row stays where it was.</summary>
    private void ArrangeOptions(Panel optionsHost)
    {
        if (_stage == FlowStage.Crop && _numbers is not null)
        {
            foreach (var key in CropKeys)
                if (_bind.TryGetValue(key, out var b))
                {
                    optionsHost.Children.Remove(b.Block);
                    b.Block.Margin = new Thickness(0, 8, 0, 0);
                    _numbers.Children.Add(b.Block);
                }
            foreach (var key in new[] { "aspect", "box" })
                if (_bind.TryGetValue(key, out var b))
                    b.Block.Visibility = Visibility.Collapsed;
            if (optionsHost.Parent is UIElement card)
                card.Visibility = optionsHost.Children.Cast<UIElement>().Any(c => c.Visibility == Visibility.Visible)
                    ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ---- reading and writing the page's options -------------------------------------------------------

    private Dictionary<string, string> CurrentValues() =>
        _bind.ToDictionary(kv => kv.Key, kv => kv.Value.Get(), StringComparer.Ordinal);

    private string? Value(string key) => _bind.TryGetValue(key, out var b) ? b.Get() : null;

    private void SetValue(string key, string value)
    {
        if (_bind.TryGetValue(key, out var b)) b.Set(value);
    }

    private void OnOptionChanged(string key)
    {
        if (_updating || !Active) return;
        if (_stage == FlowStage.Crop && CropKeys.Contains(key))
            CropFromNumbers();
        UpdateMarks();
        SchedulePreview();
    }

    private void ApplyPreset(ImagePreset preset)
    {
        _updating = true;
        foreach (var (key, value) in preset.Options)
            SetValue(key, value);
        _updating = false;
        UpdateMarks();
        SchedulePreview();
    }

    private void UpdateMarks()
    {
        foreach (var (preset, chip) in _presetChips)
            MarkChip(chip, ImagePresets.Matches(preset, k => Value(k)));
        foreach (var (shape, chip) in _shapeChips)
            MarkChip(chip, string.Equals(shape, _shape, StringComparison.Ordinal));
        _pad?.Select(Value("position"));
    }

    // ---- crop -----------------------------------------------------------------------------------------

    private void SetShape(string shape)
    {
        if (_source is null || _crop is null) return;
        _shape = shape;
        var box = CropBox.Refit(_crop.Box, _source.Width, _source.Height, shape);
        _crop.SetBox(box, shape);
        WriteCrop(box);
        UpdateMarks();
        SchedulePreview();
    }

    private void OnCropDragged()
    {
        if (_crop is null) return;
        WriteCrop(_crop.Box);
        SchedulePreview();
    }

    /// <summary>Puts the box in the page's options as a run will read it: the box, its shape, and this photo's pixel numbers.</summary>
    private void WriteCrop(CropRect box)
    {
        if (_source is null) return;
        var w = _source.Width;
        var h = _source.Height;
        var text = CropBox.ToOption(box, w, h, _shape);
        var (x, y, pw, ph) = ImageOps.CropRectFor(w, h, new Dictionary<string, string> { ["aspect"] = _shape, ["box"] = text });
        _updating = true;
        SetValue("aspect", _shape);
        SetValue("box", text);
        SetValue("x", x.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetValue("y", y.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetValue("width", pw.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetValue("height", ph.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _updating = false;
    }

    /// <summary>Typing exact numbers drops the drawn box (the numbers rule, for every photo) and moves the box on screen to them.</summary>
    private void CropFromNumbers()
    {
        _updating = true;
        SetValue("box", "");
        SetValue("aspect", "free");
        _shape = "free";
        _updating = false;
        if (_source is null || _crop is null) return;
        if (int.TryParse(Value("x"), out var x) && int.TryParse(Value("y"), out var y)
            && int.TryParse(Value("width"), out var w) && int.TryParse(Value("height"), out var h)
            && w > 0 && h > 0 && x >= 0 && y >= 0 && x + w <= _source.Width && y + h <= _source.Height)
            _crop.SetBox(new CropRect((double)x / _source.Width, (double)y / _source.Height, (double)w / _source.Width, (double)h / _source.Height), "free");
    }

    private void StartCrop()
    {
        if (_source is null || _crop is null) return;
        _shape = Value("aspect") is { Length: > 0 } a && Array.IndexOf(CropBox.Shapes, a) >= 0 ? a : "free";
        CropRect box;
        var current = CurrentValues();
        try
        {
            if (!string.IsNullOrWhiteSpace(Value("box")))
            {
                var (x, y, w, h) = ImageOps.CropRectFor(_source.Width, _source.Height, current);
                box = new CropRect((double)x / _source.Width, (double)y / _source.Height, (double)w / _source.Width, (double)h / _source.Height);
            }
            else
            {
                box = CropBox.Initial(_source.Width, _source.Height, _shape);
            }
        }
        catch (ArgumentException)
        {
            box = CropBox.Initial(_source.Width, _source.Height, _shape);
        }
        _crop.SetBox(box, _shape);
        WriteCrop(box);
    }

    // ---- choosing a photo and drawing it -----------------------------------------------------------------

    private void SelectFile(int index)
    {
        if (index < 0 || index >= _files.Count) return;
        _selected = index;
        _strip?.Select(index);
        _caption.Text = $"{IoPath.GetFileName(_files[index])}  ({index + 1} of {_files.Count})";
        var version = ++_sourceVersion;
        _previewVersion++;
        var path = _files[index];
        Clear("Loading");

        if (_snapshot)
        {
            FlowSource? src = null;
            FlowFrame? before = null;
            string? error = null;
            try { src = FlowSource.Open(path); before = src.Before(); }
            catch (Exception ex) { error = ex.Message; src?.Dispose(); src = null; }
            OnSource(version, src, before, error);
            return;
        }
        _ = LoadAsync(version, path);
    }

    private async Task LoadAsync(int version, string path)
    {
        var (src, before, error) = await Task.Run(() =>
        {
            FlowSource? s = null;
            try
            {
                s = FlowSource.Open(path);
                return (s, (FlowFrame?)s.Before(), (string?)null);
            }
            catch (Exception ex)
            {
                s?.Dispose();
                return ((FlowSource?)null, (FlowFrame?)null, (string?)ex.Message);
            }
        });
        if (version != _sourceVersion)
        {
            src?.Dispose();
            return;
        }
        OnSource(version, src, before, error);
    }

    private void OnSource(int version, FlowSource? src, FlowFrame? before, string? error)
    {
        if (version != _sourceVersion) { src?.Dispose(); return; }
        DropSource();
        _source = src;
        if (src is null || before is null)
        {
            Clear("");
            var text = "Cannot show this picture: " + error;
            _beforeNote.Text = text;
            _beforeNote.Foreground = Brush("ShellDanger");
            _afterNote.Text = _stage == FlowStage.Crop ? text : "";
            if (_stage == FlowStage.Crop) _afterNote.Foreground = Brush("ShellDanger");
            return;
        }

        var bitmap = Decode(before.Png);
        var size = $"{src.Width} x {src.Height} px - {ToolPageModel.FormatSize(src.FileBytes)}";
        _beforeNote.Text = "";
        _beforeImg.Source = bitmap;
        _beforeCap.Text = size;
        _afterNote.Text = "";
        if (_stage == FlowStage.Crop && _crop is not null)
        {
            _crop.Show(bitmap, src.Width, src.Height);
            StartCrop();
            _afterCap.Text = size;
        }
        UpdateMarks();
        SchedulePreview(immediate: true);
    }

    private void Clear(string message)
    {
        _beforeImg.Source = null;
        _afterImg.Source = null;
        _beforeCap.Text = "";
        _afterCap.Text = "";
        _beforeNote.Text = message;
        _beforeNote.Foreground = Brush("ShellMuted");
        _afterNote.Text = "";
        if (_padHost is not null) _padHost.Visibility = Visibility.Collapsed;
        _crop?.Show(null, 1, 1);
    }

    private static BitmapSource Decode(byte[] png)
    {
        using var ms = new System.IO.MemoryStream(png);
        var frame = BitmapFrame.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    // ---- the after picture --------------------------------------------------------------------------------

    private void SchedulePreview(bool immediate = false)
    {
        var src = _source;
        var model = _model;
        if (src is null || model is null || _debouncer is null) return;
        var version = ++_previewVersion;
        var values = model.BuildRequest(new[] { src.Path }, "preview", CurrentValues()).Options;

        if (_snapshot)
        {
            ComputeAndShow(src, model, values, version, CancellationToken.None, sync: true).GetAwaiter().GetResult();
            return;
        }
        _debouncer.Schedule(ct => ComputeAndShow(src, model, values, version, ct, sync: false));
    }

    private async Task ComputeAndShow(FlowSource src, ToolPageModel model, IReadOnlyDictionary<string, string> values,
        int version, CancellationToken ct, bool sync)
    {
        var (o, error) = ImageFlow.Resolve(model.Tool.Entry, src.Path, values);
        FlowFrame? frame = null;
        if (o is not null)
        {
            try { frame = src.After(model.ToolId, o); }
            catch (ObjectDisposedException) { return; }
            catch (Exception ex) { error = ex.Message; }
        }
        ct.ThrowIfCancellationRequested();
        Present(version, () => ShowAfter(frame, error, calculating: frame is not null && o is not null && ImageFlow.ShowsSize(model.ToolId)), sync);
        if (frame is null || o is null || !ImageFlow.ShowsSize(model.ToolId)) return;

        // The size is the real thing: the tool run on the whole file in memory. One at a time, newest first.
        if (!sync) await _measureGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            var m = ImageFlow.Measure(src.Path, model.ToolId, o);
            ct.ThrowIfCancellationRequested();
            FlowFrame? refined = null;
            if (m.Error is null && m.Spec is not null && model.ToolId == "img-compress")
            {
                try { refined = src.After(model.ToolId, o, m.Spec); }
                catch (Exception) { /* the first drawing stays */ }
            }
            Present(version, () => ShowSize(frame, refined, m, src), sync);
        }
        finally
        {
            if (!sync) _measureGate.Release();
        }
    }

    private void Present(int version, Action show, bool sync)
    {
        if (sync)
        {
            show();
            return;
        }
        Dispatcher.InvokeAsync(() => { if (version == _previewVersion) show(); });
    }

    private string Friendly(string message) =>
        _model?.ToolId == "img-watermark" && message.Contains("option 'text' or option 'image' is required", StringComparison.Ordinal)
            ? "Type the mark in Text, or choose a picture, to see it here."
            : message;

    private void ShowAfter(FlowFrame? frame, string? error, bool calculating)
    {
        if (frame is null)
        {
            _afterImg.Source = null;
            if (_padHost is not null) _padHost.Visibility = Visibility.Collapsed;
            _afterCap.Text = _stage == FlowStage.Crop ? "" : "";
            _afterNote.Text = error is null ? "" : Friendly(error);
            _afterNote.Foreground = Brush(error is not null && Friendly(error) == error ? "ShellDanger" : "ShellMuted");
            return;
        }
        _afterNote.Text = "";
        var dims = $"{frame.PixelWidth} x {frame.PixelHeight} px";
        if (_stage == FlowStage.Crop)
        {
            _afterCap.Text = "Result: " + dims;
            _afterCap.Foreground = Brush("ShellMuted");
            return;
        }
        _afterImg.Source = Decode(frame.Png);
        _afterCap.Text = calculating ? dims + " - working out the size" : dims;
        _afterCap.Foreground = Brush("ShellMuted");
        PlacePad(frame);
    }

    private void ShowSize(FlowFrame first, FlowFrame? refined, FlowSize m, FlowSource src)
    {
        if (m.Error is not null)
        {
            _afterNote.Text = m.Error;
            _afterNote.Foreground = Brush("ShellDanger");
            _afterCap.Text = "Would not run";
            _afterCap.Foreground = Brush("ShellDanger");
            return;
        }
        if (refined is not null && _stage != FlowStage.Crop)
            _afterImg.Source = Decode(refined.Png);
        var dims = $"{m.PixelWidth} x {m.PixelHeight} px";
        var bytes = m.Bytes ?? 0;
        var text = $"{dims} - {ToolPageModel.FormatSize(bytes)}";
        if (_model?.ToolId == "img-compress" && src.FileBytes > 0)
        {
            var saved = 100 - (int)Math.Round(bytes * 100.0 / src.FileBytes);
            text += saved > 0 ? $" (saves {saved}%)" : " (no smaller)";
        }
        if (m.Note is not null) text += ". " + char.ToUpperInvariant(m.Note[0]) + m.Note[1..];
        _afterCap.Text = text;
        _afterCap.Foreground = Brush("ShellMuted");
    }

    /// <summary>The pad covers exactly the drawn picture, so a click on a cell is a click on that part of the photo.</summary>
    private void PlacePad(FlowFrame frame)
    {
        if (_pad is null || _padHost is null || _afterBox is null) return;
        var w = _afterBox.ActualWidth > 0 ? _afterBox.ActualWidth : 452;
        var h = _afterBox.ActualHeight > 0 ? _afterBox.ActualHeight : 292;
        var scale = Math.Min(w / frame.PixelWidth, h / frame.PixelHeight);
        _padHost.Width = Math.Max(30, frame.PixelWidth * scale);
        _padHost.Height = Math.Max(30, frame.PixelHeight * scale);
        _padHost.Visibility = Visibility.Visible;
        _pad.Select(Value("position"));
    }
}
