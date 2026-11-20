using System.Runtime.InteropServices;
using NetieSpace.Helpers;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// Start Menu shortcuts for Netie PDF / Image / Video, written through COM because
/// WScript.Shell cannot carry System.AppUserModel.ID: IShellLinkW for target, arguments and
/// icon, IPropertyStore on the same link for the AUMID, then IPersistFile.Save.
/// HKCU / Start Menu only. Never touches a UserChoice key and never writes HKLM.
/// </summary>
public static class AppShortcuts
{
    public const int ExitOk = 0;
    public const int ExitFailed = 1;

    /// <summary>
    /// Entry used by AppEntry for --register-apps / --unregister-apps. Prints each path
    /// written or removed; exit 0, or exit 1 naming the failure.
    /// </summary>
    public static int Run(RegistrationSwitch which)
    {
        try { AttachConsole(0xFFFFFFFF); } catch { /* no parent console */ }
        try
        {
            var programs = AppRegistration.ProgramsDirectory(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
            if (which == RegistrationSwitch.Register)
            {
                var exe = Environment.ProcessPath
                    ?? throw new InvalidOperationException("the running exe path is unknown");
                Register(exe, programs, Console.WriteLine);
            }
            else
            {
                Unregister(programs, Console.WriteLine);
            }
            return ExitOk;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "App registration");
            Console.Error.WriteLine($"REGISTER_APPS_FAILED {ex.GetType().Name}: {ex.Message}");
            return ExitFailed;
        }
    }

    // Own declaration: Windows-shape tests link this folder without Helpers/Win32Api.cs.
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint dwProcessId);

    /// <summary>Writes the three shortcuts into <paramref name="programsDirectory"/>.</summary>
    public static void Register(string exePath, string programsDirectory, Action<string> log)
    {
        if (!IoFile.Exists(exePath))
            throw new System.IO.FileNotFoundException($"exe not found: {exePath}");
        var exeDirectory = IoPath.GetDirectoryName(exePath)!;

        // Every icon is checked before the first shortcut is written: a missing one must not leave the first
        // shortcuts behind on a run that then exits 1.
        foreach (var identity in AppIdentity.All)
        {
            var icon = AppRegistration.IconPath(exeDirectory, identity);
            if (!IoFile.Exists(icon))
                throw new System.IO.FileNotFoundException(
                    $"icon not found for {AppRegistration.ForIdentity(identity).DisplayName}: {icon}");
        }

        IoDirectory.CreateDirectory(programsDirectory);
        foreach (var identity in AppIdentity.All)
        {
            var spec = AppRegistration.ForIdentity(identity);
            var icon = AppRegistration.IconPath(exeDirectory, identity);
            var path = AppRegistration.ShortcutPath(programsDirectory, spec);
            WriteShortcut(path, exePath, exeDirectory, spec, icon);
            log($"wrote {path}");
        }
    }

    /// <summary>Deletes exactly the three shortcuts. Anything else in the folder stays.</summary>
    public static void Unregister(string programsDirectory, Action<string> log)
    {
        foreach (var spec in AppRegistration.Specs)
        {
            var path = AppRegistration.ShortcutPath(programsDirectory, spec);
            if (IoFile.Exists(path))
            {
                IoFile.Delete(path);
                log($"removed {path}");
            }
            else
            {
                log($"absent {path}");
            }
        }
    }

    private static void WriteShortcut(string lnkPath, string exePath, string exeDirectory, ShortcutSpec spec, string iconPath)
    {
        var link = (IShellLinkW)new ShellLinkCoClass();
        try
        {
            link.SetPath(exePath);
            link.SetArguments(spec.Arguments);
            link.SetWorkingDirectory(exeDirectory);
            link.SetIconLocation(iconPath, 0);
            link.SetDescription(spec.DisplayName);

            // The property store must be committed before IPersistFile.Save or the AUMID is lost.
            var store = (IPropertyStore)link;
            ShellProps.SetString(store, AppRegistration.AppUserModelIdPid, spec.Aumid);
            store.Commit();

            var file = (IPersistFile)link;
            file.Save(lnkPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }
}

/// <summary>Property-store helpers shared by the shortcut writer and the window relaunch code.</summary>
internal static class ShellProps
{
    public static void SetString(IPropertyStore store, uint pid, string value)
    {
        var key = new PropertyKey(AppRegistration.AppUserModelFormatId, pid);
        // InitPropVariantFromString is an inline helper in the Windows headers, not an export of
        // propsys.dll, so the VT_LPWSTR variant is built by hand. PropVariantClear frees the string.
        var variant = new PropVariant { Vt = VtLpwstr, Data1 = Marshal.StringToCoTaskMemUni(value) };
        try
        {
            store.SetValue(ref key, ref variant);
        }
        finally
        {
            PropVariantClear(ref variant);
        }
    }

    private const ushort VtLpwstr = 31;

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [DllImport("shell32.dll", PreserveSig = false)]
    internal static extern void SHGetPropertyStoreForWindow(
        IntPtr hwnd,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;

    public PropertyKey(Guid formatId, uint propertyId)
    {
        FormatId = formatId;
        PropertyId = propertyId;
    }
}

/// <summary>PROPVARIANT: 16 bytes on x86, 24 on x64. Only the string form is used here.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort Vt;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public IntPtr Data1;
    public IntPtr Data2;
}

[ComImport, Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLinkCoClass
{
}

[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
    void Resolve(IntPtr hwnd, uint fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[ComImport, Guid("0000010B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistFile
{
    void GetClassID(out Guid pClassID);
    [PreserveSig] int IsDirty();
    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
    void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
    void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint cProps);
    void GetAt(uint iProp, out PropertyKey pkey);
    void GetValue(ref PropertyKey key, out PropVariant pv);
    void SetValue(ref PropertyKey key, ref PropVariant pv);
    void Commit();
}
