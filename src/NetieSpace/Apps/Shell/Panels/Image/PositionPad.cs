using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NetieSpace.Apps.Shell.ImagePanels;

/// <summary>A 3 x 3 pad laid over the preview: click a cell to put the mark there. Values are the page's own grid choices.</summary>
internal sealed class PositionPad : Grid
{
    private readonly Dictionary<string, (Border Cell, Ellipse Dot)> _cells = new(StringComparer.Ordinal);

    public PositionPad()
    {
        for (var i = 0; i < 3; i++)
        {
            RowDefinitions.Add(new RowDefinition());
            ColumnDefinitions.Add(new ColumnDefinition());
        }
        var line = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
        line.Freeze();
        for (var i = 0; i < ToolPageModel.GridChoices.Count; i++)
        {
            var value = ToolPageModel.GridChoices[i];
            var dot = new Ellipse
            {
                Width = 12, Height = 12, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed, IsHitTestVisible = false,
            };
            var cell = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = line,
                BorderThickness = new Thickness(0.5),
                Cursor = Cursors.Hand,
                Child = dot,
                ToolTip = OptionLabels.For("position", value),
            };
            System.Windows.Automation.AutomationProperties.SetName(cell, "Put the mark at " + OptionLabels.For("position", value));
            cell.MouseEnter += (_, _) => { if (!ReferenceEquals(Selected, value)) cell.Background = Hover; };
            cell.MouseLeave += (_, _) => Paint();
            cell.MouseLeftButtonUp += (_, e) => { Picked?.Invoke(value); e.Handled = true; };
            SetRow(cell, i / 3);
            SetColumn(cell, i % 3);
            Children.Add(cell);
            _cells[value] = (cell, dot);
        }
    }

    private static readonly Brush Hover = Freeze(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)));

    private static Brush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    public string? Selected { get; private set; }

    public event Action<string>? Picked;

    public void Select(string? value)
    {
        Selected = value;
        Paint();
    }

    private void Paint()
    {
        var tint = TryFindResource("ShellAccent") is SolidColorBrush a
            ? new SolidColorBrush(Color.FromArgb(110, a.Color.R, a.Color.G, a.Color.B))
            : new SolidColorBrush(Color.FromArgb(110, 94, 196, 184));
        tint.Freeze();
        foreach (var (value, (cell, dot)) in _cells)
        {
            var on = string.Equals(value, Selected, StringComparison.Ordinal);
            cell.Background = on ? tint : Brushes.Transparent;
            dot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
