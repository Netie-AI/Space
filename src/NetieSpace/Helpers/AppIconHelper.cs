using System.Drawing;
using System.Windows;
using System.Windows.Media.Imaging;

namespace NetieSpace.Helpers;

/// <summary>
/// Loads netie.ico from disk beside the executable.
/// Never uses pack:// — single-file publish breaks embedded content URIs.
/// </summary>
public static class AppIconHelper
{
    public static string IconPath => IoPath.Combine(AppContext.BaseDirectory, "Assets", "netie.ico");

    public static bool ExistsOnDisk() => IoFile.Exists(IconPath);

    public static Icon LoadTrayIcon()
    {
        try
        {
            if (ExistsOnDisk())
                return new Icon(IconPath);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Tray icon disk load failed ({IconPath}): {ex.Message}");
        }

        return CreateFallbackTrayIcon();
    }

    public static void ApplyWindowIcon(Window window)
    {
        try
        {
            if (!ExistsOnDisk())
            {
                AppLogger.Warn($"Window icon missing at {IconPath}");
                return;
            }

            window.Icon = BitmapFrame.Create(new Uri(IconPath, UriKind.Absolute));
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Window icon load failed ({IconPath}): {ex.Message}");
        }
    }

    private static Icon CreateFallbackTrayIcon()
    {
        using var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(10, 12, 16));
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Color.FromArgb(94, 196, 184));
        graphics.FillEllipse(brush, 2, 2, 12, 12);
        var handle = bitmap.GetHicon();
        using var temp = Icon.FromHandle(handle);
        return (Icon)temp.Clone();
    }
}
