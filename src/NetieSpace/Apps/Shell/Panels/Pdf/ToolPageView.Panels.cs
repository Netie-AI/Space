using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NetieSpace.Apps.Shell.Panels.Pdf;
using NetieSpace.Apps.Tools;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// The tool page's side of the PDF panels (merge strip, page grid, signature placement). Everything the page does
/// for a panel lives here; ToolPageView.xaml.cs only calls these members at the few points named in the hook list.
/// </summary>
public partial class ToolPageView
{
    private IToolPanel? _panel;
    private bool _panelShowing;
    private bool _panelDropWired;
    private TextBlock? _panelHint;

    private bool PanelHides(string key) => _panel is { } p && p.HiddenOptions.Contains(key);

    /// <summary>Puts the tool's panel in place of the plain file list, or restores the list when the tool has none.</summary>
    private void AttachPanel(RegisteredTool tool)
    {
        _panel = null;
        PanelHost.Content = null;
        _panelHint = null;
        WorkColumn.MaxWidth = 640;
        FilesLabel.Visibility = Visibility.Visible;
        DropZone.Visibility = Visibility.Visible;
        AllowDrop = false;

        if (_model is null || !_model.Available) return;
        var ctx = new PanelContext(this, tool, key => _values.TryGetValue(key, out var read) ? read() : "", Browse);
        var panel = PdfPanels.For(tool.Entry.Id, ctx);
        if (panel is null) return;

        _panel = panel;
        _panelHint = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        _panelHint.Style = (Style)FindResource("ShellMutedText");
        var host = new StackPanel();
        host.Children.Add(panel.View);
        host.Children.Add(_panelHint);
        PanelHost.Content = host;
        FilesLabel.Visibility = Visibility.Collapsed;
        DropZone.Visibility = Visibility.Collapsed;
        WorkColumn.MaxWidth = double.PositiveInfinity;

        // A drop on the window (the title, the margins) goes to the panel too.
        AllowDrop = true;
        if (!_panelDropWired)
        {
            _panelDropWired = true;
            DragOver += (_, e) =>
            {
                if (_panel is null) return;
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            Drop += (_, e) =>
            {
                if (_panel is not null && e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
                    _panel.AddFiles(dropped);
                e.Handled = true;
            };
        }

        panel.Changed += () =>
        {
            if (!ReferenceEquals(panel, _panel) || _panelShowing) return;
            SetFiles(panel.Files.ToList());
        };
    }

    /// <summary>The panel's own view of the files, called from SetFiles. Re-entrant Changed events are ignored while it runs.</summary>
    private void PanelShow(IReadOnlyList<string> files)
    {
        if (_panel is null) return;
        _panelShowing = true;
        try { _panel.Show(files); }
        finally { _panelShowing = false; }
    }

    /// <summary>True when the page has no panel or the panel is ready; otherwise the reason shows under the panel.</summary>
    private bool PanelReady()
    {
        if (_panel is null) return true;
        var ok = _panel.Ready(out var why);
        if (_panelHint is not null)
        {
            _panelHint.Text = ok ? "" : why ?? "";
            _panelHint.Visibility = ok || string.IsNullOrEmpty(why) ? Visibility.Collapsed : Visibility.Visible;
        }
        return ok;
    }

    /// <summary>The panel replaces the strip beside the options; nothing else is drawn there.</summary>
    private bool PanelCoversPreview()
    {
        if (_panel is null) return false;
        PreviewPanel.Visibility = Visibility.Collapsed;
        return true;
    }

    /// <summary>The tool a run uses: the page's own, or the one the panel writes through (Rotate writes an organize plan).</summary>
    private ToolPageModel PanelModel(ToolPageModel page) =>
        _panel?.RunTool is { } other ? new ToolPageModel(other) : page;

    /// <summary>Option values the panel decides win over the controls; they are added after the page remembered its own.</summary>
    private void PanelOverrides(Dictionary<string, string> values)
    {
        if (_panel is null) return;
        foreach (var kv in _panel.Options())
            values[kv.Key] = kv.Value;
    }

    private void PanelDecorateResult() => _panel?.DecorateResult(ResultButtons, _output);

    /// <summary>
    /// Snapshot path: hands the panel the files from the command line, waits until its pictures are drawn, applies the
    /// --state text, waits again, and returns the one line CI asserts on. Null when the tool has no panel.
    /// </summary>
    internal async Task<string?> SnapshotPanelAsync(IReadOnlyList<string> files, string? state)
    {
        if (_panel is not { } panel) return null;
        panel.AddFiles(files);
        await panel.Settled;
        if (!string.IsNullOrWhiteSpace(state))
        {
            panel.ApplyState(state!);
            await panel.Settled;
        }
        RefreshAction();
        UpdateLayout();
        return panel.Describe();
    }
}

/// <summary>Runs the snapshot's panel step on the UI thread, pumping the dispatcher until it settles or times out.</summary>
internal static class PanelSnapshot
{
    public static string? Run(ToolPageView page, IReadOnlyList<string> files, string? state, TimeSpan timeout)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        // No message loop is running yet: without this, an await would resume on a pool thread and touch the UI there.
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

        var frame = new DispatcherFrame();
        string? line = null;
        Exception? error = null;
        var timedOut = false;
        var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = timeout };
        timer.Tick += (_, _) => { timedOut = true; frame.Continue = false; };
        timer.Start();
        page.SnapshotPanelAsync(files, state).ContinueWith(t =>
        {
            if (t.IsFaulted) error = t.Exception!.GetBaseException();
            else line = t.Result;
            dispatcher.BeginInvoke(new Action(() => frame.Continue = false));
        }, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        timer.Stop();

        if (error is not null) throw new InvalidOperationException("panel snapshot failed: " + error.Message, error);
        if (timedOut) throw new TimeoutException($"the panel did not settle within {timeout.TotalSeconds:0} seconds");
        return line;
    }
}
