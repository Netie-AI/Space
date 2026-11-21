using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfImage = System.Windows.Controls.Image;

namespace NetieSpace.Apps.Shell.ImagePanels;

/// <summary>
/// One thumbnail per dropped photo, the one being previewed outlined, and on each its run state:
/// a tick when done, a mark when failed (the reason is the tooltip and the line under the strip), dots while it runs.
/// </summary>
internal sealed class ThumbStrip : StackPanel
{
    public const int Size = 72;

    private readonly ScrollViewer _scroll;
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _status;
    private readonly List<Item> _items = new();
    private CancellationTokenSource? _load;

    private sealed class Item
    {
        public required string Path;
        public required Border Frame;
        public required WpfImage Picture;
        public required Border Badge;
        public required TextBlock BadgeText;
        public QueueState? State;
        public string? Message;
    }

    public ThumbStrip(TextBlock status)
    {
        _status = status;
        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _row,
        };
        Children.Add(_scroll);
        Children.Add(_status);
    }

    public event Action<int>? Picked;

    public int Count => _items.Count;

    public void SetFiles(IReadOnlyList<string> files, bool sync)
    {
        _load?.Cancel();
        _row.Children.Clear();
        _items.Clear();
        for (var i = 0; i < files.Count; i++)
        {
            var index = i;
            var item = MakeItem(files[i]);
            item.Frame.MouseLeftButtonUp += (_, _) => Picked?.Invoke(index);
            _items.Add(item);
            _row.Children.Add(item.Frame);
        }
        ShowSummary();
        LoadPictures(files, sync);
    }

    private Item MakeItem(string path)
    {
        var pic = new WpfImage { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(pic, BitmapScalingMode.HighQuality);
        var text = new TextBlock
        {
            FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Segoe UI"),
        };
        var badge = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Child = text,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 3, 3), Visibility = Visibility.Collapsed, IsHitTestVisible = false,
        };
        var grid = new Grid { Width = Size, Height = Size };
        grid.Children.Add(new Border { Background = (Brush)FindResource("ShellSidebar") });
        grid.Children.Add(pic);
        grid.Children.Add(badge);
        var frame = new Border
        {
            Width = Size + 4, Height = Size + 4, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(1),
            BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent,
            CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, ClipToBounds = true,
            Child = grid, ToolTip = System.IO.Path.GetFileName(path),
        };
        System.Windows.Automation.AutomationProperties.SetName(frame, System.IO.Path.GetFileName(path));
        return new Item { Path = path, Frame = frame, Picture = pic, Badge = badge, BadgeText = text };
    }

    private void LoadPictures(IReadOnlyList<string> files, bool sync)
    {
        var cts = _load = new CancellationTokenSource();
        if (sync)
        {
            for (var i = 0; i < files.Count; i++)
                SetPicture(i, Thumbnails.Picture(files[i], 160)?.Image);
            return;
        }
        var dispatcher = Dispatcher;
        _ = Task.Run(() =>
        {
            for (var i = 0; i < files.Count && !cts.IsCancellationRequested; i++)
            {
                var index = i;
                var pic = Thumbnails.Picture(files[i], 160)?.Image;
                dispatcher.InvokeAsync(() => { if (!cts.IsCancellationRequested) SetPicture(index, pic); });
            }
        });
    }

    private void SetPicture(int index, BitmapSource? image)
    {
        if (index >= 0 && index < _items.Count)
            _items[index].Picture.Source = image;
    }

    public void Select(int index)
    {
        var accent = (Brush)FindResource("ShellAccent");
        for (var i = 0; i < _items.Count; i++)
            _items[i].Frame.BorderBrush = i == index ? accent : Brushes.Transparent;
        if (index >= 0 && index < _items.Count)
            _items[index].Frame.BringIntoView();
    }

    /// <summary>Forgets every run state (a new file list, or a run starting over).</summary>
    public void ClearStates()
    {
        foreach (var item in _items)
            Apply(item, null, null);
        ShowSummary();
    }

    public void ShowQueue(RunQueue queue)
    {
        foreach (var q in queue.Items)
        {
            var item = _items.FirstOrDefault(i => string.Equals(i.Path, q.Path, StringComparison.OrdinalIgnoreCase));
            if (item is not null)
                Apply(item, q.State, q.State == QueueState.Failed ? q.Message : null);
        }
        ShowSummary();
    }

    private void Apply(Item item, QueueState? state, string? message)
    {
        item.State = state;
        item.Message = message;
        var name = System.IO.Path.GetFileName(item.Path);
        switch (state)
        {
            case QueueState.Done:
                Badge(item, "OK", (Brush)FindResource("ShellAccentStrong"));
                item.Frame.ToolTip = name + ": done";
                break;
            case QueueState.Failed:
                Badge(item, "!", (Brush)FindResource("ShellDanger"));
                item.Frame.ToolTip = name + ": " + message;
                break;
            case QueueState.Running:
                Badge(item, "...", (Brush)FindResource("ShellMuted"));
                item.Frame.ToolTip = name + ": running";
                break;
            case QueueState.Cancelled:
                Badge(item, "-", (Brush)FindResource("ShellMuted"));
                item.Frame.ToolTip = name + ": cancelled";
                break;
            default:
                item.Badge.Visibility = Visibility.Collapsed;
                item.Frame.ToolTip = name;
                break;
        }
    }

    private static void Badge(Item item, string text, Brush fill)
    {
        item.BadgeText.Text = text;
        item.Badge.Background = fill;
        item.Badge.Width = text.Length > 1 ? 28 : 20;
        item.Badge.Visibility = Visibility.Visible;
    }

    /// <summary>"5 photos: 3 done, 1 failed" and one line per failure with its reason, so a failure is read, not guessed.</summary>
    private void ShowSummary()
    {
        if (_items.Count == 0)
        {
            _status.Text = "";
            return;
        }
        var done = _items.Count(i => i.State == QueueState.Done);
        var failed = _items.Where(i => i.State == QueueState.Failed).ToList();
        var running = _items.Count(i => i.State == QueueState.Running);
        var head = $"{_items.Count} photo{(_items.Count == 1 ? "" : "s")}";
        if (done + failed.Count + running == 0)
        {
            _status.Text = _items.Count > 1 ? head + ". One Run applies to all of them." : head;
            _status.Foreground = (Brush)FindResource("ShellMuted");
            return;
        }
        var parts = new List<string>();
        if (done > 0) parts.Add(done + " done");
        if (failed.Count > 0) parts.Add(failed.Count + " failed");
        if (running > 0) parts.Add(running + " running");
        var lines = new List<string> { head + ": " + string.Join(", ", parts) };
        foreach (var f in failed.Take(3))
            lines.Add(System.IO.Path.GetFileName(f.Path) + ": " + f.Message);
        if (failed.Count > 3)
            lines.Add("and " + (failed.Count - 3) + " more failed");
        _status.Text = string.Join(Environment.NewLine, lines);
        _status.Foreground = (Brush)FindResource(failed.Count > 0 ? "ShellDanger" : "ShellMuted");
    }
}
