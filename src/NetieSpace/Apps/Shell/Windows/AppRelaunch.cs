using System.Runtime.InteropServices;
using NetieSpace.Helpers;

namespace NetieSpace.Apps.Shell;

/// <summary>
/// Makes a taskbar pin of a Netie app relaunch that app. Explorer builds a pin from the window's
/// property store; without these properties it pins the bare exe and the pin opens Netie Space.
/// Apply once the HWND exists (SourceInitialized), before the window is shown.
/// </summary>
public static class AppRelaunch
{
    /// <summary>Returns true when the properties were written and committed. Never throws.</summary>
    public static bool Apply(IntPtr hwnd, AppIdentity identity)
    {
        IPropertyStore? store = null;
        try
        {
            var exe = Environment.ProcessPath;
            if (hwnd == IntPtr.Zero || string.IsNullOrEmpty(exe))
                return false;

            var iid = typeof(IPropertyStore).GUID;
            ShellProps.SHGetPropertyStoreForWindow(hwnd, ref iid, out store);

            var icon = AppRegistration.IconPath(IoPath.GetDirectoryName(exe)!, identity);
            ShellProps.SetString(store, AppRegistration.AppUserModelIdPid, identity.Aumid);
            ShellProps.SetString(store, AppRegistration.RelaunchCommandPid, AppRegistration.RelaunchCommand(exe, identity));
            ShellProps.SetString(store, AppRegistration.RelaunchDisplayNameResourcePid, identity.DisplayName);
            if (IoFile.Exists(icon))
                ShellProps.SetString(store, AppRegistration.RelaunchIconResourcePid, icon);
            else
                AppLogger.Warn($"[app:{identity.Key}] relaunch icon missing at {icon}; pin keeps the exe icon");
            store.Commit();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[app:{identity.Key}] relaunch properties not applied: {ex.Message}");
            return false;
        }
        finally
        {
            if (store is not null)
                Marshal.FinalReleaseComObject(store);
        }
    }
}
