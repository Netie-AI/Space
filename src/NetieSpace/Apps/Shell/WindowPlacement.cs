using System.Text.Json;

namespace NetieSpace.Apps.Shell;

/// <summary>Where a window was last: its normal (not maximised) rectangle in DIPs, and whether it was maximised.</summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized)
{
    /// <summary>A rectangle of the desktop (the virtual screen), in DIPs.</summary>
    public readonly record struct Area(double X, double Y, double Width, double Height);

    /// <summary>
    /// The placement to restore on a desktop that may have changed since (a monitor was unplugged, the resolution fell).
    /// The size is kept between the window's minimum and the desktop; the position is pulled back until at least a
    /// grab-able strip of the title bar is on screen. Null for a nonsense placement: the window then centres as usual.
    /// </summary>
    public static WindowPlacement? Fit(WindowPlacement? p, Area desktop, double minWidth, double minHeight)
    {
        if (p is null || !Finite(p.Left, p.Top, p.Width, p.Height) || p.Width < 100 || p.Height < 100
            || desktop.Width <= 0 || desktop.Height <= 0)
            return null;

        var w = Math.Clamp(p.Width, Math.Min(minWidth, desktop.Width), desktop.Width);
        var h = Math.Clamp(p.Height, Math.Min(minHeight, desktop.Height), desktop.Height);
        const double strip = 120;
        var left = Math.Clamp(p.Left, desktop.X - w + strip, desktop.X + desktop.Width - strip);
        var top = Math.Clamp(p.Top, desktop.Y, desktop.Y + desktop.Height - 40);
        return new WindowPlacement(left, top, w, h, p.Maximized);
    }

    private static bool Finite(params double[] v) => v.All(d => !double.IsNaN(d) && !double.IsInfinity(d));
}

/// <summary>
/// Per-app window memory (%LOCALAPPDATA%\NetieSpace\apps\&lt;app&gt;\window.json). Pure IO. A missing or damaged file
/// reads as null (first run: centre the window); a failed save is silent, the window just opens centred next time.
/// </summary>
public sealed class WindowPlacementStore
{
    private readonly string _jsonPath;

    public WindowPlacementStore(string jsonPath) => _jsonPath = jsonPath;

    public string JsonPath => _jsonPath;

    public static string DefaultPath(AppIdentity app) =>
        IoPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetieSpace", "apps", app.Key, "window.json");

    public WindowPlacement? Load()
    {
        try
        {
            return IoFile.Exists(_jsonPath)
                ? JsonSerializer.Deserialize<WindowPlacement>(IoFile.ReadAllText(_jsonPath))
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Save(WindowPlacement placement)
    {
        try
        {
            var dir = IoPath.GetDirectoryName(_jsonPath);
            if (!string.IsNullOrEmpty(dir))
                IoDirectory.CreateDirectory(dir);
            var tmp = _jsonPath + ".tmp";
            IoFile.WriteAllText(tmp, JsonSerializer.Serialize(placement));
            IoFile.Move(tmp, _jsonPath, overwrite: true);
        }
        catch (Exception)
        {
            // Convenience only.
        }
    }
}
