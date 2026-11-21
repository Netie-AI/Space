namespace NetieSpace.Apps.Shell;

/// <summary>
/// One accent per app: PDF red, Image teal, Video violet, MD slate. Colours as #RRGGBB; the WPF side
/// turns them into the Shell* accent brushes. <see cref="Ink"/> is the text colour on <see cref="Accent"/>,
/// <see cref="Strong"/> the colour for icons and links on <see cref="Tint"/> or white.
/// </summary>
public sealed record AppPalette(string Key, string Accent, string Hover, string Press, string Tint, string Strong, string Ink)
{
    public static readonly AppPalette Pdf = new("pdf", "#D7322B", "#BF2A24", "#A5231E", "#FCE9E8", "#B8261F", "#FFFFFF");
    public static readonly AppPalette Image = new("image", "#0C7A71", "#09685F", "#07564F", "#E0F4F2", "#09685F", "#FFFFFF");
    public static readonly AppPalette Video = new("video", "#6D4AE0", "#5B3BC9", "#4C2FAD", "#EFEAFD", "#5B3BC9", "#FFFFFF");
    public static readonly AppPalette Md = new("md", "#475569", "#3A4657", "#2E3846", "#EBEEF2", "#3A4657", "#FFFFFF");

    public static IReadOnlyList<AppPalette> All { get; } = new[] { Pdf, Image, Video, Md };

    /// <summary>The palette of an app key; the Image teal (the family default) for anything else.</summary>
    public static AppPalette For(string? key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) ?? Image;
}
