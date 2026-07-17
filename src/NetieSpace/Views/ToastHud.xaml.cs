using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using NetieSpace.Helpers;

namespace NetieSpace.Views;

public partial class ToastHud : Window
{
    private readonly string? _openPath;

    public ToastHud(string title, string body, string? openPath = null)
    {
        InitializeComponent();
        TitleText.Text = title;
        BodyText.Text = body;
        _openPath = openPath;
        GlassHelper.ApplyRoundedDialog(this, 0.92, 14);

        Left = SystemParameters.WorkArea.Width - Width - 24;
        Top = SystemParameters.WorkArea.Height - Height - 24;
    }

    public static void ShowExportComplete(string outputPath)
    {
        var name = System.IO.Path.GetFileName(outputPath);
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var hud = new ToastHud("Video exported", name, outputPath);
            hud.Show();
        });
    }

    public static void Show(string title, string? openFolderOrFile = null)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var hud = new ToastHud(title, openFolderOrFile is null ? "" : System.IO.Path.GetFileName(openFolderOrFile), openFolderOrFile);
            hud.Show();
        });
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AnimationHelper.AnimateOpen(Root, 260);
        await Task.Delay(6000);
        await AnimationHelper.AnimateCloseAsync(Root, 200);
        Close();
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_openPath))
            return;

        try
        {
            if (System.IO.File.Exists(_openPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _openPath,
                    UseShellExecute = true
                });
            }
            else
            {
                var dir = System.IO.Path.GetDirectoryName(_openPath);
                if (!string.IsNullOrWhiteSpace(dir) && System.IO.Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true
                    });
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Toast open failed: {ex.Message}");
        }

        Close();
    }
}
