namespace NetieSpace.Apps.Shell;

/// <summary>
/// One row per Netie app that runs from NetieSpace.exe as its own process.
/// The AUMID strings and the --app keys are written into Start Menu shortcuts and
/// user pins (S50-E1-c), so they are frozen once shipped. Pure data: no WPF, no Win32.
/// </summary>
public sealed record AppIdentity(string Key, string Aumid, string DisplayName)
{
    public static readonly AppIdentity Pdf = new("pdf", "Netie.Pdf", "Netie PDF");
    public static readonly AppIdentity Image = new("image", "Netie.Image", "Netie Image");
    public static readonly AppIdentity Video = new("video", "Netie.Video", "Netie Video");
    public static readonly AppIdentity Md = new("md", "Netie.Md", "Netie MD");

    /// <summary>The three tool apps: tool grid, family switcher and Peek hand-off. Netie MD is not one.</summary>
    public static IReadOnlyList<AppIdentity> All { get; } = new[] { Pdf, Image, Video };

    /// <summary>Every app process: the tool apps and the Markdown viewer. Find, registration and icons use this.</summary>
    public static IReadOnlyList<AppIdentity> Everything { get; } = new[] { Pdf, Image, Video, Md };

    public bool IsMarkdown => string.Equals(Key, Md.Key, StringComparison.Ordinal);

    public static AppIdentity? Find(string? key) =>
        Everything.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>"Pdf" / "Image" / "Video" / "Md": the segment used in kernel object names.</summary>
    public string ObjectName => Aumid["Netie.".Length..];

    public string InstanceMutexName => $"Local\\Netie.{ObjectName}.Instance.v1";

    public string ActivateEventName => $"Local\\Netie.{ObjectName}.Activate.v1";

    /// <summary>Icon beside the exe. Loaded from disk, never pack:// (single-file publish).</summary>
    public string IconPath => IoPath.Combine(AppContext.BaseDirectory, "Assets", "apps", $"netie-{Key}.ico");

    public string PendingOpenPath =>
        IoPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetieSpace", "apps", Key, "pending-open.txt");
}
