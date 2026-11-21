using System.Windows;
using System.Windows.Media;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// Installs one app's accent brushes in Application.Resources. The theme reads them with DynamicResource,
/// so a window already on screen repaints when the palette changes (the snapshot renders several apps in turn).
/// </summary>
public static class AppAccents
{
    private static AppPalette? _current;

    public static void Apply(AppIdentity identity) => Apply(AppPalette.For(identity.Key));

    /// <summary>Installs the neutral tokens of the current Windows theme, then this app's accent as it is drawn on them.</summary>
    public static void Apply(AppPalette palette)
    {
        _current = palette;
        ShellTheme.Install();
        var res = Application.Current?.Resources;
        if (res is null)
            return;
        var p = ShellTheme.IsDark ? palette.ForDark() : palette;
        Set(res, "ShellAccent", p.Accent);
        Set(res, "ShellAccentHover", p.Hover);
        Set(res, "ShellAccentPress", p.Press);
        Set(res, "ShellAccentTint", p.Tint);
        Set(res, "ShellAccentStrong", p.Strong);
        Set(res, "ShellAccentInk", p.Ink);
    }

    /// <summary>Windows switched theme while the app runs: install both sets again.</summary>
    public static void Reapply()
    {
        if (_current is not null)
            Apply(_current);
    }

    private static void Set(ResourceDictionary res, string key, string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        res[key] = brush;
    }
}
