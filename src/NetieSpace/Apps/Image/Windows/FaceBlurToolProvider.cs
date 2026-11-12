using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;
using SRect = SixLabors.ImageSharp.Rectangle;

namespace NetieSpace.Apps.Image.WinRt;

/// <summary>
/// img-blur-face: faces found by the Windows face detector (or rectangles given by hand) are blurred or
/// pixelated in the full-colour image, which is saved in its own format. Compiled only for the Windows TFM.
/// </summary>
public sealed class FaceBlurToolProvider : IToolProvider
{
    // Detector boxes sit on the eyes, nose and mouth; grow them so hair line and chin are covered.
    public const double FacePadding = 0.25;
    private const int DetectMaxSide = 1600;

    public ToolApp App => ToolApp.Image;

    public IEnumerable<(string ToolId, ToolRunnerDelegate Run)> Runners()
    {
        yield return ("img-blur-face", (r, p, ct) => Task.Run(() => RunAsync(r, p, ct), ct));
    }

    private static async Task<IReadOnlyList<string>> RunAsync(
        ToolRequest request, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var mode = ImageIo.StrOpt(request.Options, "mode", "blur").ToLowerInvariant();
        if (mode != "blur" && mode != "pixelate")
            throw new ArgumentException("option 'mode': '" + mode + "' is not blur or pixelate");
        var strength = 60;
        if (request.Options.TryGetValue("strength", out var strengthText)
            && !int.TryParse(strengthText, out strength))
            throw new ArgumentException("option 'strength': '" + strengthText + "' is not a number");
        if (strength < 1 || strength > 100)
            throw new ArgumentException("option 'strength': " + strength + " is outside 1..100");
        var regionsText = ImageIo.StrOpt(request.Options, "regions", "");

        var namer = new ImageIo.OutputNamer(request.OutDir);
        var outputs = new List<string>();
        try
        {
            for (var i = 0; i < request.Files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var input = request.Files[i];
                var (img, kind) = ImageIo.Load(input);
                using (img)
                {
                    if (img.Frames.Count > 1)
                        throw new NotSupportedException("animated images are not supported by this tool: " + input);

                    IReadOnlyList<SRect> boxes;
                    if (regionsText.Length > 0)
                    {
                        boxes = FaceBlurCore.ParseRegions(regionsText, img.Width, img.Height);
                    }
                    else
                    {
                        var faces = await DetectAsync(img, ct);
                        if (faces.Count == 0)
                            throw new InvalidOperationException(
                                "no faces found in " + input + "; nothing was changed (give option 'regions' to blur areas by hand)");
                        boxes = faces.Select(b => FaceBlurCore.Pad(b, FacePadding, img.Width, img.Height)).ToList();
                    }

                    FaceBlurCore.Apply(img, boxes, mode, strength);
                    var path = namer.Reserve(input, "-blurred", ImageIo.Extension(kind));
                    img.Save(path, ImageIo.Encoder(kind, 92));
                    outputs.Add(path);
                }
                progress?.Report(new ToolProgress((i + 1) * 100 / request.Files.Count,
                    "image " + (i + 1) + " of " + request.Files.Count));
            }
        }
        catch
        {
            foreach (var path in outputs)
            {
                try { IoFile.Delete(path); } catch { /* ignore */ }
            }
            throw;
        }
        return outputs;
    }

    /// <summary>Face boxes in the pixel space of <paramref name="image"/>, as the detector reports them.</summary>
    public static async Task<IReadOnlyList<SRect>> DetectAsync(SixLabors.ImageSharp.Image<Rgba32> image, CancellationToken ct)
    {
        if (!FaceDetector.IsSupported)
            throw new InvalidOperationException("face detection is not supported on this PC");
        var supported = FaceDetector.GetSupportedBitmapPixelFormats();
        BitmapPixelFormat target;
        if (supported.Contains(BitmapPixelFormat.Gray8)) target = BitmapPixelFormat.Gray8;
        else if (supported.Contains(BitmapPixelFormat.Nv12)) target = BitmapPixelFormat.Nv12;
        else throw new InvalidOperationException("the face detector accepts no pixel format this tool can produce");

        // A smaller copy detects faster and as well; boxes are scaled back to the full image below.
        var longest = Math.Max(image.Width, image.Height);
        var scale = longest > DetectMaxSide ? DetectMaxSide / (double)longest : 1.0;
        var w = Math.Max(2, (int)Math.Round(image.Width * scale));
        var h = Math.Max(2, (int)Math.Round(image.Height * scale));
        if (target == BitmapPixelFormat.Nv12) { w &= ~1; h &= ~1; }

        SixLabors.ImageSharp.Image<Rgba32>? resized = null;
        try
        {
            var source = image;
            if (w != image.Width || h != image.Height)
            {
                resized = image.Clone(c => c.Resize(w, h));
                source = resized;
            }

            // BGRA bytes; transparency is flattened onto white so a transparent corner is not read as black.
            var bytes = FlattenToBgra(source, w, h);

            using var bgra = SoftwareBitmap.CreateCopyFromBuffer(
                bytes.AsBuffer(), BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Ignore);
            using var converted = SoftwareBitmap.Convert(bgra, target);
            var detector = await FaceDetector.CreateAsync().AsTask(ct);
            var faces = await detector.DetectFacesAsync(converted).AsTask(ct);

            var sx = image.Width / (double)w;
            var sy = image.Height / (double)h;
            var boxes = new List<SRect>();
            foreach (var face in faces)
            {
                var b = face.FaceBox;
                var x0 = (int)Math.Floor(b.X * sx);
                var y0 = (int)Math.Floor(b.Y * sy);
                var x1 = (int)Math.Ceiling((b.X + b.Width) * sx);
                var y1 = (int)Math.Ceiling((b.Y + b.Height) * sy);
                var clipped = SRect.Intersect(new SRect(x0, y0, x1 - x0, y1 - y0), new SRect(0, 0, image.Width, image.Height));
                if (clipped.Width > 0 && clipped.Height > 0) boxes.Add(clipped);
            }
            return boxes;
        }
        finally
        {
            resized?.Dispose();
        }
    }

    /// <summary>BGRA bytes of the image; transparency is flattened onto white so a clear corner is not read as black.</summary>
    internal static byte[] FlattenToBgra(SixLabors.ImageSharp.Image<Rgba32> source, int w, int h)
    {
        var bytes = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            var row = source.GetPixelRowSpan(y);
            for (var x = 0; x < w; x++)
            {
                var p = row[x];
                int a = p.A;
                var o = (y * w + x) * 4;
                bytes[o] = (byte)((p.B * a + 255 * (255 - a)) / 255);
                bytes[o + 1] = (byte)((p.G * a + 255 * (255 - a)) / 255);
                bytes[o + 2] = (byte)((p.R * a + 255 * (255 - a)) / 255);
                bytes[o + 3] = 255;
            }
        }
        return bytes;
    }
}
