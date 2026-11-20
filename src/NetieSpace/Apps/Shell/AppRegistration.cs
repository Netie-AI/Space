namespace NetieSpace.Apps.Shell;

public enum RegistrationSwitch
{
    None,
    Register,
    Unregister,
}

/// <summary>One Start Menu shortcut: everything a reader must find on the written .lnk.</summary>
public sealed record ShortcutSpec(string Key, string FileName, string Arguments, string Aumid, string DisplayName, string IconFileName);

/// <summary>
/// Pure data for Start Menu registration (S50-E1-c): shortcut names, arguments, AUMIDs, icon
/// paths and the pin relaunch command line. No Win32, no WPF, so the Linux suite pins it.
/// Registration is HKCU / Start Menu only and never touches a UserChoice key.
/// </summary>
public static class AppRegistration
{
    public const string RegisterFlag = "--register-apps";
    public const string UnregisterFlag = "--unregister-apps";

    /// <summary>System.AppUserModel.ID: {9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, pid 5.</summary>
    public static readonly Guid AppUserModelFormatId = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    public const uint AppUserModelIdPid = 5;
    public const uint RelaunchCommandPid = 2;
    public const uint RelaunchIconResourcePid = 3;
    public const uint RelaunchDisplayNameResourcePid = 4;

    public static IReadOnlyList<ShortcutSpec> Specs { get; } = AppIdentity.Everything.Select(ForIdentity).ToArray();

    public static ShortcutSpec ForIdentity(AppIdentity identity) => new(
        identity.Key,
        identity.DisplayName + ".lnk",
        "--app " + identity.Key,
        identity.Aumid,
        identity.DisplayName,
        IconFileName(identity));

    public static string IconFileName(AppIdentity identity) => $"netie-{identity.Key}.ico";

    /// <summary>"&lt;exe folder&gt;\Assets\apps\netie-&lt;app&gt;.ico", built with the platform separator.</summary>
    public static string IconPath(string exeDirectory, AppIdentity identity) =>
        IoPath.Combine(exeDirectory, "Assets", "apps", IconFileName(identity));

    /// <summary>Folder that holds the shortcuts: Start Menu\Programs under the roaming profile.</summary>
    public static string ProgramsDirectory(string startMenuFolder) => IoPath.Combine(startMenuFolder, "Programs");

    public static string ShortcutPath(string programsDirectory, ShortcutSpec spec) =>
        IoPath.Combine(programsDirectory, spec.FileName);

    /// <summary>The command a taskbar pin runs: the exe quoted, then --app and the key.</summary>
    public static string RelaunchCommand(string exePath, AppIdentity identity) =>
        "\"" + exePath + "\" --app " + identity.Key;

    /// <summary>
    /// Registration is the first argument only, so a file or value that merely spells the flag
    /// later on a command line (for example after --app pdf) never triggers a registration.
    /// Matching is exact and case-insensitive.
    /// </summary>
    public static RegistrationSwitch Parse(string[] args)
    {
        if (args.Length == 0)
            return RegistrationSwitch.None;
        if (string.Equals(args[0], RegisterFlag, StringComparison.OrdinalIgnoreCase))
            return RegistrationSwitch.Register;
        if (string.Equals(args[0], UnregisterFlag, StringComparison.OrdinalIgnoreCase))
            return RegistrationSwitch.Unregister;
        return RegistrationSwitch.None;
    }
}
