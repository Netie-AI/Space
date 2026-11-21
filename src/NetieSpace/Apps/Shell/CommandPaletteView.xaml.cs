using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// Ctrl+K / Ctrl+P: type 2 or 3 letters, arrow keys move, Enter runs, Esc closes. Thin: ranking, wrapping and the rows
/// come from <see cref="CommandPalette"/>.
/// </summary>
public partial class CommandPaletteView : UserControl
{
    /// <summary>What a row shows: the palette item plus the word for its kind.</summary>
    public sealed record Row(PaletteItem Item)
    {
        public string Title => Item.Title;
        public string Subtitle => Item.Subtitle;
        public bool Enabled => Item.Enabled;
        public string KindText => Item.Kind switch
        {
            PaletteKind.Tool => "Tool",
            PaletteKind.RecentFile => "Recent file",
            _ => "Command",
        };
    }

    private IReadOnlyList<PaletteItem> _all = Array.Empty<PaletteItem>();
    private IInputElement? _before;

    public CommandPaletteView()
    {
        InitializeComponent();
        Query.TextChanged += (_, _) => Refresh();
        Query.PreviewKeyDown += OnQueryKey;
        Scrim.MouseLeftButtonDown += (_, _) => Close();
        Rows.PreviewMouseLeftButtonUp += OnRowClick;
    }

    public event Action<PaletteItem>? Picked;

    public bool IsOpen => Visibility == Visibility.Visible;

    public void Open(IReadOnlyList<PaletteItem> items)
    {
        _all = items;
        _before = Keyboard.FocusedElement;
        Query.Text = "";
        Visibility = Visibility.Visible;
        Refresh();
        Query.Focus();
        Keyboard.Focus(Query);
    }

    public void SetQuery(string text) => Query.Text = text;

    public void Close()
    {
        if (!IsOpen)
            return;
        Visibility = Visibility.Collapsed;
        // Back to where the person was typing or tabbing.
        if (_before is UIElement e && e.IsVisible)
            Keyboard.Focus(e);
    }

    private void Refresh()
    {
        var hits = CommandPalette.Search(_all, Query.Text);
        Rows.ItemsSource = hits.Select(h => new Row(h)).ToList();
        Rows.SelectedIndex = hits.Count > 0 ? 0 : -1;
        Empty.Visibility = hits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Rows.Visibility = hits.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnQueryKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
            case Key.Up:
                Rows.SelectedIndex = CommandPalette.Move(Rows.SelectedIndex, e.Key == Key.Down ? 1 : -1, Rows.Items.Count);
                if (Rows.SelectedItem is not null)
                    Rows.ScrollIntoView(Rows.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                Pick(Rows.SelectedItem as Row);
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        var item = (e.OriginalSource as DependencyObject)?.FindAncestor<ListBoxItem>();
        if (item?.DataContext is Row row)
            Pick(row);
    }

    private void Pick(Row? row)
    {
        if (row is null)
            return;
        Close();
        Picked?.Invoke(row.Item);
    }
}

internal static class VisualTreeExtensions
{
    public static T? FindAncestor<T>(this DependencyObject start) where T : DependencyObject
    {
        for (var d = start; d is not null; d = System.Windows.Media.VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
            if (d is T t)
                return t;
        return null;
    }
}
