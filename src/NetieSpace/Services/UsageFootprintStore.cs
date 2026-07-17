using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>
/// Local usage + footprint log (SQLite-free JSONL under %LOCALAPPDATA%).
/// Opt-in export is DPAPI-protected for Netie learning only — never ships secrets.
/// </summary>
public static class UsageFootprintStore
{
    private static readonly object Gate = new();
    private static string Dir => IoPath.Combine(EnvLoader.UserDataDirectory, "data");
    private static string LogPath => IoPath.Combine(Dir, "usage.jsonl");
    private static string ExportPath => IoPath.Combine(Dir, "learning-export.bin");

    public static void Track(string kind, string? detail = null, long? bytes = null)
    {
        try
        {
            IoDirectory.CreateDirectory(Dir);
            var ext = string.IsNullOrWhiteSpace(detail) ? null : IoPath.GetExtension(detail).ToLowerInvariant();
            // Never store full paths or file contents — extension + kind only
            var row = new
            {
                t = DateTime.UtcNow.ToString("o"),
                kind,
                ext,
                bytes,
                ver = "1"
            };
            var line = JsonSerializer.Serialize(row) + "\n";
            lock (Gate)
            {
                IoFile.AppendAllText(LogPath, line);
                TrimIfHuge();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Usage track failed: {ex.Message}");
        }
    }

    private static void TrimIfHuge()
    {
        try
        {
            var fi = new System.IO.FileInfo(LogPath);
            if (!fi.Exists || fi.Length < 2_000_000)
                return;
            var lines = IoFile.ReadAllLines(LogPath);
            if (lines.Length <= 2000)
                return;
            IoFile.WriteAllLines(LogPath, lines.TakeLast(1500));
        }
        catch { /* ignore */ }
    }

    /// <summary>DPAPI-encrypt a redacted footprint snapshot for optional Netie learning sync.</summary>
    public static string? ExportEncryptedLearningBlob()
    {
        try
        {
            if (!IoFile.Exists(LogPath))
                return null;
            IoDirectory.CreateDirectory(Dir);
            var plain = Encoding.UTF8.GetBytes(IoFile.ReadAllText(LogPath));
            var protectedBytes = ProtectedData.Protect(plain, optionalEntropy: Encoding.UTF8.GetBytes("NetieSpace.Learn.v1"),
                scope: DataProtectionScope.CurrentUser);
            IoFile.WriteAllBytes(ExportPath, protectedBytes);
            return ExportPath;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Learning export failed: {ex.Message}");
            return null;
        }
    }

    public static (int events, long bytes) SnapshotStats()
    {
        try
        {
            if (!IoFile.Exists(LogPath))
                return (0, 0);
            var lines = IoFile.ReadAllLines(LogPath);
            return (lines.Length, new System.IO.FileInfo(LogPath).Length);
        }
        catch
        {
            return (0, 0);
        }
    }
}
