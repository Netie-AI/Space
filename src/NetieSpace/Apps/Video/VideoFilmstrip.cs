using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static NetieSpace.Apps.Video.VideoFfmpeg;

namespace NetieSpace.Apps.Video;

/// <summary>The thumbnails of one clip: <see cref="Times"/>[i] is the second the picture <see cref="Frames"/>[i] was taken at.</summary>
internal sealed record Filmstrip(VideoInfo Info, IReadOnlyList<string> Frames, IReadOnlyList<double> Times, bool FromCache);

/// <summary>
/// The picture strip of the quick edit. N JPEGs are cut out with ffmpeg using fast input seeking (-ss before -i, one
/// frame each, a few at a time) and kept in a cache folder named by file + size + mtime + shape, so reopening the same
/// clip draws the strip at once and an edited file is never shown with its old pictures.
/// </summary>
internal static class VideoFilmstrip
{
    public const int MaxCount = 40;

    public static string DefaultCacheRoot() =>
        IoPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetieSpace", "cache", "filmstrip");

    /// <summary>Same file, same bytes, same mtime, same shape: same folder.</summary>
    public static string CacheKey(string path, int count, int height)
    {
        var full = IoPath.GetFullPath(path);
        var fi = new System.IO.FileInfo(full);
        var raw = full.ToLowerInvariant() + "|" + fi.Length.ToString(CultureInfo.InvariantCulture) + "|"
            + fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + count.ToString(CultureInfo.InvariantCulture)
            + "x" + height.ToString(CultureInfo.InvariantCulture);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..20].ToLowerInvariant();
    }

    public static IReadOnlyList<double> TimesFor(double duration, int count) =>
        Enumerable.Range(0, count).Select(i => duration * (i + 0.5) / count).ToList();

    public static async Task<Filmstrip> BuildAsync(string path, int count, int height, CancellationToken ct, string? cacheRoot = null, VideoInfo? probed = null)
    {
        count = Math.Clamp(count, 1, MaxCount);
        height = Math.Clamp(height, 16, 512);
        var input = Full(path);
        var info = probed ?? RequireVideo(input);
        var times = TimesFor(info.Duration, count);
        var root = cacheRoot ?? DefaultCacheRoot();
        var dir = IoPath.Combine(root, CacheKey(input, count, height));

        string[] Expected() => Enumerable.Range(0, count).Select(i => IoPath.Combine(dir, "f" + i.ToString("00", CultureInfo.InvariantCulture) + ".jpg")).ToArray();
        var want = Expected();
        if (want.All(f => IoFile.Exists(f) && new System.IO.FileInfo(f).Length > 0))
            return new Filmstrip(info, want, times, true);

        // Built beside the final folder and moved in whole, so a cancelled or crashed run never leaves a half strip that looks complete.
        var work = dir + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        IoDirectory.CreateDirectory(work);
        try
        {
            using var gate = new SemaphoreSlim(Math.Min(4, Math.Max(1, Environment.ProcessorCount)));
            var jobs = Enumerable.Range(0, count).Select(async i =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var file = IoPath.Combine(work, "f" + i.ToString("00", CultureInfo.InvariantCulture) + ".jpg");
                    await FrameAsync(input, times[i], height, file, ct).ConfigureAwait(false);
                }
                finally { gate.Release(); }
            }).ToList();
            await Task.WhenAll(jobs).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            try { IoDirectory.Move(work, dir); }
            catch (System.IO.IOException) when (IoDirectory.Exists(dir)) { /* another run finished first; its folder is as good */ }
        }
        finally
        {
            try { if (IoDirectory.Exists(work)) IoDirectory.Delete(work, true); } catch { /* a temp folder is not a result */ }
        }
        return new Filmstrip(info, Expected(), times, false);
    }

    /// <summary>One JPEG of the clip at <paramref name="seconds"/>, <paramref name="height"/> pixels tall (fast seek).</summary>
    public static async Task FrameAsync(string input, double seconds, int height, string outFile, CancellationToken ct)
    {
        var args = new List<string>
        {
            "-ss", Num(Math.Max(0, seconds)), "-i", input, "-frames:v", "1", "-an",
            "-vf", SquarePixels + ",scale=-2:" + height.ToString(CultureInfo.InvariantCulture),
            "-q:v", "4", outFile,
        };
        await RunAsync(args, 0, null, "thumbnail", new[] { outFile }, ct).ConfigureAwait(false);
        if (!IoFile.Exists(outFile) || new System.IO.FileInfo(outFile).Length == 0)
            throw new InvalidOperationException("ffmpeg made no picture at " + Num(seconds) + " s of " + IoPath.GetFileName(input));
    }

    /// <summary>A larger still of the clip at <paramref name="seconds"/>, cached with the strip; the poster shown while the player opens.</summary>
    public static async Task<string> PosterAsync(string path, double seconds, int width, CancellationToken ct, string? cacheRoot = null)
    {
        var input = Full(path);
        var key = CacheKey(input, 0, width);
        var dir = IoPath.Combine(cacheRoot ?? DefaultCacheRoot(), key);
        IoDirectory.CreateDirectory(dir);
        var file = IoPath.Combine(dir, "poster-" + ((long)Math.Round(seconds * 10)).ToString(CultureInfo.InvariantCulture) + ".jpg");
        if (IoFile.Exists(file) && new System.IO.FileInfo(file).Length > 0) return file;
        var tmp = file + ".tmp-" + Guid.NewGuid().ToString("N")[..8] + ".jpg";
        var args = new List<string>
        {
            "-ss", Num(Math.Max(0, seconds)), "-i", input, "-frames:v", "1", "-an",
            "-vf", SquarePixels + ",scale=" + width.ToString(CultureInfo.InvariantCulture) + ":-2", "-q:v", "3", tmp,
        };
        await RunAsync(args, 0, null, "poster", new[] { tmp }, ct).ConfigureAwait(false);
        try { IoFile.Move(tmp, file, true); } catch (System.IO.IOException) { TryDelete(tmp); }
        return file;
    }
}
