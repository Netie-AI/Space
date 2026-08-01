using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NetieSpace.Helpers;
using NetieSpace.Views;

namespace NetieSpace.Services;

/// <summary>Simple local image tools — compress / re-encode without cloud.</summary>
public static class ImageEditService
{
    public static long GetFileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    /// <summary>
    /// Compress while KEEPING the source file type (png→png, jpg→jpg, …). JPEG uses the quality
    /// slider directly; lossless formats (png/bmp/tiff/gif) re-encode and, since re-encoding alone
    /// barely shrinks them, apply a mild downscale derived from the quality so the file actually gets
    /// smaller. Writes beside the original as *_q&lt;quality&gt;.&lt;ext&gt; unless overwrite is set.
    /// </summary>
    public static string Compress(string sourcePath, int quality, bool overwrite = false)
    {
        if (!IoFile.Exists(sourcePath))
            throw new FileNotFoundException("Image not found", sourcePath);

        if (IsAnimatedGif(sourcePath))
            throw new InvalidOperationException(
                "Animated GIFs cannot be compressed here — it would flatten all frames to a single still.");

        quality = Math.Clamp(quality, 5, 100);
        var ext = IoPath.GetExtension(sourcePath).ToLowerInvariant();
        var isJpeg = ext is ".jpg" or ".jpeg";

        var source = LoadBitmap(sourcePath);

        // Lossless formats don't honour a quality knob — downscale a touch to guarantee a smaller file.
        BitmapSource frame = source;
        if (!isJpeg && quality < 90)
        {
            var scale = Math.Clamp(0.55 + (quality / 100.0) * 0.4, 0.55, 0.95); // q60≈0.79, q75≈0.85
            frame = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            frame.Freeze();
        }

        BitmapEncoder encoder = ext switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = quality },
            ".png" => new PngBitmapEncoder(),
            ".bmp" => new BmpBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder { Compression = TiffCompressOption.Zip },
            ".gif" => new GifBitmapEncoder(),
            // Unknown/exotic (webp, heic, …): fall back to JPEG and let the destination extension reflect it.
            _ => new JpegBitmapEncoder { QualityLevel = quality }
        };
        var outExt = encoder is JpegBitmapEncoder && !isJpeg && ext is not (".jpg" or ".jpeg")
            ? ".jpg"
            : ext;

        encoder.Frames.Add(BitmapFrame.Create(frame));

        var dir = IoPath.GetDirectoryName(sourcePath) ?? ".";
        var name = IoPath.GetFileNameWithoutExtension(sourcePath);
        var dest = overwrite
            ? IoPath.Combine(dir, name + outExt)
            : IoPath.Combine(dir, $"{name}_q{quality}{outExt}");

        if (!overwrite)
        {
            var i = 1;
            while (IoFile.Exists(dest))
            {
                dest = IoPath.Combine(dir, $"{name}_q{quality}_{i}{outExt}");
                i++;
            }
        }

        using (var fs = IoFile.Create(dest))
            encoder.Save(fs);

        return dest;
    }

    /// <summary>Back-compat shim — old callers asked for JPEG explicitly.</summary>
    public static string CompressJpeg(string sourcePath, int quality, bool overwrite = false)
    {
        // Force a .jpg destination regardless of source (legacy behaviour).
        if (!IoFile.Exists(sourcePath))
            throw new FileNotFoundException("Image not found", sourcePath);

        quality = Math.Clamp(quality, 5, 100);
        var bmp = LoadBitmap(sourcePath);
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(bmp));

        var dir = IoPath.GetDirectoryName(sourcePath) ?? ".";
        var name = IoPath.GetFileNameWithoutExtension(sourcePath);
        var dest = overwrite
            ? IoPath.Combine(dir, name + ".jpg")
            : IoPath.Combine(dir, $"{name}_q{quality}.jpg");

        if (!overwrite)
        {
            var i = 1;
            while (IoFile.Exists(dest))
            {
                dest = IoPath.Combine(dir, $"{name}_q{quality}_{i}.jpg");
                i++;
            }
        }

        using (var fs = IoFile.Create(dest))
            encoder.Save(fs);

        return dest;
    }

    public static string? PromptCompress(Window owner, string sourcePath)
    {
        if (IsAnimatedGif(sourcePath))
        {
            GlassChoiceDialog.Show(owner,
                "Animated GIF",
                "This GIF has multiple frames. Compress would flatten it to a single still and lose the animation.\n\n" +
                "Use Share → Export as… → WEBP/GIF instead (frame-preserving), or a dedicated GIF optimizer.",
                [("ok", "OK", true)]);
            return null;
        }

        var size = GetFileSize(sourcePath);
        var ext = IoPath.GetExtension(sourcePath).ToLowerInvariant();
        var isJpeg = ext is ".jpg" or ".jpeg";
        var needsJpegChoice = ext is ".webp" or ".heic" or ".heif";

        if (needsJpegChoice)
        {
            var convert = GlassChoiceDialog.Show(owner,
                "Convert to JPEG?",
                $"WPF cannot re-encode {ext.TrimStart('.').ToUpperInvariant()} in-place. " +
                "Compress will write a JPEG beside the original (transparency may be lost).\n\n" +
                $"Current size: {FormatSize(size)}. Or use Share → Export as… for WEBP/PNG without this downgrade.",
                [
                    ("q60", "JPEG · quality 60", true),
                    ("q75", "JPEG · quality 75", false),
                    ("q85", "JPEG · quality 85", false),
                    ("cancel", "Cancel", false)
                ]);
            if (convert is null or "cancel") return null;
            var qj = convert switch { "q60" => 60, "q85" => 85, _ => 75 };
            return CompressJpeg(sourcePath, qj, overwrite: false);
        }

        var typeNote = isJpeg
            ? "Stays JPEG."
            : $"Stays {ext.TrimStart('.').ToUpperInvariant()} (kept lossless, lightly downscaled to shrink).";

        var choice = GlassChoiceDialog.Show(owner,
            "Compress image",
            $"Current size: {FormatSize(size)}. Pick quality (lower = smaller file). {typeNote}",
            [
                ("q60", "Small · quality 60", true),
                ("q75", "Balanced · quality 75", false),
                ("q85", "High · quality 85", false),
                ("cancel", "Cancel", false)
            ]);

        if (choice is null or "cancel") return null;
        var q = choice switch { "q60" => 60, "q85" => 85, _ => 75 };
        return Compress(sourcePath, q, overwrite: false);
    }

    public static string? PromptSaveAs(Window owner, string sourcePath)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save compressed JPEG",
            Filter = "JPEG|*.jpg;*.jpeg",
            FileName = IoPath.GetFileNameWithoutExtension(sourcePath) + "_compressed.jpg",
            InitialDirectory = IoPath.GetDirectoryName(sourcePath)
        };
        if (dlg.ShowDialog(owner) != true) return null;

        var bmp = LoadBitmap(sourcePath);
        var encoder = new JpegBitmapEncoder { QualityLevel = 75 };
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = IoFile.Create(dlg.FileName);
        encoder.Save(fs);
        return dlg.FileName;
    }

    /// <summary>True when a GIF has more than one frame (animation would be lost on re-encode).</summary>
    public static bool IsAnimatedGif(string path)
    {
        if (!IoPath.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            using var stream = IoFile.OpenRead(path);
            var decoder = BitmapDecoder.Create(
                stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return decoder.Frames.Count > 1;
        }
        catch
        {
            return false;
        }
    }

    private static BitmapSource LoadBitmap(string path)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bi.UriSource = new Uri(path);
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024):0.##} MB";
    }
}
