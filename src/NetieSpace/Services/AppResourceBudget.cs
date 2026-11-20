using System.Diagnostics;
using System.IO;
using System.Runtime;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>
/// Persistent caches under %LOCALAPPDATA%\NetieSpace and process memory headroom.
/// Preview stays light; Edit mode uses these caches for FFmpeg/WebView work.
/// </summary>
public static class AppResourceBudget
{
    public static string CacheRoot => IoPath.Combine(EnvLoader.UserDataDirectory, "cache");
    public static string VideoPreviewCache => IoPath.Combine(CacheRoot, "video-preview");
    public static string VideoEditCache => IoPath.Combine(CacheRoot, "video-edit");
    public static string PdfCache => IoPath.Combine(CacheRoot, "pdf");
    public static string ChatClipsCache => IoPath.Combine(CacheRoot, "chat-clips");
    public static string OfficeCache => IoPath.Combine(CacheRoot, "office");
    /// <summary>Keep up to ~8 GB of on-disk caches before pruning oldest files.</summary>
    public const long MaxCacheBytes = 8L * 1024 * 1024 * 1024;

    public static void Initialize()
    {
        foreach (var dir in new[]
                 {
                     CacheRoot, VideoPreviewCache, VideoEditCache, PdfCache, ChatClipsCache, OfficeCache
                 })
        {
            try { IoDirectory.CreateDirectory(dir); }
            catch { /* ignore */ }
        }

        try
        {
            // Do not set Min/MaxWorkingSet — Windows rejects invalid ranges and it does not help stability.
            // Rely on GC + disk caches under %LOCALAPPDATA%\NetieSpace\cache.
            GCSettings.LatencyMode = GCLatencyMode.Interactive;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"GC latency mode skipped: {ex.Message}");
        }

        AppLogger.Info($"Caches at {CacheRoot} (budget {MaxCacheBytes / (1024 * 1024)} MB disk)");
        _ = Task.Run(PruneCachesIfNeeded);
    }

    public static void PruneCachesIfNeeded()
    {
        try
        {
            if (!IoDirectory.Exists(CacheRoot))
                return;

            var files = IoDirectory.EnumerateFiles(CacheRoot, "*", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .Where(f => f.Exists)
                .OrderBy(f => f.LastAccessTimeUtc)
                .ToList();

            long total = files.Sum(f => f.Length);
            if (total <= MaxCacheBytes)
                return;

            foreach (var f in files)
            {
                if (total <= MaxCacheBytes * 3 / 4)
                    break;
                try
                {
                    total -= f.Length;
                    f.Delete();
                }
                catch { /* ignore locked */ }
            }

            AppLogger.Info($"Cache pruned to ~{total / (1024 * 1024)} MB");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Cache prune failed: {ex.Message}");
        }
    }
}
