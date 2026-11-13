using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using PdfSharpCore;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using static NetieSpace.Apps.Pdf.PdfToolSupport;

namespace NetieSpace.Apps.Pdf;

/// <summary>pdf-from-jpg: pictures to PDF pages with PdfSharpCore.</summary>
internal static class PdfImageTools
{
    public static Task<IReadOnlyList<string>> FromImagesAsync(ToolRequest req, IProgress<ToolProgress>? progress, CancellationToken ct) =>
        Work(ct, () =>
        {
            var orientation = OptChoice(req, "orientation", "portrait", "portrait", "landscape");
            var pageSize = OptChoice(req, "pagesize", "fit", "fit", "A4", "letter");
            var marginName = OptChoice(req, "margin", "none", "none", "small", "big");
            var merge = OptBool(req, "merge_after", true);
            var margin = marginName switch { "small" => 18.0, "big" => 36.0, _ => 0.0 };
            var names = new OutputNamer(req.OutDir);
            var outputs = new List<string>();

            PdfDocument? doc = null;
            try
            {
                for (var i = 0; i < req.Files.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var file = req.Files[i];
                    doc ??= new PdfDocument();
                    AddImagePage(doc, file, orientation, pageSize, margin);
                    if (!merge)
                    {
                        var path = names.Next(Stem(file), ".pdf");
                        doc.Save(path);
                        outputs.Add(path);
                        doc.Dispose();
                        doc = null;
                    }
                    Report(progress, i + 1, req.Files.Count, "placed " + IoPath.GetFileName(file));
                }
                if (doc is not null)
                {
                    var path = names.Next(Stem(req.Files[0]) + (req.Files.Count > 1 ? "_images" : ""), ".pdf");
                    doc.Save(path);
                    outputs.Add(path);
                }
            }
            finally { doc?.Dispose(); }
            return outputs;
        });

    private static void AddImagePage(PdfDocument doc, string file, string orientation, string pageSize, double margin)
    {
        byte[]? oriented = null;
        try
        {
            using var probe = SixLabors.ImageSharp.Image.Load<Rgba32>(file);
            var exif = probe.Metadata.ExifProfile;
            var orient = exif?.GetValue(ExifTag.Orientation);
            if (orient is not null && orient.Value > 1)
            {
                probe.Mutate(x => x.AutoOrient());
                using var ms = new MemoryStream();
                probe.Save(ms, new PngEncoder());
                oriented = ms.ToArray();
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"cannot decode image {file}: {ex.Message}", ex);
        }

        using var img = oriented is null ? XImage.FromFile(file) : XImage.FromStream(() => new MemoryStream(oriented));
        double iw = img.PixelWidth, ih = img.PixelHeight;
        var page = doc.AddPage();
        double pw, ph;
        if (pageSize == "fit")
        {
            // 96 pixels per inch -> points, so a photo does not become a billboard.
            pw = iw * 0.75 + margin * 2;
            ph = ih * 0.75 + margin * 2;
            // A PDF page is only valid between 3 and 14400 points a side: a 1 x 1 picture or a 20000 pixel
            // banner would otherwise give a page no viewer will open. Scale the page (the picture follows it).
            var grow = Math.Max(1.0, 3.0 / Math.Min(pw, ph));
            var shrink = Math.Min(1.0, 14400.0 / Math.Max(pw, ph));
            pw *= grow * shrink; ph *= grow * shrink;
            if (pw < 3 - 1e-6 || ph < 3 - 1e-6 || pw > 14400 + 1e-6 || ph > 14400 + 1e-6)
                throw new InvalidOperationException($"{IoPath.GetFileName(file)} is {iw} x {ih} pixels, too elongated to make a PDF page; use pagesize A4 or letter");
        }
        else
        {
            var size = pageSize == "A4" ? PageSize.A4 : PageSize.Letter;
            var s = PageSizeConverter.ToSize(size);
            pw = s.Width; ph = s.Height;
            if (orientation == "landscape") (pw, ph) = (ph, pw);
        }
        page.Width = pw; page.Height = ph;

        var availW = pw - margin * 2;
        var availH = ph - margin * 2;
        var scale = Math.Min(availW / iw, availH / ih);
        var dw = iw * scale; var dh = ih * scale;
        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawImage(img, (pw - dw) / 2, (ph - dh) / 2, dw, dh);
    }
}
