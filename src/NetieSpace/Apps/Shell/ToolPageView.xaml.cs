using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
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

        TitleText.Text = _model.Name;
        CategoryText.Text = tool.Entry.Category;
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
        SetFiles(pendingFile is not null && _model.Accepts(pendingFile)
            ? new[] { pendingFile }
            : Array.Empty<string>());
    }

    private void BuildOptions()
    {
        OptionsHost.Children.Clear();
        foreach (var d0 in _model!.Options)
        {
            // The last used value, when the store still holds a valid one, replaces the entry default.
            var d = _memory.Options.TryGetValue(d0.Key, out var remembered) ? d0 with { Default = remembered } : d0;
            var block = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
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
                    var combo = new ComboBox { ItemsSource = d.Choices, MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 220 };
                    combo.SelectedItem = d.Choices.FirstOrDefault(c => string.Equals(c, d.Default, StringComparison.OrdinalIgnoreCase))
                                         ?? d.Choices.FirstOrDefault();
                    _values[d.Key] = () => combo.SelectedItem as string ?? "";
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
                    var tb = new TextBox { Text = d.Default };
                    _values[d.Key] = () => tb.Text;
                    control = tb;
                    break;
                }
            }

            var label = new TextBlock { Text = d.Label, Margin = new Thickness(0, 0, 0, 4) };
            label.Style = (Style)FindResource("ShellMutedText");
            block.Children.Add(label);
            block.Children.Add(control);
            OptionsHost.Children.Add(block);
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
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(remove);
            row.Children.Add(name);
            FileRows.Children.Add(row);
        }

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
            ShowResult(new ToolResult(false, ToolOutcome.Invalid, model.ToolId, model.Tool.Entry.Engine.Name,
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
        ShowResult(queue.Combine(results, model.ToolId, model.Tool.Entry.Engine.Name));
    }

    private void ShowResult(ToolResult result)
    {
        _output = ToolPageModel.PrimaryOutput(result);
        ResultText.Text = ToolPageModel.ResultLine(result);
        ResultText.Foreground = result.Ok
            ? (System.Windows.Media.Brush)FindResource("ShellInk")
            : (System.Windows.Media.Brush)FindResource("ShellDanger");
        ResultButtons.Visibility = _output is null ? Visibility.Collapsed : Visibility.Visible;
        ResultPanel.Visibility = Visibility.Visible;
        RefreshAction();
    }

    /// <summary>Per-file state under the progress bar; only drawn when more than one file is queued.</summary>
    private void DrawQueue(RunQueue queue)
    {
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
