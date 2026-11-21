using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using NetieSpace.Apps.Tools;
using NetieSpace.Helpers;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// Sidebar (categories, family switcher) and the content host (home / tool page) of one app window.
/// Also the root the snapshot renders, so it needs no Window around it. Owns the window-wide behaviour: the drop target,
/// the keyboard map, the quick-search palette and the help sheet.
/// </summary>
public partial class AppShell : UserControl
{
    private AppIdentity? _identity;
    private RecentFiles? _recent;
    private RecentTools? _recentTools;
    private IReadOnlyList<RegisteredTool> _tools = Array.Empty<RegisteredTool>();
    private IReadOnlyList<HomeCategory> _categories = Array.Empty<HomeCategory>();
    private ToolStateStore? _state;
    private string? _activeCategoryForNav;
    private string? _homeCategory;

    public AppShell()
    {
        InitializeComponent();
        Home.ToolPicked += ShowTool;
        Home.RecentPicked += OpenFile;
        Home.PendingCleared += () => SelectFile(null);
        Home.SearchChanged += _ => RefreshHome();
        Home.OpenFileRequested += ChooseFile;
        ToolPage.BackRequested += () => ShowHome(null);
        ToolPage.ToolRequested += ShowTool;
        ToolPage.ToolsRequested += () => ShowHome(null);

        PaletteButton.Click += (_, _) => OpenPalette();
        Palette.Picked += OnPalettePicked;
        HelpCloseButton.Click += (_, _) => CloseHelp();
        HelpScrim.MouseLeftButtonDown += (_, _) => CloseHelp();
        BuildHelp();

        // Tunnelling, so a text box under the pointer cannot swallow the drag: a file dropped anywhere lands here.
        PreviewDragEnter += OnDragOver;
        PreviewDragOver += OnDragOver;
        PreviewDragLeave += OnDragLeave;
        PreviewDrop += OnDrop;
    }

    public string? SelectedFile { get; private set; }

    public void Initialize(AppIdentity identity, RecentFiles recent, string? openPath, ToolStateStore? state = null, RecentTools? recentTools = null)
    {
        _identity = identity;
        AppAccents.Apply(identity);
        _recent = recent;
        _recentTools = recentTools;
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

    /// <summary>A file in hand: remember it, then show the tools that suit it.</summary>
    public void OpenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !IoFile.Exists(path))
            return;
        _recent?.Add(path);
        SelectFile(path);
        ShowHome(null);
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
        var byId = _categories.SelectMany(c => c.Tiles).GroupBy(t => t.ToolId).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var recentTools = (_recentTools?.Load(byId.Keys.ToHashSet(StringComparer.Ordinal)) ?? Array.Empty<RecentTool>())
            .Select(r => byId[r.ToolId])
            .Take(6)
            .ToList();
        Home.Bind(_identity!.DisplayName, grid, _homeCategory, _recent!.Load(), SelectedFile, recentTools);
    }

    public void ShowTool(string toolId)
    {
        var tool = _tools.FirstOrDefault(t => t.Entry.Id == toolId);
        if (tool is null)
            return;
        _activeCategoryForNav = tool.Entry.Category;
        Home.CloseTooltips();
        BuildNav();
        _recentTools?.Add(toolId);
        ToolPage.Load(tool, _recent!, SelectedFile, _identity!, _state!);
        if (!tool.Availability.Available)
        {
            var all = _categories.SelectMany(c => c.Tiles).ToList();
            var me = HomeModel.Tile(tool);
            ToolPage.ShowUnavailable(FriendlyError.ForUnavailable(tool.Availability.Text, toolId, FriendlyError.AlternativeFor(me, all)));
        }
        Home.Visibility = Visibility.Collapsed;
        ToolPage.Visibility = Visibility.Visible;
    }

    // ---- keyboard -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The shell-wide keys. The window forwards every key here from its own tunnelling handler: with nothing focused yet
    /// (a fresh window) a key never reaches a child, so a handler on this control alone would miss Ctrl+K.
    /// </summary>
    public void HandleKey(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            if (Palette.IsOpen) { Palette.Close(); e.Handled = true; }
            else if (HelpOverlay.Visibility == Visibility.Visible) { CloseHelp(); e.Handled = true; }
            return;
        }

        var m = Keyboard.Modifiers;
        switch (ShellKeys.Resolve(key.ToString(), m.HasFlag(ModifierKeys.Control), m.HasFlag(ModifierKeys.Alt), m.HasFlag(ModifierKeys.Shift)))
        {
            case ShellCommand.Palette:
                if (Palette.IsOpen) Palette.Close(); else OpenPalette();
                e.Handled = true;
                break;
            case ShellCommand.OpenFile:
                ChooseFile();
                e.Handled = true;
                break;
            case ShellCommand.Back:
                GoBack();
                e.Handled = true;
                break;
            case ShellCommand.Help:
                if (HelpOverlay.Visibility == Visibility.Visible) CloseHelp(); else ShowHelp();
                e.Handled = true;
                break;
        }
    }

    private void GoBack()
    {
        if (Palette.IsOpen) { Palette.Close(); return; }
        if (HelpOverlay.Visibility == Visibility.Visible) { CloseHelp(); return; }
        if (ToolPage.Visibility == Visibility.Visible || _homeCategory is not null)
            ShowHome(null);
    }

    private void ChooseFile()
    {
        var dlg = new OpenFileDialog { Filter = "All files|*.*", Multiselect = false, Title = "Open a file" };
        if (dlg.ShowDialog() == true)
            OpenFile(dlg.FileName);
    }

    // ---- palette and help -----------------------------------------------------------------------------------------

    public void OpenPalette()
    {
        if (_identity is null)
            return;
        var tiles = _categories.SelectMany(c => c.Tiles).ToList();
        var known = tiles.Select(t => t.ToolId).ToHashSet(StringComparer.Ordinal);
        var items = CommandPalette.Build(
            tiles,
            _recent?.Load() ?? Array.Empty<RecentEntry>(),
            (_recentTools?.Load(known) ?? Array.Empty<RecentTool>()).Select(r => r.ToolId),
            SelectedFile);
        Palette.Open(items);
    }

    /// <summary>Opens the palette with a query already typed (the snapshot uses this).</summary>
    public void OpenPalette(string query)
    {
        OpenPalette();
        Palette.SetQuery(query);
    }

    private void OnPalettePicked(PaletteItem item)
    {
        switch (item.Kind)
        {
            case PaletteKind.Tool:
                ShowTool(item.Key);
                break;
            case PaletteKind.RecentFile:
                OpenFile(item.Key);
                break;
            default:
                switch (item.Key)
                {
                    case PaletteItem.CommandOpenFile: ChooseFile(); break;
                    case PaletteItem.CommandAllTools: ShowHome(null); break;
                    case PaletteItem.CommandHelp: ShowHelp(); break;
                }
                break;
        }
    }

    private void BuildHelp()
    {
        HelpRows.Children.Clear();
        foreach (var (chord, what) in ShellKeys.HelpLines)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var c = new TextBlock { Text = chord, FontWeight = FontWeights.SemiBold };
            var w = new TextBlock { Text = what, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(w, 1);
            row.Children.Add(c);
            row.Children.Add(w);
            AutomationProperties.SetName(row, chord + ": " + what);
            HelpRows.Children.Add(row);
        }
    }

    public void ShowHelp()
    {
        HelpOverlay.Visibility = Visibility.Visible;
        HelpCloseButton.Focus();
    }

    private void CloseHelp()
    {
        HelpOverlay.Visibility = Visibility.Collapsed;
    }

    // ---- drop anywhere --------------------------------------------------------------------------------------------

    /// <summary>The overlay as it looks while a file is held over the window (the snapshot draws it; a drag cannot be scripted).</summary>
    public void ShowDropPreview()
    {
        DropTitle.Text = _identity is null ? "Drop a file" : "Drop a file to open it in " + _identity.DisplayName;
        DropOverlay.Visibility = Visibility.Visible;
    }

    private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!HasFiles(e))
            return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        if (DropOverlay.Visibility != Visibility.Visible)
        {
            DropTitle.Text = _identity is null ? "Drop a file" : "Drop a file to open it in " + _identity.DisplayName;
            DropOverlay.Visibility = Visibility.Visible;
        }
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        // Leaving a child fires this too; only a pointer that left the window ends the drag.
        var p = e.GetPosition(this);
        if (p.X <= 0 || p.Y <= 0 || p.X >= ActualWidth - 1 || p.Y >= ActualHeight - 1)
            DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] dropped)
            return;
        var files = dropped.Where(f => IoFile.Exists(f)).ToList();
        e.Handled = true;
        if (files.Count == 0)
            return;
        if (_identity is null || _categories.Count == 0)
            return;

        // A tool that is open and takes these files adds them; anything else shows what suits the file.
        if (ToolPage.Visibility == Visibility.Visible && ToolPage.TryAcceptDrop(files))
        {
            foreach (var f in files)
                _recent?.Add(f);
            return;
        }
        OpenFile(files[0]);
    }

    // ---- sidebar --------------------------------------------------------------------------------------------------

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
        AutomationProperties.SetName(b, label);
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
            AutomationProperties.SetName(b, target.Label);
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
