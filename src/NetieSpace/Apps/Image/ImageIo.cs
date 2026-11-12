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
