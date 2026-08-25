using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NetieSpace.Helpers;
using NetieSpace.Services;

namespace NetieSpace.Views;

/// <summary>Reorder packed V1 clips before export. Top row plays first.</summary>
public partial class VideoOrderWindow : Window
{
    private readonly ObservableCollection<ClipOrderRow> _rows = new();
    private Point _dragStart;
    private ClipOrderRow? _dragItem;

    public IReadOnlyList<int> OrderedIndices => _rows.Select(r => r.OriginalIndex).ToList();

    public VideoOrderWindow(IReadOnlyList<KeptClip> clips)
    {
        InitializeComponent();
        GlassHelper.ApplyRoundedDialog(this, 0.92, 16);
        ClipList.ItemsSource = _rows;
        for (var i = 0; i < clips.Count; i++)
        {
            var c = clips[i];
            var dur = c.End - c.Start;
            var name = string.IsNullOrWhiteSpace(c.SourcePath)
                ? $"Clip {i + 1}"
                : IoPath.GetFileName(c.SourcePath);
            _rows.Add(new ClipOrderRow
            {
                OriginalIndex = i,
                Title = name,
                Duration = Format(dur)
            });
        }

        RefreshLabels();
        if (_rows.Count > 0)
            ClipList.SelectedIndex = 0;
    }

    public static IReadOnlyList<int>? Show(Window owner, IReadOnlyList<KeptClip> clips)
    {
        if (clips.Count == 0)
            return null;
        var win = new VideoOrderWindow(clips) { Owner = owner };
        return win.ShowDialog() == true ? win.OrderedIndices.ToList() : null;
    }

    private void RefreshLabels()
    {
        for (var i = 0; i < _rows.Count; i++)
            _rows[i].NotifyOrder(i + 1);
        StatusText.Text = $"{_rows.Count} clip{(_rows.Count == 1 ? "" : "s")} — top = first on V1";
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        AnimationHelper.AnimateOpen(RootBorder, 240);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0)
        {
            StatusText.Text = "No clips to order.";
            return;
        }

        DialogResult = true;
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        var i = ClipList.SelectedIndex;
        if (i <= 0) return;
        _rows.Move(i, i - 1);
        RefreshLabels();
        ClipList.SelectedIndex = i - 1;
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        var i = ClipList.SelectedIndex;
        if (i < 0 || i >= _rows.Count - 1) return;
        _rows.Move(i, i + 1);
        RefreshLabels();
        ClipList.SelectedIndex = i + 1;
    }

    private void ClipList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null)
            return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumHorizontalDragDistance)
            return;
        DragDrop.DoDragDrop(ClipList, _dragItem, DragDropEffects.Move);
    }

    private void ClipList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem = (e.OriginalSource as FrameworkElement)?.DataContext as ClipOrderRow
                    ?? ClipList.SelectedItem as ClipOrderRow;
    }

    private void ClipList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ClipOrderRow))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void ClipList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(ClipOrderRow)))
            return;
        if (e.Data.GetData(typeof(ClipOrderRow)) is not ClipOrderRow src)
            return;
        var target = (e.OriginalSource as FrameworkElement)?.DataContext as ClipOrderRow;
        var from = _rows.IndexOf(src);
        var to = target is null ? _rows.Count - 1 : _rows.IndexOf(target);
        if (from < 0 || to < 0 || from == to)
            return;
        _rows.Move(from, to);
        RefreshLabels();
        ClipList.SelectedIndex = to;
    }

    private static string Format(TimeSpan t) =>
        $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
}

public sealed class ClipOrderRow : INotifyPropertyChanged
{
    public int OriginalIndex { get; init; }
    public string Title { get; init; } = "";
    public string Duration { get; init; } = "";
    public string Badge { get; private set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyOrder(int n)
    {
        Badge = $"{n}.";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Badge)));
    }
}
