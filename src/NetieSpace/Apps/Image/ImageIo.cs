using System;
using System.Collections.Generic;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace NetieSpace.Apps.Image;

/// <summary>The four raster formats this build can read and write, by content rather than by extension.</summary>
public enum ImageKind { Jpeg, Png, Gif, Bmp }

/// <summary>Decode, encode and output-naming helpers shared by every image runner.</summary>
public static class ImageIo
{
    private static readonly HashSet<string> NotBuilt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tif", ".tiff", ".webp", ".heic", ".heif", ".psd", ".svg", ".jp2", ".ppm",
    };

    /// <summary>
    /// The one pixel budget for every image these tools open or create: 64 megapixels (8000 x 8000).
    /// A decoded frame costs 4 bytes a pixel and some tools hold two copies, so this caps a single
    /// decode near 256 MB. Images whose header declares more are refused before any pixel memory is allocated.
    /// </summary>
    public const long MaxPixels = 64L * 1000 * 1000;

    /// <summary>Throws a clear message when width x height is above <see cref="MaxPixels"/>.</summary>
    public static void CheckPixelBudget(long width, long height, string what)
    {
        if (width <= 0 || height <= 0 || width * height > MaxPixels)
            throw new InvalidOperationException(
                $"{what} is {width} x {height} pixels, above the limit of {MaxPixels / 1000000} megapixels this tool will open or create");
    }

    /// <summary>
    /// Same limit for an animation: every frame is decoded to a full canvas, so width x height x frames is what
    /// memory pays. Throws when that is above <see cref="MaxPixels"/>.
    /// </summary>
    public static void CheckPixelBudget(long width, long height, long frames, string what)
    {
        CheckPixelBudget(width, height, what);
        if (frames > 1 && width * height * frames > MaxPixels)
            throw new InvalidOperationException(
                $"{what} is {width} x {height} pixels with {frames} frames, above the limit of {MaxPixels / 1000000} megapixels in total this tool will open or create");
    }

    /// <summary>
    /// Counts the frames of a GIF by walking its block structure (no pixel decoding, no allocation beyond a buffer),
    /// stopping early once more than <paramref name="stopAfter"/> are seen. 1 for a file that is not a readable GIF.
    /// </summary>
    internal static int GifFrameCount(string path, int stopAfter = int.MaxValue)
    {
        try
        {
            using var s = new System.IO.BufferedStream(IoFile.OpenRead(path), 1 << 16);
            var head = new byte[13];
            if (s.Read(head, 0, 13) < 13 || head[0] != 'G' || head[1] != 'I' || head[2] != 'F') return 1;
            void SkipTable(int packed) { if ((packed & 0x80) != 0) Skip(s, 3 * (1 << ((packed & 7) + 1))); }
            SkipTable(head[10]);
            var frames = 0;
            while (frames <= stopAfter)
            {
                var id = s.ReadByte();
                if (id == 0x21) { s.ReadByte(); SkipSubBlocks(s); }
                else if (id == 0x2C)
                {
                    frames++;
                    var desc = new byte[9];
                    if (s.Read(desc, 0, 9) < 9) break;
                    SkipTable(desc[8]);
                    s.ReadByte();
                    SkipSubBlocks(s);
                }
                else break;
            }
            return Math.Max(1, frames);
        }
        catch (Exception) { return 1; }
    }

    private static void Skip(System.IO.Stream s, long n)
    {
        var buf = new byte[4096];
        while (n > 0) { var r = s.Read(buf, 0, (int)Math.Min(buf.Length, n)); if (r <= 0) throw new System.IO.EndOfStreamException(); n -= r; }
    }

    private static void SkipSubBlocks(System.IO.Stream s)
    {
        int len;
        while ((len = s.ReadByte()) > 0) Skip(s, len);
        if (len < 0) throw new System.IO.EndOfStreamException();
    }

    public static string Extension(ImageKind k) => k switch
    {
        ImageKind.Jpeg => ".jpg",
        ImageKind.Png => ".png",
        ImageKind.Gif => ".gif",
        _ => ".bmp",
    };

    public static ImageKind KindFromName(string name) => name.ToLowerInvariant() switch
    {
        "jpg" or "jpeg" => ImageKind.Jpeg,
        "png" => ImageKind.Png,
        "gif" => ImageKind.Gif,
        "bmp" => ImageKind.Bmp,
        _ => throw new ArgumentException("unknown image format '" + name + "'"),
    };

    /// <summary>Throws a clear message for extensions this build has no codec for.</summary>
    public static void RequireBuiltFormat(string path)
    {
        var ext = IoPath.GetExtension(path);
        if (NotBuilt.Contains(ext))
            throw new NotSupportedException(
                $"{ext.ToLowerInvariant()} images are not built in this version (png, jpg, gif and bmp are): {path}");
    }

    public static (Image<Rgba32> Image, ImageKind Kind) Load(string path)
    {
        RequireBuiltFormat(path);
        try
        {
            // Header-only read: nothing is allocated for pixels until the declared size passes the budget.
            IImageInfo info;
            try
            {
                info = SixLabors.ImageSharp.Image.Identify(path)
                    ?? throw new UnknownImageFormatException("unknown");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("max allowed size", StringComparison.OrdinalIgnoreCase))
            {
                // The bmp decoder has its own size guard; report it in our terms.
                throw new InvalidOperationException(
                    $"image is too large to open (limit {MaxPixels / 1000000} megapixels): {path}");
            }
            CheckPixelBudget(info.Width, info.Height, "image " + path);
            // An animation costs a full canvas per frame; the header alone does not say how many there are.
            var frames = GifFrameCount(path, (int)(MaxPixels / Math.Max(1, (long)info.Width * info.Height)));
            CheckPixelBudget(info.Width, info.Height, frames, "animated image " + path);
            var img = SixLabors.ImageSharp.Image.Load<Rgba32>(path, out var fmt);
            ImageKind kind;
            if (fmt is JpegFormat) kind = ImageKind.Jpeg;
            else if (fmt is PngFormat) kind = ImageKind.Png;
            else if (fmt is GifFormat) kind = ImageKind.Gif;
            else if (fmt is BmpFormat) kind = ImageKind.Bmp;
            else
            {
                img.Dispose();
                throw new NotSupportedException($"{fmt.Name} images are not built in this version: {path}");
            }
            return (img, kind);
        }
        catch (SixLabors.ImageSharp.Memory.InvalidMemoryOperationException)
        {
            // The decoder's own size guard fired (for example a bmp declaring an absurd canvas).
            throw new InvalidOperationException(
                $"image is too large to open (limit {MaxPixels / 1000000} megapixels): {path}");
        }
        catch (UnknownImageFormatException)
        {
            throw new InvalidOperationException("not a readable png, jpg, gif or bmp image: " + path);
        }
        catch (InvalidImageContentException ex)
        {
            throw new InvalidOperationException("image is damaged: " + path + " (" + ex.Message + ")");
        }
    }

    public static IImageEncoder Encoder(ImageKind kind, int jpegQuality = 90) => kind switch
    {
        ImageKind.Jpeg => new JpegEncoder { Quality = jpegQuality },
        ImageKind.Png => new PngEncoder(),
        ImageKind.Gif => new GifEncoder(),
        _ => new BmpEncoder(),
    };

    /// <summary>
    /// Reserves an output path inside <paramref name="outDir"/> that does not exist yet and was not handed
    /// out earlier in this run, so two inputs called the same never overwrite each other or the input.
    /// </summary>
    public sealed class OutputNamer
    {
        private readonly string _outDir;
        private readonly HashSet<string> _taken = new(StringComparer.OrdinalIgnoreCase);

        public OutputNamer(string outDir) { _outDir = outDir; IoDirectory.CreateDirectory(outDir); }

        public string Reserve(string inputPath, string suffix, string extension)
        {
            var stem = IoPath.GetFileNameWithoutExtension(inputPath);
            var n = 1;
            while (true)
            {
                var name = stem + suffix + (n == 1 ? "" : "-" + n) + extension;
                var full = IoPath.Combine(_outDir, name);
                if (!IoFile.Exists(full) && _taken.Add(full)) return full;
                n++;
            }
        }
    }

    public static int IntOpt(IReadOnlyDictionary<string, string> o, string key, int fallback = 0) =>
        o.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : fallback;

    public static bool BoolOpt(IReadOnlyDictionary<string, string> o, string key, bool fallback) =>
        o.TryGetValue(key, out var v) ? v == "true" : fallback;

    public static string StrOpt(IReadOnlyDictionary<string, string> o, string key, string fallback) =>
        o.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : fallback;

    /// <summary>Parses #RRGGBB (opaque only) into a colour.</summary>
    public static Color ParseOpaqueHex(string optionKey, string value)
    {
        var v = value.Trim();
        if (v.Length != 7 || v[0] != '#' || !v.Skip(1).All(Uri.IsHexDigit))
            throw new ArgumentException($"option '{optionKey}': '{value}' must be #RRGGBB (JPG has no transparency)");
        return Color.ParseHex(v.Substring(1));
    }
}
