using System.Windows;
using System.Windows.Controls;
using NetieSpace.Helpers;

namespace NetieSpace.Views;

public partial class GlassChoiceDialog : Window
{
    public string? SelectedId { get; private set; }
    public string? ExtraText { get; private set; }

    private GlassChoiceDialog(
        string title,
        string body,
        IReadOnlyList<(string Id, string Label, bool Primary)> choices,
        string? extraPlaceholder)
    {
        InitializeComponent();
        TitleText.Text = title;
        BodyText.Text = body;

        TextBox? extraBox = null;
        if (!string.IsNullOrWhiteSpace(extraPlaceholder))
        {
            ExtraPanel.Visibility = Visibility.Visible;
            ExtraPanel.Children.Add(new TextBlock
            {
                Text = "Optional title / on-screen text",
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 0, 6)
            });
            extraBox = new TextBox
            {
                Text = "",
                Tag = "extra",
                Padding = new Thickness(10, 8, 10, 8),
                FontSize = 13.5,
                ToolTip = extraPlaceholder
            };
            ExtraPanel.Children.Add(extraBox);
            ExtraPanel.Children.Add(new TextBlock
            {
                Text = extraPlaceholder,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                FontSize = 11,
                Opacity = 0.75,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }

        foreach (var (id, label, primary) in choices)
        {
            var btn = new Button
            {
                Content = label,
                Tag = id,
                Margin = new Thickness(8, 6, 0, 0),
                Padding = new Thickness(primary ? 16 : 14, 9, primary ? 16 : 14, 9),
                Style = (Style)FindResource(primary ? "EditorChipPrimary" : "EditorChip")
            };
            btn.Click += (_, _) =>
            {
                SelectedId = id;
                ExtraText = extraBox?.Text?.Trim();
                DialogResult = true;
            };
            ButtonPanel.Children.Add(btn);
        }

        GlassHelper.ApplyRoundedDialog(this, 0.92, 16);
    }

    public static string? Show(Window owner, string title, string body,
        IReadOnlyList<(string Id, string Label, bool Primary)> choices)
    {
        var dlg = new GlassChoiceDialog(title, body, choices, null) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.SelectedId : null;
    }

    public static (string? Id, string? ExtraText) ShowWithText(
        Window owner, string title, string body,
        IReadOnlyList<(string Id, string Label, bool Primary)> choices,
        string extraPlaceholder)
    {
        var dlg = new GlassChoiceDialog(title, body, choices, extraPlaceholder) { Owner = owner };
        return dlg.ShowDialog() == true ? (dlg.SelectedId, dlg.ExtraText) : (null, null);
    }
}
