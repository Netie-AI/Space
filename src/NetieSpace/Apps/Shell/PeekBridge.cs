namespace NetieSpace.Apps.Shell;

/// <summary>
/// Peek hands a file to the app that works on it: Netie PDF, Netie Image or Netie Video,
/// each its own process. Pure: which app, and the exact argv, so both are testable.
/// </summary>
public static class PeekBridge
{
    /// <summary>The app for a preview kind name ("Pdf", "Image", "Video"); null for every other kind.</summary>
    public static AppIdentity? ForKind(string? kind) => kind switch
    {
        "Pdf" => AppIdentity.Pdf,
        "Image" => AppIdentity.Image,
        "Video" => AppIdentity.Video,
        _ => null,
    };

    public static string Label(AppIdentity app) => "Open in " + app.DisplayName;

    /// <summary>
    /// One argument per element, never a joined command line, so a path with spaces,
    /// quotes or an apostrophe arrives intact.
    /// </summary>
    public static string[] Arguments(AppIdentity app, string path) =>
        new[] { "--app", app.Key, "--open", path };
}
