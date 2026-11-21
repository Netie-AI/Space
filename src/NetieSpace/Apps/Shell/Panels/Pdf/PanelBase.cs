using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using NetieSpace.Apps.Tools;

namespace NetieSpace.Apps.Shell.Panels.Pdf;

/// <summary>Shared plumbing of the PDF panels: theme lookups, small controls, the Changed event, the Settled task.</summary>
internal abstract class PanelBase
{
    protected PanelBase(PanelContext ctx) => Ctx = ctx;

    protected PanelContext Ctx { get; }
    protected FrameworkElement Host => Ctx.Host;

    public event Action? Changed;
    protected void RaiseChanged() => Changed?.Invoke();

    protected CancellationTokenSource Cts = new();
    protected Task SettledTask = Task.CompletedTask;
    public Task Settled => SettledTask;

    public virtual RegisteredTool? RunTool => null;
    public virtual IReadOnlyCollection<string> HiddenOptions { get; } = Array.Empty<string>();
    public virtual void ApplyState(string state) { }

    protected Brush Brush(string key) => Host.TryFindResource(key) as Brush ?? Brushes.Gray;

    protected Style? StyleOf(string key) => Host.TryFindResource(key) as Style;

    protected TextBlock Text(string text, string style = "ShellMutedText", double size = 0, FontWeight? weight = null)
    {
        var t = new TextBlock { Text = text };
        if (StyleOf(style) is { } s) t.Style = s;
        if (size > 0) t.FontSize = size;
        if (weight is { } w) t.FontWeight = w;
        return t;
    }

    protected Button Button(string label, string tip, Action onClick, bool action = false)
    {
        var b = new Button { Content = label, ToolTip = tip, MinHeight = 30, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        if (action && StyleOf("ShellActionButton") is { } s) b.Style = s;
        AutomationProperties.SetName(b, tip.Length > 0 ? tip : label);
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>The thin accent bar that shows where a dragged item will land.</summary>
    protected Border InsertBar(bool vertical) => new()
    {
        Background = Brush("ShellAccent"),
        Width = vertical ? 3 : double.NaN,
        Height = vertical ? double.NaN : 3,
        CornerRadius = new CornerRadius(1.5),
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
        HorizontalAlignment = vertical ? HorizontalAlignment.Left : HorizontalAlignment.Stretch,
        VerticalAlignment = vertical ? VerticalAlignment.Stretch : VerticalAlignment.Top,
    };

    protected static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    public virtual void DecorateResult(Panel buttons, string? output) => PeekLauncher.Decorate(buttons, output);

    protected void Cancel()
    {
        Cts.Cancel();
        Cts = new CancellationTokenSource();
    }
}
