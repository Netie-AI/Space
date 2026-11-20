using System.Windows.Media;
using NetieSpace.Helpers;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// Whether an icon font is installed. When neither is, tiles and the page title draw the category line icon
/// instead of empty boxes, and the log says so once.
/// </summary>
internal static class IconFont
{
    private static readonly Lazy<bool> Probe = new(() =>
    {
        try
        {
            var names = Fonts.SystemFontFamilies.Select(f => f.Source).ToList();
            var fluent = names.Any(n => string.Equals(n, "Segoe Fluent Icons", StringComparison.OrdinalIgnoreCase));
            var mdl2 = names.Any(n => string.Equals(n, "Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase));
            AppLogger.Info($"[app] icon fonts: SegoeFluentIcons={fluent} SegoeMDL2Assets={mdl2}");
            if (!fluent && !mdl2)
                AppLogger.Warn("[app] no Segoe icon font installed; tiles draw the category line icon");
            return fluent || mdl2;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[app] icon font probe failed: {ex.Message}");
            return false;
        }
    });

    public static bool Available => Probe.Value;
}
