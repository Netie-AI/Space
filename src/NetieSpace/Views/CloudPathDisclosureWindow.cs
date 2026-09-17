using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NetieSpace.Helpers;

namespace NetieSpace.Views;

/// <summary>SEC-01 cheap path: acknowledge cloud AI before document text leaves this PC.</summary>
public sealed class CloudPathDisclosureWindow : Window
{
    public bool Accepted { get; private set; }

    public CloudPathDisclosureWindow()
    {
        Title = "NETIE SPACE — Cloud AI";
        Width = 480;
        Height = 420;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Owner is null)
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
            Text = "Cloud AI leaves this PC",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = textBrush,
            Margin = new Thickness(0, 0, 0, 10)
        });
        stack.Children.Add(new TextBlock
        {
            Text =
                "Chat, summary, and word lookup send text from the open file to the " +
                "cloud provider shown in the toolbar (Groq, Gemini, OpenRouter, or SEA-LION).\n\n" +
                "• Local Autofill / My details stay on this PC (DPAPI)\n" +
                "• Image OCR stays local unless you set NETIE_SPACE_OCR_CLOUD=1\n" +
                "• Keys still live in user.env until the vault path ships\n\n" +
                "Continue with cloud AI for this PC?",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 20,
            Foreground = mutedBrush,
            Margin = new Thickness(0, 0, 0, 18)
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var decline = new Button
        {
            Content = "Keep local only",
            Margin = new Thickness(0, 0, 10, 0),
            Padding = new Thickness(14, 8, 14, 8),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Foreground = textBrush,
            BorderThickness = new Thickness(0)
        };
        decline.Click += (_, _) =>
        {
            Accepted = false;
            UserPrefs.CloudPathAcknowledged = false;
            UserPrefs.CloudPathPrompted = true;
            try { DialogResult = false; } catch { /* ignore */ }
            Close();
        };
        var allow = new Button
        {
            Content = "Allow cloud AI",
            Padding = new Thickness(14, 8, 14, 8),
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xB0)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        allow.Click += (_, _) =>
        {
            Accepted = true;
            UserPrefs.CloudPathAcknowledged = true;
            UserPrefs.CloudPathPrompted = true;
            try { DialogResult = true; } catch { /* ignore */ }
            Close();
        };
        row.Children.Add(decline);
        row.Children.Add(allow);
        stack.Children.Add(row);
        shell.Child = stack;
        Content = shell;
        AppIconHelper.ApplyWindowIcon(this);
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

    /// <summary>True if the user already allowed cloud, or just allowed this prompt.</summary>
    public static bool EnsureAcked()
    {
        if (UserPrefs.CloudPathAcknowledged)
            return true;
        if (UserPrefs.CloudPathPrompted)
            return false;

        var dlg = new CloudPathDisclosureWindow();
        dlg.ShowDialog();
        return dlg.Accepted;
    }
}
