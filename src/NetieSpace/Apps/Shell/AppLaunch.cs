namespace NetieSpace.Apps.Shell;

public enum AppLaunchKind
{
    /// <summary>Not ours: the Space tray path handles these args as before.</summary>
    Space,
    /// <summary>--app pdf|image|video|md, optional file.</summary>
    App,
    /// <summary>--tool ... / --list-tools: headless tool CLI (S50-E2-a).</summary>
    ToolCli,
    /// <summary>--app with a missing or unknown value.</summary>
    Invalid,
}

/// <summary>
/// Pure argv classification, decided before the Space single-instance mutex exists.
/// Parses only what S50-E1-a owns; the full args travel on so later tickets
/// (--snapshot, --view, --register-apps) can extend the app path.
/// </summary>
public sealed record AppLaunch(AppLaunchKind Kind, AppIdentity? Identity = null, string? OpenPath = null, string? Error = null)
{
    public static AppLaunch Parse(string[] args)
    {
        var appIndex = IndexOf(args, "--app");
        if (appIndex < 0)
        {
            return IndexOf(args, "--tool") >= 0 || IndexOf(args, "--list-tools") >= 0
                ? new AppLaunch(AppLaunchKind.ToolCli)
                : new AppLaunch(AppLaunchKind.Space);
        }

        var value = appIndex + 1 < args.Length ? args[appIndex + 1] : null;
        var identity = AppIdentity.Find(value);
        if (identity is null)
        {
            var shown = string.IsNullOrWhiteSpace(value) ? "(none)" : value;
            return new AppLaunch(
                AppLaunchKind.Invalid,
                Error: $"Unknown --app value: {shown}. Expected pdf, image, video or md.");
        }

        return new AppLaunch(AppLaunchKind.App, identity, ExtractOpenPath(args, appIndex));
    }

    /// <summary>--open FILE wins; otherwise a bare existing path right after the app name.</summary>
    private static string? ExtractOpenPath(string[] args, int appIndex)
    {
        var openIndex = IndexOf(args, "--open");
        if (openIndex >= 0)
            return openIndex + 1 < args.Length ? Clean(args[openIndex + 1]) : null;

        var next = appIndex + 2;
        if (next >= args.Length)
            return null;
        var candidate = Clean(args[next]);
        if (candidate.StartsWith("--", StringComparison.Ordinal))
            return null;
        return IoFile.Exists(candidate) || IoDirectory.Exists(candidate) ? candidate : null;
    }

    private static string Clean(string value) => value.Trim().Trim('"');

    private static int IndexOf(string[] args, string flag) =>
        Array.FindIndex(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
}
