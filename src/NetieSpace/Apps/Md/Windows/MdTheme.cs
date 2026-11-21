using System.Windows;
using System.Windows.Media;

namespace NetieSpace.Apps.Md.WinRt;

/// <summary>The light chrome of the Netie app family, shared with the tool apps through AppTheme.xaml.</summary>
public static class MdTheme
{
    /// <summary>AppTheme.xaml from the running exe, or null where no application resources exist (tests).</summary>
    public static ResourceDictionary? Load()
    {
        try
        {
            return Application.LoadComponent(new Uri("/NetieSpace;component/Apps/Shell/AppTheme.xaml", UriKind.Relative)) as ResourceDictionary;
        }
        catch (Exception ex)
        {
            NetieSpace.Helpers.AppLogger.Warn($"[app:md] theme not loaded: {ex.Message}");
            return null;
        }
    }

    public static Brush Brush(FrameworkElement owner, string key, string fallbackHex)
    {
        if (owner.TryFindResource(key) is Brush found)
            return found;
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(fallbackHex)!;
        brush.Freeze();
        return brush;
    }
}
