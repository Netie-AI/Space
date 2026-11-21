using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// Puts the light or dark neutral tokens (<see cref="ShellPalette"/>) in Application.Resources. The theme reads them with
/// DynamicResource, so a window that is already open repaints when this PC switches between light and dark.
/// The choice is, in order: <see cref="Force"/> (the --theme flag of a snapshot), the NETIE_THEME variable, then Windows.
/// </summary>
public static class ShellTheme
{
    private static ShellPalette? _forced;
    private static bool _watching;

    public static ShellPalette Current { get; private set; } = ShellPalette.Light;

    public static bool IsDark => Current.Name == ShellPalette.Dark.Name;

    /// <summary>Raised on the UI thread after the palette changed while the app runs.</summary>
    public static event Action? Changed;

    public static void Force(string? text) => _forced = ShellPalette.Forced(text);

    public static ShellPalette Resolve() =>
        _forced
        ?? ShellPalette.Forced(Environment.GetEnvironmentVariable("NETIE_THEME"))
        ?? FromWindows();

    /// <summary>Installs the palette the PC asks for now. Safe to call again.</summary>
    public static void Install()
    {
        Current = Resolve();
        var res = Application.Current?.Resources;
        if (res is null)
            return;
        foreach (var (key, hex) in Current.Tokens)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            res[key] = brush;
        }
    }

    /// <summary>Follows Windows while the app runs. A forced palette never changes.</summary>
    public static void Watch()
    {
        if (_watching)
            return;
        _watching = true;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.VisualStyle)
                return;
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                var before = Current.Name;
                AppAccents.Reapply();
                if (before != Current.Name)
                    Changed?.Invoke();
            }));
        };
    }

    private static ShellPalette FromWindows()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return ShellPalette.ForAppsUseLightTheme(key?.GetValue("AppsUseLightTheme") is int i ? i : null);
        }
        catch (Exception)
        {
            return ShellPalette.Light;
        }
    }
}
