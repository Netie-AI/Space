using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>
/// Win32 Application Recovery & Restart — OS relaunches Netie after a hard crash.
/// Not Docker (desktop app). Stores last preview path for optional --recover reopen.
/// </summary>
public static class CrashRecovery
{
    private static readonly string SessionPath = IoPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NetieSpace", "last-session.txt");

    public static void Register()
    {
        try
        {
            // Relaunch with --recover after crash (not after clean Exit)
            Win32Api.RegisterApplicationRestart("--recover", 0);
            AppLogger.Info("Crash recovery registered (Win32 ARR)");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Crash recovery register skipped: {ex.Message}");
        }
    }

    public static void RememberPreview(string? filePath)
    {
        try
        {
            IoDirectory.CreateDirectory(IoPath.GetDirectoryName(SessionPath)!);
            IoFile.WriteAllText(SessionPath, filePath ?? "");
        }
        catch { /* ignore */ }
    }

    public static string? LoadLastPreview()
    {
        try
        {
            if (!IoFile.Exists(SessionPath)) return null;
            var p = IoFile.ReadAllText(SessionPath).Trim();
            return IoFile.Exists(p) ? p : null;
        }
        catch { return null; }
    }
}
