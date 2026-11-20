using System.Windows;
using System.Windows.Media;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// Installs one app's accent brushes in Application.Resources. The theme reads them with DynamicResource,
/// so a window already on screen repaints when the palette changes (the snapshot renders several apps in turn).
/// </summary>
public static class AppAccents
{
    public static void Apply(AppIdentity identity) => Apply(AppPalette.For(identity.Key));

    public static void Apply(AppPalette p)
    {
        var res = Application.Current?.Resources;
        if (res is null)
            return;
        Set(res, "ShellAccent", p.Accent);
        Set(res, "ShellAccentHover", p.Hover);
        Set(res, "ShellAccentPress", p.Press);
        Set(res, "ShellAccentTint", p.Tint);
        Set(res, "ShellAccentStrong", p.Strong);
        Set(res, "ShellAccentInk", p.Ink);
    }

    private static void Set(ResourceDictionary res, string key, string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        res[key] = brush;
    }
}
