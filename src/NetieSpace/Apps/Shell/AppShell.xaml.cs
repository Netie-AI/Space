using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using NetieSpace.Apps.Tools;
using NetieSpace.Helpers;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// Sidebar (categories, family switcher) and the content host (home / tool page) of one app window.
/// Also the root the snapshot renders, so it needs no Window around it.
/// </summary>
public partial class AppShell : UserControl
{
    private AppIdentity? _identity;
    private RecentFiles? _recent;
    private IReadOnlyList<RegisteredTool> _tools = Array.Empty<RegisteredTool>();
    private IReadOnlyList<HomeCategory> _categories = Array.Empty<HomeCategory>();
    private string? _activeCategoryForNav;

    public AppShell()
    {
        InitializeComponent();
        Home.ToolPicked += ShowTool;
        Home.RecentPicked += path => SelectFile(path);
        Home.PendingCleared += () => SelectFile(null);
    }

    public string? SelectedFile { get; private set; }

    public void Initialize(AppIdentity identity, RecentFiles recent, string? openPath)
    {
        _identity = identity;
        _recent = recent;
        SelectedFile = string.IsNullOrWhiteSpace(openPath) ? null : openPath;
        AppName.Text = identity.DisplayName;
        AppGlyph.Text = HomeModel.Monogram(identity.DisplayName.Replace("Netie ", "", StringComparison.Ordinal));
        BuildFamily(identity);
        Home.ShowLoading(identity.DisplayName);
    }

    public void SetTools(IReadOnlyList<RegisteredTool> tools)
    {
        _tools = tools;
        _categories = HomeModel.Categories(_identity!, tools);
        ShowHome(null);
    }

    public void SelectFile(string? path)
    {
        SelectedFile = string.IsNullOrWhiteSpace(path) ? null : path;
        Home.SetPending(SelectedFile);
    }

    public void ShowHome(string? category)
    {
        _activeCategoryForNav = category;
        BuildNav();
        Home.Bind(_identity!.DisplayName, _categories, category, _recent!.Load(), SelectedFile);
        Home.Visibility = Visibility.Visible;
        ToolPage.Visibility = Visibility.Collapsed;
    }

    public void ShowTool(string toolId)
    {
        var tool = _tools.FirstOrDefault(t => t.Entry.Id == toolId);
        if (tool is null)
            return;
        _activeCategoryForNav = tool.Entry.Category;
        BuildNav();
        ToolPage.Load(tool, _recent!, SelectedFile);
        Home.Visibility = Visibility.Collapsed;
        ToolPage.Visibility = Visibility.Visible;
    }

    private void BuildNav()
    {
        NavPanel.Children.Clear();
        NavPanel.Children.Add(NavButton("All tools", null, isOn: _activeCategoryForNav is null));
        foreach (var c in _categories)
            NavPanel.Children.Add(NavButton(c.Name, c.Name, isOn: _activeCategoryForNav == c.Name));
    }

    private Button NavButton(string label, string? category, bool isOn)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis },
            Style = (Style)FindResource(isOn ? "ShellNavItemOn" : "ShellNavItem"),
            ToolTip = label,
        };
        b.Click += (_, _) => ShowHome(category);
        return b;
    }

    private void BuildFamily(AppIdentity current)
    {
        FamilyPanel.Children.Clear();
        foreach (var target in HomeModel.FamilyTargets(current))
        {
            var args = target.Arguments;
            var b = new Button
            {
                Content = target.Label,
                Style = (Style)FindResource("ShellNavItem"),
                Margin = new Thickness(0, 4, 0, 0),
            };
            b.Click += (_, _) => StartApp(args);
            FamilyPanel.Children.Add(b);
        }
    }

    /// <summary>The other app is its own process, never a tab.</summary>
    private static void StartApp(string arguments)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return;
            Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"start {arguments} failed: {ex.Message}");
        }
    }
}
