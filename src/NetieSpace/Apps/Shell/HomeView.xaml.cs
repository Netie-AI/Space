using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NetieSpace.Apps.Shell;

/// <summary>Recent files and the tool grid. Thin: every decision comes from <see cref="HomeModel"/>.</summary>
public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        ClearPendingButton.Click += (_, _) => PendingCleared?.Invoke();
        SearchBox.TextChanged += (_, _) =>
        {
            SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            SearchChanged?.Invoke(SearchBox.Text);
        };
        AllowDrop = true;
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    public event Action<string>? SearchChanged;
    public event Action<string>? FileDropped;

    public string Query => SearchBox.Text;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
        {
            var first = dropped.FirstOrDefault(f => IoFile.Exists(f));
            if (first is not null)
                FileDropped?.Invoke(first);
        }
        e.Handled = true;
    }

    public event Action<string>? ToolPicked;
    public event Action<string>? RecentPicked;
    public event Action? PendingCleared;

    public void ShowLoading(string title)
    {
        HomeTitle.Text = title;
        LoadingText.Visibility = Visibility.Visible;
        RecentSection.Visibility = Visibility.Collapsed;
        ToolsHost.Children.Clear();
    }

    public void SetPending(string? path)
    {
        PendingBar.Visibility = path is null ? Visibility.Collapsed : Visibility.Visible;
        PendingText.Text = path ?? "";
        PendingText.ToolTip = path;
    }

    /// <param name="category">Null shows every category and the recent files.</param>
    public void Bind(
        string title,
        HomeGrid grid,
        string? category,
        IReadOnlyList<RecentEntry> recent,
        string? pending)
    {
        var searching = ToolSearch.Tokens(SearchBox.Text).Length > 0;
        HomeTitle.Text = category is not null && !searching ? category : title;
        LoadingText.Visibility = Visibility.Collapsed;
        SetPending(pending);

        // No heading over nothing: the section appears with the first recent file.
        RecentSection.Visibility = category is null && !searching && recent.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
        RecentRows.Children.Clear();
        foreach (var r in recent)
            RecentRows.Children.Add(RecentRow(r));

        ToolsHost.Children.Clear();
        NoToolsText.Text = grid.EmptyLine ?? "";
        NoToolsText.Visibility = grid.EmptyLine is null ? Visibility.Collapsed : Visibility.Visible;

        if (grid.OpenWithTitle is not null)
            AddGroup(grid.OpenWithTitle, grid.OpenWith);
        foreach (var cat in grid.Categories)
            AddGroup(cat.Name, cat.Tiles);
    }

    private void AddGroup(string name, IReadOnlyList<HomeTile> tiles)
    {
        var header = new TextBlock { Text = name, Margin = new Thickness(0, 20, 0, 10) };
        header.Style = (Style)FindResource("ShellSection");
        ToolsHost.Children.Add(header);

        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var tile in tiles)
            wrap.Children.Add(TileButton(tile));
        ToolsHost.Children.Add(wrap);
    }

    private Button RecentRow(RecentEntry r)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock { Text = r.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var folder = new TextBlock
        {
            Text = r.Folder,
            Style = (Style)FindResource("ShellMutedText"),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 16, 0),
        };
        var when = new TextBlock
        {
            Text = r.OpenedAtUtc.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.InvariantCulture),
            Style = (Style)FindResource("ShellMutedText"),
        };
        Grid.SetColumn(name, 0);
        Grid.SetColumn(folder, 1);
        Grid.SetColumn(when, 2);
        grid.Children.Add(name);
        grid.Children.Add(folder);
        grid.Children.Add(when);

        var button = new Button
        {
            Content = grid,
            Style = (Style)FindResource("ShellNavItem"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ToolTip = r.Path,
        };
        var path = r.Path;
        button.Click += (_, _) => RecentPicked?.Invoke(path);
        return button;
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
            Stroke = (Brush)FindResource("ShellInk"),
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

    private Button TileButton(HomeTile tile)
    {
        var badge = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = (Brush)FindResource(tile.Available ? "ShellAccentTint" : "ShellHover"),
            VerticalAlignment = VerticalAlignment.Center,
            Child = GlyphPath(tile),
        };

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
                Text = tile.ReasonLine,
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
            ToolTip = tile.ReasonLine is null ? tile.Name : tile.Name + ": " + tile.ReasonLine,
        };
        var id = tile.ToolId;
        button.Click += (_, _) => ToolPicked?.Invoke(id);
        return button;
    }
}
