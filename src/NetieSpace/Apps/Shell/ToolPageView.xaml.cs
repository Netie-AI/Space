using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NetieSpace.Apps.Tools;
using NetieSpace.Helpers;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// One tool: files, one control per option, one action, progress, result. Controls come from
/// <see cref="ToolPageModel"/> descriptors; this class only instantiates them and forwards clicks.
/// </summary>
public partial class ToolPageView : UserControl
{
    private readonly Dictionary<string, Func<string>> _values = new(StringComparer.Ordinal);
    private ToolPageModel? _model;
    private RecentFiles? _recent;
    private IReadOnlyList<string> _files = Array.Empty<string>();
    private RunQueue? _queue;
    private AppIdentity? _app;
    private ToolStateStore? _state;
    private ToolMemory _memory = ToolMemory.Empty;
    private string? _chosenOut;
    private string? _output;
    private bool _running;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _resultCts;

    public ToolPageView()
    {
        InitializeComponent();
        BrowseButton.Click += (_, _) => Browse();
        ActionButton.Click += async (_, _) => await RunAsync();
        OpenButton.Click += (_, _) => OpenOutput();
        ShowButton.Click += (_, _) => ShowOutput();
        BackButton.Click += (_, _) => BackRequested?.Invoke();
        DropZone.DragOver += OnDragOver;
        DropZone.Drop += OnDrop;
        ChangeFolderButton.Click += (_, _) => ChangeFolder();
        CancelButton.Click += (_, _) => _queue?.Cancel();
    }

    /// <summary>The page has no chrome of its own to leave by; the shell decides where back goes.</summary>
    public event Action? BackRequested;
    private string _outFolder = "";

    public void Load(RegisteredTool tool, RecentFiles recent, string? pendingFile, AppIdentity app, ToolStateStore state)
    {
        _queue?.Cancel();
        _queue = null;
        _model = new ToolPageModel(tool);
        _recent = recent;
        _app = app;
        _state = state;
        _memory = state.Load(tool.Entry);
        _chosenOut = _memory.OutDir;
        _output = null;
        _running = false;
        _values.Clear();

        _previewCts?.Cancel();
        _resultCts?.Cancel();
        TitleText.Text = _model.Name;
        CategoryText.Text = tool.Entry.Category;
        DrawTitleIcon(tool.Entry);
        ActionButton.Content = _model.ActionLabel;
        RunPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;

        var reason = _model.UnavailableLine;
        UnavailableBox.Visibility = reason is null ? Visibility.Collapsed : Visibility.Visible;
        ReasonText.Text = reason ?? "";
        var offer = _model.InstallOfferLine;
        OfferText.Text = offer ?? "";
        OfferText.Visibility = offer is null ? Visibility.Collapsed : Visibility.Visible;

        // An unavailable tool shows its reason and offer, and no controls that cannot act.
        WorkPanel.Visibility = _model.Available ? Visibility.Visible : Visibility.Collapsed;

        BuildOptions();
        OptionsCard.Visibility = _model.Options.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ImageFlow.Attach(_model, OptionsHost, PreviewPanel, files => SetFiles(files)); // hook 1 of 5: image panel
        SetFiles(pendingFile is not null && _model.Accepts(pendingFile)
            ? new[] { pendingFile }
            : Array.Empty<string>());
        ImageFlow.LoadSnapshot(); // hook 2 of 5: only --snapshot with --flow-files does anything
    }

    private void BuildOptions()
    {
        OptionsHost.Children.Clear();
        foreach (var d0 in _model!.Options)
        {
            // The last used value, when the store still holds a valid one, replaces the entry default.
            var d = _memory.Options.TryGetValue(d0.Key, out var remembered) ? d0 with { Default = remembered } : d0;
            var block = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            FrameworkElement control;
            switch (d.Control)
            {
                case OptionControl.CheckBox:
                {
                    var cb = new CheckBox { Content = d.Label, IsChecked = d.Default == "true" };
                    _values[d.Key] = () => cb.IsChecked == true ? "true" : "false";
                    block.Children.Add(cb);
                    OptionsHost.Children.Add(block);
                    continue;
                }
                case OptionControl.ComboBox:
                case OptionControl.GridCombo:
                {
                    // The list shows readable labels; the value the runner and the memory file see is unchanged.
                    var items = d.Choices.Select(c => new ChoiceItem(c, d.ChoiceLabel(c))).ToList();
                    var combo = new ComboBox
                    {
                        ItemsSource = items,
                        DisplayMemberPath = nameof(ChoiceItem.Label),
                        SelectedValuePath = nameof(ChoiceItem.Value),
                        MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 260,
                    };
                    combo.SelectedValue = d.Choices.FirstOrDefault(c => string.Equals(c, d.Default, StringComparison.OrdinalIgnoreCase))
                                          ?? d.Choices.FirstOrDefault();
                    _values[d.Key] = () => combo.SelectedValue as string ?? "";
                    control = combo;
                    break;
                }
                case OptionControl.NumberBox:
                {
                    var tb = new TextBox { Text = d.Default, MaxWidth = 140, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 100 };
                    var allowMinus = d.Min is null or < 0;
                    tb.PreviewTextInput += (_, e) =>
                        e.Handled = !e.Text.All(ch => char.IsDigit(ch) || (allowMinus && ch == '-'));
                    if (d.Min is not null || d.Max is not null)
                        tb.ToolTip = $"{d.Min?.ToString() ?? "..."} - {d.Max?.ToString() ?? "..."}";
                    _values[d.Key] = () => tb.Text;
                    control = tb;
                    break;
                }
                case OptionControl.FileBrowse:
                {
                    var tb = new TextBox { Text = d.Default };
                    var browse = new Button { Content = "Browse", Margin = new Thickness(8, 0, 0, 0) };
                    browse.Click += (_, _) =>
                    {
                        var dlg = new OpenFileDialog();
                        if (dlg.ShowDialog() == true)
                            tb.Text = dlg.FileName;
                    };
                    var row = new DockPanel();
                    DockPanel.SetDock(browse, Dock.Right);
                    row.Children.Add(browse);
                    row.Children.Add(tb);
                    _values[d.Key] = () => tb.Text;
                    control = row;
                    break;
                }
                default:
                {
                    // "1-end" reads as an empty box with the hint "All pages"; blank means the tool's own default.
                    var tb = new TextBox { Text = d.StartText };
                    _values[d.Key] = () => tb.Text;
                    control = d.Hint is { } hint ? WithHint(tb, hint) : tb;
                    break;
                }
            }

            var label = new TextBlock { Text = d.Label, Margin = new Thickness(0, 0, 0, 6) };
            label.Style = (Style)FindResource("ShellMutedText");
            block.Children.Add(label);
            block.Children.Add(control);
            OptionsHost.Children.Add(block);
        }
    }

    /// <summary>A text box with grey text over it while it is empty.</summary>
    private FrameworkElement WithHint(TextBox tb, string hint)
    {
        tb.MaxWidth = 360;
        tb.HorizontalAlignment = HorizontalAlignment.Left;
        tb.MinWidth = 260;
        var note = new TextBlock
        {
            Text = hint,
            Style = (Style)FindResource("ShellMutedText"),
            FontSize = 13,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            // border 1 + TextBox padding 9 + the text view's own 2 px: the hint starts where typed text does.
            Margin = new Thickness(12, 0, 0, 0),
        };
        note.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        tb.TextChanged += (_, _) => note.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var grid = new Grid { MaxWidth = 360, MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left };
        grid.Children.Add(tb);
        grid.Children.Add(note);
        return grid;
    }

    internal sealed record ChoiceItem(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>The tool's own icon in the title badge; the category line drawing when no icon font is installed.</summary>
    private void DrawTitleIcon(ToolEntry entry)
    {
        var glyph = IconFont.Available ? ToolGlyphs.For(entry.Id) : null;
        TitleGlyph.Text = glyph ?? "";
        TitleGlyph.Visibility = glyph is null ? Visibility.Collapsed : Visibility.Visible;
        TitlePath.Visibility = glyph is null ? Visibility.Visible : Visibility.Collapsed;
        if (glyph is null)
        {
            try { TitlePath.Data = Geometry.Parse(TileGlyphs.ForCategory(entry.Category)); }
            catch (FormatException) { TitlePath.Data = Geometry.Parse(TileGlyphs.Fallback); }
        }
    }

    private void SetFiles(IReadOnlyList<string> files)
    {
        _files = files;
        FileRows.Children.Clear();
        foreach (var f in files)
        {
            var path = f;
            var remove = new Button { Content = "Remove", MinHeight = 26, Padding = new Thickness(8, 2, 8, 2) };
            remove.Click += (_, _) => SetFiles(_files.Where(x => !string.Equals(x, path, StringComparison.OrdinalIgnoreCase)).ToList());
            DockPanel.SetDock(remove, Dock.Right);
            var name = new TextBlock
            {
                Text = path,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = path,
            };
            var size = SizeOf(path);
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(remove);
            if (size is not null)
            {
                var sizeText = new TextBlock
                {
                    Text = ToolPageModel.FormatSize(size.Value),
                    Style = (Style)FindResource("ShellCaption"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 12, 0),
                };
                DockPanel.SetDock(sizeText, Dock.Right);
                row.Children.Add(sizeText);
            }
            row.Children.Add(name);
            FileRows.Children.Add(row);
        }

        ImageFlow.SetFiles(files); // hook 3 of 5
        RefreshPreview();
        NoFilesText.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BrowseButton.HorizontalAlignment = files.Count == 0 ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        RefreshAction();
    }

    private void RefreshAction()
    {
        if (_model is null) return;
        var check = _model.ValidateFiles(_files);
        FilesMessage.Text = check.Message ?? "";
        FilesMessage.Visibility = !check.Ok && _files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Before a file is chosen the folder is not known yet; show the name it will carry.
        var folder = OutputFolder.Resolve(_app!, _files, _chosenOut);
        _outFolder = folder;
        OutFolderText.Text = string.IsNullOrWhiteSpace(folder) ? _app!.DisplayName : folder;
        OutFolderText.ToolTip = OutFolderText.Text;
        ActionButton.IsEnabled = _model.ActionEnabled(_files) && !_running;
    }

    private void ChangeFolder()
    {
        var dlg = new OpenFolderDialog();
        var current = _outFolder;
        if (!string.IsNullOrWhiteSpace(current) && IoDirectory.Exists(current))
            dlg.InitialDirectory = current;
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
        {
            _chosenOut = dlg.FolderName;
            RefreshAction();
        }
    }

    private void Browse()
    {
        if (_model is null) return;
        var dlg = new OpenFileDialog { Filter = _model.BrowseFilter(), Multiselect = _model.AllowMultiple };
        if (dlg.ShowDialog() == true)
            AddFiles(dlg.FileNames);
    }

    private void AddFiles(IEnumerable<string> incoming)
    {
        if (_model is null) return;
        var accepted = incoming.Where(f => IoFile.Exists(f)).ToList();
        SetFiles(_model.MaxFiles <= 1 && accepted.Count > 0
            ? new[] { accepted[0] }
            : ToolPageModel.MergeFiles(_files, accepted));
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
            AddFiles(dropped);
        e.Handled = true;
    }

    private async Task RunAsync()
    {
        if (_model is null || _running) return;
        var model = _model;
        var files = _files;

        var folder = OutputFolder.Prepare(OutputFolder.Resolve(_app!, files, _chosenOut));
        if (!folder.Ok)
        {
            await ShowResultAsync(new ToolResult(false, ToolOutcome.Invalid, model.ToolId, model.Tool.Entry.Engine.Name,
                Array.Empty<string>(), folder.Message, null, 0));
            return;
        }

        _running = true;
        _output = null;
        ActionButton.IsEnabled = false;
        ResultPanel.Visibility = Visibility.Collapsed;
        RunPanel.Visibility = Visibility.Visible;
        Bar.IsIndeterminate = true;
        ProgressText.Text = "";

        foreach (var f in files)
            _recent?.Add(f);

        var values = _values.ToDictionary(kv => kv.Key, kv => kv.Value(), StringComparer.Ordinal);
        _state?.Save(model.Tool.Entry, values, _chosenOut);
        var queue = RunQueue.For(model.Tool.Entry, files);
        _queue = queue;
        CancelButton.IsEnabled = true;
        DrawQueue(queue);

        var progress = new Progress<ToolProgress>(p =>
        {
            if (!ReferenceEquals(queue, _queue)) return;
            Bar.IsIndeterminate = false;
            Bar.Value = queue.PerFile
                ? (queue.DoneCount + queue.FailedCount) * 100.0 / Math.Max(1, queue.Items.Count) + p.Percent / (double)Math.Max(1, queue.Items.Count)
                : p.Percent;
            ProgressText.Text = p.Message;
        });

        IReadOnlyList<ToolResult> results;
        try
        {
            // Off the dispatcher: a runner that works synchronously before its first await must not freeze the window.
            results = await queue.RunAsync(
                (batch, ct) =>
                {
                    var request = model.BuildRequest(batch, folder.Folder, values);
                    return Task.Run(() => ToolRunner.RunAsync(model.Tool, request, progress, ct));
                },
                () => DrawQueue(queue));
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"tool {model.ToolId}");
            queue.MarkAllRunning();
            results = new[]
            {
                new ToolResult(false, ToolOutcome.Failed, model.ToolId, model.Tool.Entry.Engine.Name,
                    Array.Empty<string>(), ex.GetType().Name + ": " + ex.Message, null, 0),
            };
            queue.Settle(results[0]);
        }

        if (!ReferenceEquals(model, _model) || !ReferenceEquals(queue, _queue))
            return; // the page moved to another tool while this one ran

        _running = false;
        RunPanel.Visibility = Visibility.Collapsed;
        await ShowResultAsync(queue.Combine(results, model.ToolId, model.Tool.Entry.Engine.Name));
    }

    private static long? SizeOf(string path)
    {
        try { return IoFile.Exists(path) ? new System.IO.FileInfo(path).Length : null; }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// The result as a card: thumbnail of the output when the engines can draw one, its name, size and type,
    /// the "Done: path" line, and Open / Show in folder. The card is first on the page. The thumbnail is awaited
    /// (at most two seconds) before the card appears, so it never pops in after the person has looked.
    /// </summary>
    private async Task ShowResultAsync(ToolResult result)
    {
        var model = _model;
        _output = ToolPageModel.PrimaryOutput(result);
        var summary = ToolPageModel.Summarize(result, SizeOf);

        ResultTitle.Text = summary.Title;
        ResultTitle.ToolTip = summary.Primary;
        ResultDetail.Text = summary.Detail;
        ResultDetail.Visibility = summary.Detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ResultText.Text = summary.Line;
        var danger = (Brush)FindResource("ShellDanger");
        ResultTitle.Foreground = summary.Ok ? (Brush)FindResource("ShellInk") : danger;
        ResultText.Foreground = summary.Ok ? (Brush)FindResource("ShellMuted") : danger;
        ResultCard.BorderBrush = summary.Ok ? (Brush)FindResource("ShellAccent") : danger;
        ResultMark.Background = summary.Ok ? (Brush)FindResource("ShellAccent") : danger;
        ResultMarkGlyph.Foreground = summary.Ok ? (Brush)FindResource("ShellAccentInk") : Brushes.White;
        // Check mark / cross in the icon font; plain letters if no icon font exists.
        ResultMarkGlyph.Text = IconFont.Available ? (summary.Ok ? "\uE73E" : "\uE711") : (summary.Ok ? "+" : "x");
        ResultButtons.Visibility = _output is null ? Visibility.Collapsed : Visibility.Visible;

        ResultThumb.Source = null;
        ResultThumbBadge.Text = "";
        ResultThumbBox.Visibility = summary.Primary is null ? Visibility.Collapsed : Visibility.Visible;
        if (summary.Primary is { } primary)
        {
            _resultCts?.Cancel();
            var cts = _resultCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var source = await ThumbnailOfAsync(primary, cts.Token);
            if (!ReferenceEquals(model, _model))
                return; // the page moved to another tool while the thumbnail was drawn
            ResultThumb.Source = source;
            ResultThumbBadge.Text = source is null ? PreviewPlan.TypeBadge(primary) : "";
        }

        ResultPanel.Visibility = Visibility.Visible;
        ResultPanel.BringIntoView();
        RefreshAction();
    }

    /// <summary>One picture of a file: the first page of a PDF, the picture itself, or null for any other type or a failure.</summary>
    private static async Task<ImageSource?> ThumbnailOfAsync(string path, CancellationToken ct)
    {
        try
        {
            switch (PreviewPlan.KindOf(path))
            {
                case PreviewKind.Pdf:
                    return (await Thumbnails.PdfPagesAsync(path, 1, 160, ct))?.Pages.FirstOrDefault();
                case PreviewKind.Image:
                    return Thumbnails.Picture(path, 240)?.Image;
                default:
                    return null;
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// The panel beside the options: pages of the first file as a thumbnail strip, its picture, or a typed
    /// placeholder. A file that cannot be drawn says so in the panel; nothing is silently blank.
    /// </summary>
    private async void RefreshPreview()
    {
        _previewCts?.Cancel();
        if (ImageFlow.Active) return; // hook 4 of 5: image tools draw their own preview
        PreviewStrip.Children.Clear();
        PreviewMore.Visibility = Visibility.Collapsed;
        if (_files.Count == 0)
        {
            PreviewPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var first = _files[0];
        var cts = _previewCts = new CancellationTokenSource();
        PreviewPanel.Visibility = Visibility.Visible;
        PreviewTitle.Text = IoPath.GetFileName(first);
        PreviewTitle.ToolTip = first;
        var more = _files.Count - 1;
        var size = SizeOf(first);
        PreviewCaption.Text = size is null ? "" : ToolPageModel.FormatSize(size.Value);
        PreviewStripScroll.Visibility = Visibility.Collapsed;
        PreviewPictureBox.Visibility = Visibility.Collapsed;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        if (more > 0)
        {
            PreviewMore.Text = more == 1 ? "and 1 more file" : $"and {more} more files";
            PreviewMore.Visibility = Visibility.Visible;
        }

        try
        {
            switch (PreviewPlan.KindOf(first))
            {
                case PreviewKind.Pdf:
                {
                    ShowPlaceholder(first, "Drawing pages");
                    var strip = await Thumbnails.PdfPagesAsync(first, PreviewPlan.MaxPages, 224, cts.Token);
                    if (cts.IsCancellationRequested) return;
                    if (strip is null || strip.Pages.Count == 0)
                    {
                        ShowPlaceholder(first, "No preview. The file may be protected or damaged.");
                        return;
                    }
                    for (var i = 0; i < strip.Pages.Count; i++)
                        PreviewStrip.Children.Add(PageThumb(strip.Pages[i], i + 1));
                    PreviewCaption.Text = PreviewPlan.PageCaption(strip.PageCount, strip.Pages.Count)
                        + (size is null ? "" : " - " + ToolPageModel.FormatSize(size.Value));
                    PreviewPlaceholder.Visibility = Visibility.Collapsed;
                    PreviewStripScroll.Visibility = Visibility.Visible;
                    break;
                }
                case PreviewKind.Image:
                {
                    var pic = await Task.Run(() => Thumbnails.Picture(first, 640), cts.Token);
                    if (cts.IsCancellationRequested) return;
                    if (pic is null)
                    {
                        ShowPlaceholder(first, "No preview for this picture type.");
                        return;
                    }
                    PreviewPicture.Source = pic.Value.Image;
                    PreviewCaption.Text = $"{pic.Value.PixelWidth} x {pic.Value.PixelHeight} px"
                        + (size is null ? "" : " - " + ToolPageModel.FormatSize(size.Value));
                    PreviewPictureBox.Visibility = Visibility.Visible;
                    break;
                }
                default:
                    ShowPlaceholder(first, "");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // a newer file list replaced this one
        }
    }

    private void ShowPlaceholder(string path, string note)
    {
        PreviewBadge.Text = PreviewPlan.TypeBadge(path);
        PreviewNote.Text = note;
        PreviewNote.Visibility = note.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        PreviewPlaceholder.Visibility = Visibility.Visible;
    }

    private FrameworkElement PageThumb(ImageSource page, int number)
    {
        var frame = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)FindResource("ShellLine"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = new System.Windows.Controls.Image { Source = page, Stretch = Stretch.Uniform },
        };
        RenderOptions.SetBitmapScalingMode(frame, BitmapScalingMode.HighQuality);
        var stack = new StackPanel { Width = 116, Margin = new Thickness(0, 0, 10, 12) };
        stack.Children.Add(frame);
        stack.Children.Add(new TextBlock
        {
            Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Style = (Style)FindResource("ShellCaption"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        });
        return stack;
    }

    /// <summary>Per-file state under the progress bar; only drawn when more than one file is queued.</summary>
    private void DrawQueue(RunQueue queue)
    {
        ImageFlow.ShowQueue(queue); // hook 5 of 5
        QueueRows.Children.Clear();
        QueueRows.Visibility = queue.ShowsList ? Visibility.Visible : Visibility.Collapsed;
        if (!queue.ShowsList)
            return;
        foreach (var item in queue.Items)
        {
            var state = new TextBlock
            {
                Text = RunQueue.Label(item.State) + (item.State == QueueState.Failed && item.Message is not null ? ": " + item.Message : ""),
                Style = (Style)FindResource("ShellMutedText"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(12, 0, 0, 0),
                ToolTip = item.Message,
            };
            if (item.State == QueueState.Failed)
                state.Foreground = (System.Windows.Media.Brush)FindResource("ShellDanger");
            var name = new TextBlock { Text = item.Name, MinWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = item.Path };
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(name, Dock.Left);
            row.Children.Add(name);
            row.Children.Add(state);
            QueueRows.Children.Add(row);
        }
    }

    private void OpenOutput()
    {
        if (_output is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(_output) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"open output failed ({_output}): {ex.Message}");
            ResultText.Text = ex.Message;
        }
    }

    private void ShowOutput()
    {
        if (_output is null) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", ToolPageModel.ExplorerSelectArguments(_output)) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"show in folder failed ({_output}): {ex.Message}");
            ResultText.Text = ex.Message;
        }
    }
}
