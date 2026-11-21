using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using NetieSpace.Apps.Tools;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// The shell's share of the tool page: errors that read like a person wrote them, the way out of an unavailable tool,
/// names for every control, and a drop from anywhere in the window. Kept apart from the file and preview code, which
/// other lanes extend.
/// </summary>
public partial class ToolPageView
{
    private Action? _unavailableAction;
    private Action? _fixAction;

    /// <summary>The page asks to go to another tool (an error offered it, e.g. Unlock PDF).</summary>
    public event Action<string>? ToolRequested;

    /// <summary>The page asks for the tool list.</summary>
    public event Action? ToolsRequested;

    /// <summary>An unavailable tool: one sentence on what it needs, one way forward, the engine's own words in a fold.</summary>
    public void ShowUnavailable(FriendlyError f)
    {
        ReasonText.Text = f.Sentence;
        UnavailableDetailsText.Text = f.Details;
        UnavailableDetails.Visibility = f.Details.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        UnavailableDetails.IsExpanded = false;
        _unavailableAction = ActionFor(f, UnavailableActionButton);
        UnavailableActionButton.Style = (Style)FindResource("ShellActionButton");
    }

    /// <summary>
    /// A file dropped on the window while this tool is open: added when the tool takes every file, otherwise left to the
    /// shell, which shows the tools that suit it. Returns whether the tool took them.
    /// </summary>
    public bool TryAcceptDrop(IReadOnlyList<string> files)
    {
        if (_model is null || !_model.Available || _running || files.Count == 0 || !files.All(_model.Accepts))
            return false;
        AddFiles(files);
        return true;
    }

    /// <summary>A failed run: the friendly sentence replaces the engine text, which moves to the Details fold.</summary>
    private void ApplyFriendly(ToolResult result, ResultSummary summary)
    {
        ResultDetails.Visibility = Visibility.Collapsed;
        FixButton.Visibility = Visibility.Collapsed;
        _fixAction = null;
        if (summary.Ok)
            return;

        var f = FriendlyError.ForResult(result);
        ResultText.Text = f.Sentence;
        ResultDetailsText.Text = f.Details;
        ResultDetails.Visibility = Visibility.Visible;
        ResultDetails.IsExpanded = false;
        _fixAction = ActionFor(f, FixButton);
    }

    /// <summary>Wires one error action to a button; returns the handler, or null (and hides the button) when there is none.</summary>
    private Action? ActionFor(FriendlyError f, Button button)
    {
        Action? act = f.Action switch
        {
            ErrorAction.ChooseFile => Browse,
            ErrorAction.ChangeFolder => ChangeFolder,
            ErrorAction.TryAgain => () => _ = RunAsync(),
            ErrorAction.FixOptions => () => OptionsCard.BringIntoView(),
            ErrorAction.OpenTool when f.ActionArg is { } id => () => ToolRequested?.Invoke(id),
            ErrorAction.ShowTools => () => ToolsRequested?.Invoke(),
            _ => null,
        };
        button.Content = f.ActionLabel;
        AutomationProperties.SetName(button, f.ActionLabel);
        button.Visibility = act is null ? Visibility.Collapsed : Visibility.Visible;
        button.Click -= OnActionClick;
        button.Click += OnActionClick;
        return act;
    }

    private void OnActionClick(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, FixButton))
            _fixAction?.Invoke();
        else
            _unavailableAction?.Invoke();
    }

    /// <summary>
    /// Every control gets a name a screen reader and UI Automation can say: an option's box is named by its label, a file's
    /// remove button by the file. Runs after each rebuild of the file list, which is also after the options are built.
    /// </summary>
    private void NameControls()
    {
        foreach (var child in OptionsHost.Children)
        {
            if (child is not StackPanel block || block.Children.Count == 0)
                continue;
            var label = block.Children[0] is TextBlock t ? t.Text : null;
            if (string.IsNullOrWhiteSpace(label))
                continue;
            foreach (var c in Descendants(block))
            {
                if (c is Button b && b.Content is string text && AutomationProperties.GetName(b).Length == 0)
                    AutomationProperties.SetName(b, text + " for " + label);
                else if ((c is TextBox || c is ComboBox) && AutomationProperties.GetName(c).Length == 0)
                    AutomationProperties.SetName(c, label);
            }
        }

        for (var i = 0; i < FileRows.Children.Count && i < _files.Count; i++)
            if (FileRows.Children[i] is DockPanel row)
                foreach (var c in row.Children.OfType<Button>())
                    if (AutomationProperties.GetName(c).Length == 0)
                        AutomationProperties.SetName(c, "Remove " + System.IO.Path.GetFileName(_files[i]));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var d in Descendants(child))
                yield return d;
        }
    }
}
