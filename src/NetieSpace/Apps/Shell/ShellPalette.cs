namespace NetieSpace.Apps.Shell;

/// <summary>
/// The neutral tokens of the shell chrome (paper, ink, lines, hover) for one Windows theme, as #RRGGBB.
/// The WPF side installs them in Application.Resources under the <c>Shell*</c> keys, so every view that reads them
/// with DynamicResource repaints when the PC switches between light and dark. Accents stay per app (AppPalette).
/// </summary>
public sealed record ShellPalette(
    string Name, string Paper, string Sidebar, string Surface, string Ink, string Muted, string Line,
    string Hover, string Press, string Danger, string Thumb, string ThumbHover, string Focus)
{
    public static readonly ShellPalette Light = new(
        "light", Paper: "#FFFFFF", Sidebar: "#F4F5F7", Surface: "#FFFFFF", Ink: "#2C2C2C", Muted: "#6E6E6E",
        Line: "#D6D6D6", Hover: "#EEEEEE", Press: "#E2E2E2", Danger: "#B3261E", Thumb: "#C4C8CE",
        ThumbHover: "#9EA4AD", Focus: "#1A56DB");

    public static readonly ShellPalette Dark = new(
        "dark", Paper: "#1B1D21", Sidebar: "#16181C", Surface: "#23262B", Ink: "#ECEEF1", Muted: "#A3A9B3",
        Line: "#3A3F47", Hover: "#2D3138", Press: "#383D45", Danger: "#FF8A80", Thumb: "#4B515B",
        ThumbHover: "#6A717D", Focus: "#8AB4FF");

    /// <summary>The XAML resource key and colour of every token, in one place so the installer cannot miss one.</summary>
    public IReadOnlyList<(string Key, string Hex)> Tokens => new[]
    {
        ("ShellPaper", Paper), ("ShellSidebar", Sidebar), ("ShellSurface", Surface), ("ShellInk", Ink),
        ("ShellMuted", Muted), ("ShellLine", Line), ("ShellHover", Hover), ("ShellPress", Press),
        ("ShellDanger", Danger), ("ShellThumb", Thumb), ("ShellThumbHover", ThumbHover), ("ShellFocus", Focus),
    };

    /// <summary>Windows stores "apps use light theme" as 1 or 0. A missing value (old Windows) means light.</summary>
    public static ShellPalette ForAppsUseLightTheme(int? registryValue) => registryValue == 0 ? Dark : Light;

    /// <summary>"dark" or "light" (any case) forces a palette; anything else is null, meaning follow Windows.</summary>
    public static ShellPalette? Forced(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "dark" => Dark,
        "light" => Light,
        _ => null,
    };
}
