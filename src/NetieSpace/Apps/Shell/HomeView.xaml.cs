using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NetieSpace.Helpers;

namespace NetieSpace.Apps.Shell;

/// <summary>Recent files, recent tools and the tool grid. Thin: every decision comes from <see cref="HomeModel"/>.</summary>
public partial class HomeView : UserControl
{
    private const int RecentFileRows = 8;
    private CancellationTokenSource? _thumbsCts;

    public HomeView()
    {
        InitializeComponent();
        ClearPendingButton.Click += (_, _) => PendingCleared?.Invoke();
        SearchBox.TextChanged += (_, _) =>
        {
            SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            SearchChanged?.Invoke(SearchBox.Text);
        };
        OpenFileButton.Click += (_, _) => OpenFileRequested?.Invoke();
        ClearSearchButton.Click += (_, _) =>
        {
            SearchBox.Text = "";
            SearchBox.Focus();
        };
        // Dropping a file is the shell's job (the whole window takes it), not this view's.
    }

    public event Action<string>? SearchChanged;
    public event Action? OpenFileRequested;
    public event Action<string>? ToolPicked;
    public event Action<string>? RecentPicked;
    public event Action? PendingCleared;

    public string Query => SearchBox.Text;

    public void ShowLoading(string title)
    {
        HomeTitle.Text = title;
        LoadingText.Visibility = Visibility.Visible;
        RecentSection.Visibility = Visibility.Collapsed;
        RecentToolsSection.Visibility = Visibility.Collapsed;
        StartCard.Visibility = Visibility.Collapsed;
        ToolsHost.Children.Clear();
        SuggestedHost.Children.Clear();
        _tiles.Clear();
    }

    public void SetPending(string? path)
    {
        PendingBar.Visibility = path is null ? Visibility.Collapsed : Visibility.Visible;
        PendingText.Text = path ?? "";
        PendingText.ToolTip = path;
    }

    /// <param name="category">Null shows every category, the recent tools and the recent files.</param>
    public void Bind(
        string title,
        HomeGrid grid,
        string? category,
        IReadOnlyList<RecentEntry> recent,
        string? pending,
        IReadOnlyList<HomeTile> recentTools)
    {
        var searching = ToolSearch.Tokens(SearchBox.Text).Length > 0;
        var overview = category is null && !searching;
        HomeTitle.Text = category is not null && !searching ? category : title;
        LoadingText.Visibility = Visibility.Collapsed;
        SetPending(pending);

        // Recent files and tools are for the front page only; a search or a category is about the grid.
        var firstRun = overview && recent.Count == 0 && recentTools.Count == 0 && pending is null;
        StartCard.Visibility = firstRun ? Visibility.Visible : Visibility.Collapsed;

        RecentToolsSection.Visibility = overview && !firstRun ? Visibility.Visible : Visibility.Collapsed;
        RecentToolRows.Children.Clear();
        foreach (var t in recentTools)
            RecentToolRows.Children.Add(TileButton(t));
        RecentToolsEmpty.Visibility = recentTools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        RecentSection.Visibility = overview && !firstRun ? Visibility.Visible : Visibility.Collapsed;
        RecentRows.Children.Clear();
        _thumbsCts?.Cancel();
        var cts = _thumbsCts = new CancellationTokenSource();
        var now = DateTime.Now;
        if (recent.Count == 0)
        {
            RecentRows.Children.Add(new TextBlock
            {
                Text = "Files you open show up here. Drop one anywhere in this window, or press Ctrl+O.",
                Style = (Style)FindResource("ShellMutedText"),
            });
        }
        foreach (var r in recent.Take(RecentFileRows))
            RecentRows.Children.Add(RecentRow(r, now, cts.Token));

        ToolsHost.Children.Clear();
        SuggestedHost.Children.Clear();
        _tiles.Clear();
        var emptyLine = grid.EmptyLine is null
            ? null
            : searching ? $"No tool matches \"{SearchBox.Text.Trim()}\". Try fewer letters." : grid.EmptyLine;
        NoToolsText.Text = emptyLine ?? "";
        NoToolsPanel.Visibility = emptyLine is null ? Visibility.Collapsed : Visibility.Visible;
        ClearSearchButton.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;

        if (grid.OpenWithTitle is not null)
            AddGroup(SuggestedHost, grid.OpenWithTitle, grid.OpenWith);
        foreach (var cat in grid.Categories)
            AddGroup(ToolsHost, cat.Name, cat.Tiles);
    }

    private void AddGroup(Panel host, string name, IReadOnlyList<HomeTile> tiles)
    {
        var header = new TextBlock { Text = name, Margin = new Thickness(0, 20, 0, 10) };
        header.Style = (Style)FindResource("ShellSection");
        host.Children.Add(header);

        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var tile in tiles)
            wrap.Children.Add(TileButton(tile));
        host.Children.Add(wrap);
    }

    private Button RecentRow(RecentEntry r, DateTime now, CancellationToken ct)
    {
        var when = RelativeTime.Format(r.OpenedAtUtc.ToLocalTime(), now);

        var thumbImage = new System.Windows.Controls.Image { Stretch = Stretch.Uniform, Margin = new Thickness(2) };
        RenderOptions.SetBitmapScalingMode(thumbImage, BitmapScalingMode.HighQuality);
        var thumbBadge = new TextBlock
        {
            Text = PreviewPlan.TypeBadge(r.Path),
            Style = (Style)FindResource("ShellCaption"),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var thumb = new Border
        {
            Width = 40,
            Height = 52,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("ShellLine"),
            Background = (Brush)FindResource("ShellSurface"),
            Child = new Grid { Children = { thumbBadge, thumbImage } },
            VerticalAlignment = VerticalAlignment.Center,
        };
        _ = LoadThumbAsync(r.Path, thumbImage, thumbBadge, ct);

        var text = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = r.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock
        {
            Text = r.Folder,
            Style = (Style)FindResource("ShellMutedText"),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
        });
        var whenText = new TextBlock
        {
            Text = when,
            Style = (Style)FindResource("ShellMutedText"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(thumb, Dock.Left);
        DockPanel.SetDock(whenText, Dock.Right);
        dock.Children.Add(thumb);
        dock.Children.Add(whenText);
        dock.Children.Add(text);

        var button = new Button
        {
            Content = dock,
            Style = (Style)FindResource("ShellNavItem"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 6, 12, 6),
            ToolTip = r.Path,
        };
        AutomationProperties.SetName(button, $"{r.Name}, {when}");
        var path = r.Path;
        button.Click += (_, _) => RecentPicked?.Invoke(path);
        return button;
    }

    /// <summary>The first page of a PDF or the picture itself; any other type keeps its type badge. Never throws.</summary>
    private static async Task LoadThumbAsync(string path, System.Windows.Controls.Image target, TextBlock badge, CancellationToken ct)
    {
        try
        {
            ImageSource? source = null;
            switch (PreviewPlan.KindOf(path))
            {
                case PreviewKind.Pdf:
                    source = (await Thumbnails.PdfPagesAsync(path, 1, 120, ct))?.Pages.FirstOrDefault();
                    break;
                case PreviewKind.Image:
                    source = await Task.Run(() => Thumbnails.Picture(path, 120)?.Image, ct);
                    break;
            }
            if (ct.IsCancellationRequested || source is null)
                return;
            target.Source = source;
            badge.Visibility = Visibility.Collapsed;
        }
        catch (Exception)
        {
            // The badge stays: a file that cannot be drawn is still a row.
        }
    }

    /// <summary>A stroked line icon from the category table; the box fallback if a string ever fails to parse.</summary>
    private Path GlyphPath(HomeTile tile)
    {
        Geometry geometry;
        try
        {
            geometry = Geometry.Parse(tile.PathData);
        }
        catch (FormatException)
        {
            geometry = Geometry.Parse(TileGlyphs.Fallback);
        }
        return new Path
        {
            Data = geometry,
            Stroke = (Brush)FindResource("ShellAccentStrong"),
            StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.Uniform,
            Width = 20,
            Height = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>The tool's own icon-font glyph; the category line drawing when the id has none or no icon font is installed.</summary>
    private UIElement TileIcon(HomeTile tile)
    {
        if (tile.IconGlyph is { } glyph && IconFont.Available)
        {
            var icon = new TextBlock { Text = glyph, Style = (Style)FindResource("ShellIcon") };
            if (!tile.Available)
                icon.SetResourceReference(TextBlock.ForegroundProperty, "ShellMuted");
            return icon;
        }
        return GlyphPath(tile);
    }

    private readonly List<Button> _tiles = new();

    /// <summary>
    /// Closes any tooltip still showing on a tile and drops it, so a hint from the grid cannot stay on screen
    /// over the tool page after the click that left the grid.
    /// </summary>
    public void CloseTooltips()
    {
        foreach (var b in _tiles)
        {
            if (b.ToolTip is System.Windows.Controls.ToolTip tip)
                tip.IsOpen = false;
            b.ToolTip = null;
        }
    }

    private Button TileButton(HomeTile tile)
    {
        var badge = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(10),
            VerticalAlignment = VerticalAlignment.Center,
            Child = TileIcon(tile),
        };
        badge.SetResourceReference(Border.BackgroundProperty, tile.Available ? "ShellAccentTint" : "ShellHover");

        // The name says what the tool does; a second line appears only to say why it cannot run.
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = tile.Name,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (tile.ReasonLine is not null)
        {
            text.Children.Add(new TextBlock
            {
                Text = FriendlyError.Short(tile.ReasonLine),
                Style = (Style)FindResource("ShellMutedText"),
                FontSize = 11.5,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 3, 0, 0),
            });
        }

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(badge, 0);
        Grid.SetColumn(text, 1);
        row.Children.Add(badge);
        row.Children.Add(text);

        var button = new Button
        {
            Content = row,
            Style = (Style)FindResource("ShellTile"),
            Opacity = tile.Available ? 1.0 : 0.62,
            VerticalContentAlignment = VerticalAlignment.Center,
            // The name is already on the tile: a hint appears only to say why it cannot run.
            ToolTip = tile.ReasonLine is null ? null : new System.Windows.Controls.ToolTip { Content = FriendlyError.ForUnavailable(tile.ReasonLine, tile.ToolId, null).Sentence },
        };
        // The tile's text is a TextBlock inside a Grid, so the button has no name of its own unless it is given one.
        // The name stays the tool's name: scripts find a tile by it.
        AutomationProperties.SetName(button, tile.Name);
        if (!tile.Available)
            AutomationProperties.SetHelpText(button, FriendlyError.ForUnavailable(tile.ReasonLine ?? "", tile.ToolId, null).Sentence);
        _tiles.Add(button);
        var id = tile.ToolId;
        button.Click += (_, _) => ToolPicked?.Invoke(id);
        return button;
    }
}
