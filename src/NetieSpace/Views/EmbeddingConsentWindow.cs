using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NetieSpace.Helpers;
using NetieSpace.Services;

namespace NetieSpace.Views;

/// <summary>First-run consent for proprietary local dual-brain embeddings.</summary>
public sealed class EmbeddingConsentWindow : Window
{
    public bool Accepted { get; private set; }

    public EmbeddingConsentWindow()
    {
        Title = "NETIE SPACE — Local memory";
        Width = 460;
        Height = 380;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowInTaskbar = false;

        Brush textBrush = TryBrush("TextBrush", Brushes.WhiteSmoke);
        Brush mutedBrush = TryBrush("MutedBrush", new SolidColorBrush(Color.FromRgb(0xA0, 0xA8, 0xB8)));
        Brush borderBrush = TryBrush("GlassBorder", new SolidColorBrush(Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF)));

        var shell = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            BorderBrush = borderBrush,
            Margin = new Thickness(8),
            Padding = new Thickness(22, 18, 22, 18),
            Background = new LinearGradientBrush(
                Color.FromArgb(0xC0, 0x18, 0x1C, 0x28),
                Color.FromArgb(0xA0, 0x10, 0x14, 0x20),
                45)
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "Local dual-brain memory",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = textBrush,
            Margin = new Thickness(0, 0, 0, 10)
        });
        stack.Children.Add(new TextBlock
        {
            Text =
                "Netie can keep a private, compressed memory of files you preview — " +
                "embeddings and a file graph — only on this PC under a hidden proprietary folder.\n\n" +
                "• Never uploaded or shared with other apps\n" +
                "• Used to make summaries smarter and use less cloud tokens\n" +
                "• You can wipe or revoke anytime (tray → Local memory)\n\n" +
                "Allow Netie to store this local memory for your benefit?",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 20,
            Foreground = mutedBrush,
            Margin = new Thickness(0, 0, 0, 18)
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var decline = new Button
        {
            Content = "Not now",
            Margin = new Thickness(0, 0, 10, 0),
            Padding = new Thickness(14, 8, 14, 8),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Foreground = textBrush,
            BorderThickness = new Thickness(0)
        };
        decline.Click += (_, _) =>
        {
            Accepted = false;
            UserPrefs.EmbeddingConsentAccepted = false;
            UserPrefs.EmbeddingConsentPrompted = true;
            try { DialogResult = false; } catch { /* ignore */ }
            Close();
        };
        var allow = new Button
        {
            Content = "Allow local memory",
            Padding = new Thickness(16, 8, 16, 8),
            Background = new SolidColorBrush(Color.FromRgb(0x5E, 0xC4, 0xB8)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x0E, 0x12, 0x18)),
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold
        };
        allow.Click += (_, _) =>
        {
            Accepted = true;
            UserPrefs.EmbeddingConsentAccepted = true;
            UserPrefs.EmbeddingConsentPrompted = true;
            LocalEmbeddingBrain.Instance.EnsureLayout();
            try { DialogResult = true; } catch { /* ignore */ }
            Close();
        };
        row.Children.Add(decline);
        row.Children.Add(allow);
        stack.Children.Add(row);
        shell.Child = stack;
        Content = shell;
    }

    private static Brush TryBrush(string key, Brush fallback)
    {
        try
        {
            if (Application.Current?.TryFindResource(key) is Brush b)
                return b;
        }
        catch { /* ignore */ }
        return fallback;
    }

    public static bool EnsurePrompted()
    {
        if (UserPrefs.EmbeddingConsentAccepted)
            return true;
        if (UserPrefs.EmbeddingConsentPrompted)
            return false;

        var dlg = new EmbeddingConsentWindow();
        dlg.ShowDialog();
        return dlg.Accepted;
    }
}
