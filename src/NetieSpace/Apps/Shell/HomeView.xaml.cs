using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NetieSpace.Apps.Shell;

/// <summary>Recent files and the tool grid. Thin: every decision comes from <see cref="HomeModel"/>.</summary>
public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        ClearPendingButton.Click += (_, _) => PendingCleared?.Invoke();
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
        IReadOnlyList<HomeCategory> categories,
        string? category,
        IReadOnlyList<RecentEntry> recent,
        string? pending)
    {
        HomeTitle.Text = category ?? title;
        LoadingText.Visibility = Visibility.Collapsed;
        SetPending(pending);

        RecentSection.Visibility = category is null ? Visibility.Visible : Visibility.Collapsed;
        RecentRows.Children.Clear();
        RecentEmpty.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var r in recent)
            RecentRows.Children.Add(RecentRow(r));

        ToolsHost.Children.Clear();
        foreach (var cat in categories.Where(c => category is null || c.Name == category))
        {
            var header = new TextBlock { Text = cat.Name, Margin = new Thickness(0, 20, 0, 10) };
            header.Style = (Style)FindResource("ShellSection");
            ToolsHost.Children.Add(header);

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var tile in cat.Tiles)
                wrap.Children.Add(TileButton(tile));
            ToolsHost.Children.Add(wrap);
        }
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

    private Button TileButton(HomeTile tile)
    {
        var badge = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(6),
            Background = (Brush)FindResource(tile.Available ? "ShellAccentTint" : "ShellHover"),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = tile.Glyph,
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        text.Children.Add(new TextBlock { Text = tile.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock
        {
            Text = tile.Verb,
            Style = (Style)FindResource("ShellMutedText"),
            Margin = new Thickness(0, 2, 0, 0),
        });
        if (tile.ReasonLine is not null)
        {
            text.Children.Add(new TextBlock
            {
                Text = tile.ReasonLine,
                Style = (Style)FindResource("ShellMutedText"),
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 6, 0, 0),
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
            Opacity = tile.Available ? 1.0 : 0.6,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        var id = tile.ToolId;
        button.Click += (_, _) => ToolPicked?.Invoke(id);
        return button;
    }
}
