using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NetieSpace.Apps.Image;

/// <summary>img-meme: a bold white caption with a black outline at the top and/or bottom of one image.</summary>
public sealed class ImageMemeProvider : IToolProvider
{
    public ToolApp App => ToolApp.Image;

    public IEnumerable<(string ToolId, ToolRunnerDelegate Run)> Runners()
    {
        yield return ("img-meme", (r, p, ct) => Task.Run<IReadOnlyList<string>>(() => Meme(r, p, ct), ct));
    }

    private static IReadOnlyList<string> Meme(ToolRequest rq, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var o = rq.Options;
        var hasTop = o.TryGetValue("top", out var topRaw) && topRaw != null;
        var hasBottom = o.TryGetValue("bottom", out var botRaw) && botRaw != null;
        if (!hasTop && !hasBottom)
            throw new ArgumentException("option 'top' or option 'bottom' is required (the caption text)");
        // A given caption must be real text; an empty or blank one is a mistake, not "no caption".
        var top = hasTop ? ImageText.CleanText("top", topRaw).ToUpperInvariant() : null;
        var bottom = hasBottom ? ImageText.CleanText("bottom", botRaw).ToUpperInvariant() : null;
        var family = ImageText.ResolveFamily();

        var namer = new ImageIo.OutputNamer(rq.OutDir);
        var outputs = new List<string>();
        var total = rq.Files.Count;
        for (var i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var input = rq.Files[i];
            var (loaded, kind) = ImageIo.Load(input);
            using (loaded)
            using (var img = loaded.Frames.Count > 1 ? loaded.Frames.CloneFrame(0) : loaded.Clone())
            {
                img.Mutate(x => x.AutoOrient());
                var margin = Math.Max(2f, img.Height * 0.03f);
                if (top != null) Place(img, top, family, margin, atTop: true);
                if (bottom != null) Place(img, bottom, family, margin, atTop: false);
                var path = namer.Reserve(input, "-meme", ImageIo.Extension(kind));
                img.Save(path, ImageIo.Encoder(kind));
                outputs.Add(path);
            }
            progress?.Report(new ToolProgress((i + 1) * 100 / Math.Max(1, total), "image " + (i + 1) + " of " + total));
        }
        return outputs;
    }

    private static void Place(Image<Rgba32> img, string text, SixLabors.Fonts.FontFamily family, float margin, bool atTop)
    {
        var maxW = img.Width * 0.94f;
        var maxH = img.Height * 0.30f;
        var size = Math.Max(8f, img.Height / 7f);
        ImageText.Ink ink;
        while (true)
        {
            ink = ImageText.Layout(text, family, size, true, maxW);
            if (ink.Width <= maxW && ink.Height <= maxH) break;
            if (size <= 6f)
                throw new InvalidOperationException("the caption is too long to fit on this image; shorten it");
            size = Math.Max(6f, size * 0.9f);
        }
        var left = (img.Width - ink.Width) / 2f;
        var top = atTop ? margin : img.Height - margin - ink.Height;
        var outline = Math.Max(1f, size / 14f);
        ImageText.Draw(img, ink, left, top, new Rgba32(255, 255, 255, 255), 1f, outline, new Rgba32(0, 0, 0, 255));
    }
}
