namespace NetieSpace.Apps.Md;

/// <summary>Which colours the page and chrome use: follow Windows (default), or one fixed.</summary>
public enum MdColorMode
{
    System,
    Light,
    Dark,
}

public static class MdColorModes
{
    /// <summary>The toggle cycles System, Light, Dark.</summary>
    public static MdColorMode Next(MdColorMode mode) => mode switch
    {
        MdColorMode.System => MdColorMode.Light,
        MdColorMode.Light => MdColorMode.Dark,
        _ => MdColorMode.System,
    };

    public static string Label(MdColorMode mode) => mode switch
    {
        MdColorMode.Light => "Light",
        MdColorMode.Dark => "Dark",
        _ => "Auto",
    };

    /// <summary>The value of the page's data-theme attribute, or null to follow the system.</summary>
    public static string? Attribute(MdColorMode mode) => mode switch
    {
        MdColorMode.Light => "light",
        MdColorMode.Dark => "dark",
        _ => null,
    };

    public static bool TryParse(string? text, out MdColorMode mode)
    {
        switch ((text ?? "").Trim().ToLowerInvariant())
        {
            case "system" or "auto": mode = MdColorMode.System; return true;
            case "light": mode = MdColorMode.Light; return true;
            case "dark": mode = MdColorMode.Dark; return true;
            default: mode = MdColorMode.System; return false;
        }
    }

    /// <summary>What the chrome paints: a fixed mode as is, System from the Windows setting.</summary>
    public static bool IsDark(MdColorMode mode, bool systemIsDark) => mode == MdColorMode.Dark || (mode == MdColorMode.System && systemIsDark);
}
