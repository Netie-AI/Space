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
    private ToolStateStore? _state;
    private string? _activeCategoryForNav;
    private string? _homeCategory;

    public AppShell()
    {
        InitializeComponent();
        Home.ToolPicked += ShowTool;
        Home.RecentPicked += path => SelectFile(path);
        Home.PendingCleared += () => SelectFile(null);
        Home.SearchChanged += _ => RefreshHome();
        Home.FileDropped += path => SelectFile(path);
        ToolPage.BackRequested += () => ShowHome(null);
    }

    public string? SelectedFile { get; private set; }

    public void Initialize(AppIdentity identity, RecentFiles recent, string? openPath, ToolStateStore? state = null)
    {
        _identity = identity;
        AppAccents.Apply(identity);
        _recent = recent;
        _state = state ?? new ToolStateStore(ToolStateStore.DefaultPath(identity));
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
        // The grid leads with the tools that take this file, so it is rebuilt when it is on screen.
        if (_identity is not null && Home.Visibility == Visibility.Visible)
            RefreshHome();
    }

    public void ShowHome(string? category)
    {
        _activeCategoryForNav = category;
        _homeCategory = category;
        BuildNav();
        RefreshHome();
        Home.Visibility = Visibility.Visible;
        ToolPage.Visibility = Visibility.Collapsed;
    }

    private void RefreshHome()
    {
        var grid = HomeGrid.Build(_categories, _homeCategory, Home.Query, SelectedFile);
        Home.Bind(_identity!.DisplayName, grid, _homeCategory, _recent!.Load(), SelectedFile);
    }

    public void ShowTool(string toolId)
    {
        var tool = _tools.FirstOrDefault(t => t.Entry.Id == toolId);
        if (tool is null)
            return;
        _activeCategoryForNav = tool.Entry.Category;
        Home.CloseTooltips();
        BuildNav();
        ToolPage.Load(tool, _recent!, SelectedFile, _identity!, _state!);
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
            // No tooltip: the label is on the button, and a tooltip on a button that is rebuilt by the click
            // (BuildNav) was left on screen over the next page.
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
