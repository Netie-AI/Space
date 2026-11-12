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
    private CancellationTokenSource? _cts;
    private string? _output;
    private bool _running;

    public ToolPageView()
    {
        InitializeComponent();
        BrowseButton.Click += (_, _) => Browse();
        ActionButton.Click += async (_, _) => await RunAsync();
        OpenButton.Click += (_, _) => OpenOutput();
        ShowButton.Click += (_, _) => ShowOutput();
        DropZone.DragOver += OnDragOver;
        DropZone.Drop += OnDrop;
    }

    public void Load(RegisteredTool tool, RecentFiles recent, string? pendingFile)
    {
        _cts?.Cancel();
        _model = new ToolPageModel(tool);
        _recent = recent;
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
        foreach (var d in _model!.Options)
        {
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
        RefreshAction();
    }

    private void RefreshAction()
    {
        if (_model is null) return;
        var check = _model.ValidateFiles(_files);
        FilesMessage.Text = check.Message ?? "";
        FilesMessage.Visibility = !check.Ok && _files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ActionButton.IsEnabled = _model.ActionEnabled(_files) && !_running;
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
        var request = model.BuildRequest(files, ToolPageModel.DefaultOutDir(files), values);
        var progress = new Progress<ToolProgress>(p =>
        {
            Bar.IsIndeterminate = false;
            Bar.Value = p.Percent;
            ProgressText.Text = p.Message;
        });

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        ToolResult result;
        try
        {
            // Off the dispatcher: a runner that works synchronously before its first await must not freeze the window.
            result = await Task.Run(() => ToolRunner.RunAsync(model.Tool, request, progress, ct));
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"tool {model.ToolId}");
            result = new ToolResult(false, ToolOutcome.Failed, model.ToolId, model.Tool.Entry.Engine.Name,
                Array.Empty<string>(), ex.GetType().Name + ": " + ex.Message, null, 0);
        }

        if (!ReferenceEquals(model, _model))
            return; // the page moved to another tool while this one ran

        _running = false;
        RunPanel.Visibility = Visibility.Collapsed;
        _output = ToolPageModel.PrimaryOutput(result);
        ResultText.Text = ToolPageModel.ResultLine(result);
        ResultText.Foreground = result.Ok
            ? (System.Windows.Media.Brush)FindResource("ShellInk")
            : (System.Windows.Media.Brush)FindResource("ShellDanger");
        ResultButtons.Visibility = _output is null ? Visibility.Collapsed : Visibility.Visible;
        ResultPanel.Visibility = Visibility.Visible;
        RefreshAction();
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
