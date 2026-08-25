using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using NetieSpace.Helpers;
using UglyToad.PdfPig.AcroForms;
using UglyToad.PdfPig.AcroForms.Fields;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;

namespace NetieSpace.Services;

/// <summary>
/// One entry in an organize plan: which source page to keep and extra clockwise rotation.
/// </summary>
public sealed class PdfPagePlanItem
{
    /// <summary>0-based index in the original PDF.</summary>
    public int SourcePageIndex { get; init; }

    /// <summary>Additional rotation applied on top of the page's existing Rotate (0/90/180/270).</summary>
    public int ExtraRotateDegrees { get; init; }
}

/// <summary>AcroForm widget rectangle in PDF user space (origin bottom-left).</summary>
public sealed record PdfFormFieldRect(int Page, double X, double Y, double Width, double Height, string? Name);

/// <summary>Text word/line box in PDF user space (origin bottom-left) for bbox editing.</summary>
public sealed record PdfTextBox(int Page, double X, double Y, double Width, double Height, string Text);

/// <summary>One Fill &amp; Sign typed-text box in PDF points, origin bottom-left (viewer convention).</summary>
public sealed record PdfFillTextItem(
    int Page, double XPt, double YFromBottom, double WidthPt, double HeightPt,
    double FontPt, string Text);

/// <summary>
/// Comment highlight or ink mark in PDF points, origin bottom-left (same as stamps/fills).
/// <paramref name="Kind"/> is "highlight" (yellow) or "mark" (teal).
/// </summary>
public sealed record PdfHighlightItem(
    int Page, double XPt, double YFromBottom, double WidthPt, double HeightPt,
    string Kind = "highlight");

/// <summary>
/// Compose slot: either import a source page or insert a new page from an image file.
/// </summary>
public sealed class PdfComposeSlot
{
    public bool IsImage { get; init; }
    public int SourcePageIndex { get; init; } = -1;
    public int ExtraRotateDegrees { get; init; }
    public string? ImagePath { get; init; }
}

/// <summary>
/// Acrobat-light PDF ops via PdfSharpCore (MIT). Merge / split / extract / rotate / reorder / image stamp.
/// Not a full digital-signature (cert) implementation.
/// </summary>
public sealed class PdfEditService
{
    private static readonly string[] SignatureFontCandidates =
    [
        "Segoe Script",
        "Ink Free",
        "Freestyle Script",
        "Brush Script MT",
        "Segoe Print",
        "Comic Sans MS",
        "Segoe UI"
    ];

    public string Merge(string primaryPdf, IEnumerable<string> additionalPdfs)
    {
        var extras = additionalPdfs.Where(IoFile.Exists).ToList();
        if (extras.Count == 0)
            throw new InvalidOperationException("No PDF files to append.");

        var output = SiblingPath(primaryPdf, "merged");
        using var outDoc = new PdfDocument();
        AppendFile(outDoc, primaryPdf);
        foreach (var extra in extras)
            AppendFile(outDoc, extra);

        outDoc.Save(output);
        AppLogger.Info($"Merged PDF saved: {output}");
        return output;
    }

    /// <summary>Merge an ordered list of PDFs (first path = first pages).</summary>
    public string MergeOrdered(IReadOnlyList<string> orderedPdfs)
    {
        var files = orderedPdfs.Where(IoFile.Exists).ToList();
        if (files.Count < 2)
            throw new InvalidOperationException("Need at least two PDFs to merge.");

        var output = SiblingPath(files[0], "merged");
        using var outDoc = new PdfDocument();
        foreach (var f in files)
            AppendFile(outDoc, f);

        outDoc.Save(output);
        AppLogger.Info($"Merged ordered PDF saved: {output}");
        return output;
    }

    /// <summary>One PDF per page into a sibling folder. Returns the folder path.</summary>
    public string SplitAllPages(string pdfPath)
    {
        using var input = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        if (input.PageCount == 0)
            throw new InvalidOperationException("PDF has no pages.");

        var dir = IoPath.Combine(
            IoPath.GetDirectoryName(pdfPath)!,
            $"{IoPath.GetFileNameWithoutExtension(pdfPath)}_split_{DateTime.Now:HHmmss}");
        IoDirectory.CreateDirectory(dir);

        for (var i = 0; i < input.PageCount; i++)
        {
            using var outDoc = new PdfDocument();
            outDoc.AddPage(input.Pages[i]);
            outDoc.Save(IoPath.Combine(dir, $"page_{i + 1:D3}.pdf"));
        }

        AppLogger.Info($"Split PDF into {input.PageCount} files: {dir}");
        return dir;
    }

    /// <summary>Extract 1-based page numbers into a new PDF. Ranges like 1-3,5,7 are accepted.</summary>
    public string ExtractPages(string pdfPath, string pageSpec)
    {
        var pages = ParsePageSpec(pageSpec);
        if (pages.Count == 0)
            throw new InvalidOperationException("Enter pages like 1-3,5 or 2.");

        using var input = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        foreach (var p in pages)
        {
            if (p < 1 || p > input.PageCount)
                throw new InvalidOperationException($"Page {p} is out of range (1–{input.PageCount}).");
        }

        var output = SiblingPath(pdfPath, "extract");
        using var outDoc = new PdfDocument();
        foreach (var p in pages)
            outDoc.AddPage(input.Pages[p - 1]);

        outDoc.Save(output);
        AppLogger.Info($"Extracted pages [{string.Join(",", pages)}] → {output}");
        return output;
    }

    /// <summary>Rotate selected pages (or all) by 90/180/270 clockwise. Empty pageSpec = all.</summary>
    public string RotatePages(string pdfPath, int degreesCw, string? pageSpec = null)
    {
        if (degreesCw is not (90 or 180 or 270))
            throw new ArgumentOutOfRangeException(nameof(degreesCw), "Use 90, 180, or 270.");

        using var input = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        var targets = ResolveTargets(pageSpec, input.PageCount);
        var output = SiblingPath(pdfPath, $"rot{degreesCw}");

        using var outDoc = new PdfDocument();
        for (var i = 0; i < input.PageCount; i++)
        {
            var page = outDoc.AddPage(input.Pages[i]);
            if (targets.Contains(i + 1))
                page.Rotate = (page.Rotate + degreesCw) % 360;
        }

        outDoc.Save(output);
        AppLogger.Info($"Rotated pages by {degreesCw}° → {output}");
        return output;
    }

    /// <summary>Light reorder: reverse page order.</summary>
    public string ReversePages(string pdfPath)
    {
        using var input = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        if (input.PageCount < 2)
            throw new InvalidOperationException("Need at least 2 pages to reorder.");

        var output = SiblingPath(pdfPath, "reversed");
        using var outDoc = new PdfDocument();
        for (var i = input.PageCount - 1; i >= 0; i--)
            outDoc.AddPage(input.Pages[i]);

        outDoc.Save(output);
        AppLogger.Info($"Reversed pages → {output}");
        return output;
    }

    /// <summary>
    /// Apply an organize plan: reorder / delete (by omission) / rotate. Writes a new sibling PDF.
    /// </summary>
    public string ApplyPagePlan(string pdfPath, IReadOnlyList<PdfPagePlanItem> plan)
    {
        if (plan is null || plan.Count == 0)
            throw new InvalidOperationException("Keep at least one page.");

        var slots = plan.Select(p => new PdfComposeSlot
        {
            IsImage = false,
            SourcePageIndex = p.SourcePageIndex,
            ExtraRotateDegrees = p.ExtraRotateDegrees
        }).ToList();

        return ComposePages(pdfPath, slots, "organized");
    }

    /// <summary>
    /// Build a new PDF from ordered slots (imported pages and/or image pages).
    /// </summary>
    public string ComposePages(string pdfPath, IReadOnlyList<PdfComposeSlot> slots, string tag = "organized")
    {
        if (slots is null || slots.Count == 0)
            throw new InvalidOperationException("Keep at least one page.");

        using var input = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        foreach (var slot in slots)
        {
            if (slot.IsImage)
            {
                if (string.IsNullOrWhiteSpace(slot.ImagePath) || !IoFile.Exists(slot.ImagePath))
                    throw new FileNotFoundException("Image not found.", slot.ImagePath);
            }
            else
            {
                if (slot.SourcePageIndex < 0 || slot.SourcePageIndex >= input.PageCount)
                    throw new InvalidOperationException($"Page {slot.SourcePageIndex + 1} is out of range (1–{input.PageCount}).");
                if (slot.ExtraRotateDegrees is not (0 or 90 or 180 or 270))
                    throw new InvalidOperationException("Rotation must be 0, 90, 180, or 270.");
            }
        }

        // When every slot is an image, pdfPath may still be used only for SiblingPath.
        var output = SiblingPath(pdfPath, tag);
        using var outDoc = new PdfDocument();
        foreach (var slot in slots)
        {
            if (slot.IsImage)
            {
                AddImagePage(outDoc, slot.ImagePath!);
            }
            else
            {
                var page = outDoc.AddPage(input.Pages[slot.SourcePageIndex]);
                if (slot.ExtraRotateDegrees != 0)
                    page.Rotate = (page.Rotate + slot.ExtraRotateDegrees) % 360;
            }
        }

        outDoc.Save(output);
        AppLogger.Info($"Composed PDF ({slots.Count} pages) → {output}");
        return output;
    }

    /// <summary>
    /// Convert an image to a single PDF page and insert at a 0-based index (or append when null / past end).
    /// </summary>
    public string InsertImageAsPage(string pdfPath, string imagePath, int? insertAtZeroBased = null)
    {
        if (!IoFile.Exists(imagePath))
            throw new FileNotFoundException("Image not found.", imagePath);

        using var input = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        var insertAt = insertAtZeroBased ?? input.PageCount;
        insertAt = Math.Clamp(insertAt, 0, input.PageCount);

        var output = SiblingPath(pdfPath, "imgpage");
        using var outDoc = new PdfDocument();

        for (var i = 0; i < input.PageCount; i++)
        {
            if (i == insertAt)
                AddImagePage(outDoc, imagePath);
            outDoc.AddPage(input.Pages[i]);
        }

        if (insertAt == input.PageCount)
            AddImagePage(outDoc, imagePath);

        outDoc.Save(output);
        AppLogger.Info($"Inserted image page at index {insertAt} → {output}");
        return output;
    }

    /// <summary>
    /// Stamp an image signature on the bottom-right of the last page (or a 1-based page).
    /// Visual stamp only — not a cryptographic / PKCS#7 digital signature.
    /// </summary>
    public const double DefaultStampMaxWidthPt = 160;

    /// <summary>Stamp size in PDF points. Hi-DPI supersampled PNGs still cap at maxWidthPt.</summary>
    public static (double WidthPt, double HeightPt) ComputeStampSizePt(int imageWidthPx, int imageHeightPx, double maxWidthPt = DefaultStampMaxWidthPt)
    {
        if (imageWidthPx <= 0 || imageHeightPx <= 0)
            return (maxWidthPt, maxWidthPt * 0.3);

        // Small images: 1px ≈ 1pt. Large (supersampled) images: scale down to maxWidthPt.
        var scale = imageWidthPx <= maxWidthPt ? 1.0 : maxWidthPt / imageWidthPx;
        return (imageWidthPx * scale, imageHeightPx * scale);
    }

    /// <summary>Extract word bounding boxes for a page (native text layer — not OCR of scans).</summary>
    public static IReadOnlyList<PdfTextBox> GetTextBoxes(string pdfPath, int pageNumber, int maxWords = 400)
    {
        var result = new List<PdfTextBox>();
        if (!IoFile.Exists(pdfPath) || pageNumber < 1)
            return result;

        using var doc = PdfPigDocument.Open(pdfPath);
        if (pageNumber > doc.NumberOfPages)
            return result;

        var page = doc.GetPage(pageNumber);
        foreach (var word in page.GetWords().Take(maxWords))
        {
            var b = word.BoundingBox;
            result.Add(new PdfTextBox(pageNumber, b.BottomLeft.X, b.BottomLeft.Y, b.Width, b.Height, word.Text ?? ""));
        }

        return result;
    }

    /// <summary>Enumerate AcroForm widget rectangles via PdfPig (read-only).</summary>
    public static IReadOnlyList<PdfFormFieldRect> GetFormFieldRects(string pdfPath)
    {
        var result = new List<PdfFormFieldRect>();
        if (!IoFile.Exists(pdfPath))
            return result;

        using var doc = PdfPigDocument.Open(pdfPath);
        if (!doc.TryGetForm(out var form))
            return result;

        for (var page = 1; page <= doc.NumberOfPages; page++)
        {
            foreach (var field in form.GetFieldsForPage(page))
            {
                CollectFieldRects(field, page, result);
            }
        }

        return result;
    }

    private static void CollectFieldRects(AcroFieldBase field, int page, List<PdfFormFieldRect> result)
    {
        if (field is AcroNonTerminalField group)
        {
            foreach (var child in group.Children)
                CollectFieldRects(child, page, result);
            return;
        }

        if (!field.Bounds.HasValue)
            return;

        var b = field.Bounds.Value;
        var name = field.Information?.PartialName;
        result.Add(new PdfFormFieldRect(page, b.BottomLeft.X, b.BottomLeft.Y, b.Width, b.Height, name));
    }

    public string StampSignatureImage(
        string pdfPath,
        string imagePath,
        int? pageNumber = null,
        double maxWidthPt = DefaultStampMaxWidthPt,
        double? xPt = null,
        double? yPtFromBottom = null,
        double? widthPt = null,
        double? heightPt = null)
    {
        if (!IoFile.Exists(imagePath))
            throw new FileNotFoundException("Signature image not found.", imagePath);

        // Modify mode required so XGraphics can draw on the imported page stream.
        using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        if (doc.PageCount == 0)
            throw new InvalidOperationException("PDF has no pages.");

        var idx = (pageNumber ?? doc.PageCount) - 1;
        if (idx < 0 || idx >= doc.PageCount)
            throw new InvalidOperationException($"Page {(pageNumber ?? doc.PageCount)} is out of range (1–{doc.PageCount}).");

        var page = doc.Pages[idx];
        using var img = XImage.FromFile(imagePath);
        double w, h;
        if (widthPt is > 0 && heightPt is > 0)
        {
            w = widthPt.Value;
            h = heightPt.Value;
        }
        else
        {
            (w, h) = ComputeStampSizePt((int)Math.Round(img.PointWidth), (int)Math.Round(img.PointHeight), maxWidthPt);
        }

        var margin = 36.0;
        // XGraphics world space is top-left origin, y down (its WorldToView emits
        // y_pdf = pageHeight - y). Callers pass y measured from the page BOTTOM,
        // so convert here — passing it straight through mirrors the stamp vertically.
        var x = xPt ?? (page.Width - w - margin);
        var yFromBottom = yPtFromBottom ?? margin;
        x = Math.Clamp(x, 0, Math.Max(0, page.Width - w));
        yFromBottom = Math.Clamp(yFromBottom, 0, Math.Max(0, page.Height - h));
        var yTop = page.Height - h - yFromBottom;

        using (var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
            gfx.DrawImage(img, x, yTop, w, h);

        // Always write via temp then replace original so Sign edits the same file (not a sibling).
        var temp = IoPath.Combine(IoPath.GetTempPath(), $"netie_signed_{Guid.NewGuid():N}.pdf");
        doc.Save(temp);
        try
        {
            // Prefer in-place overwrite of the opened document
            IoFile.Copy(temp, pdfPath, overwrite: true);
            try { IoFile.Delete(temp); } catch { /* ignore */ }
            AppLogger.Info($"Stamped signature image → {pdfPath} @ ({x:0},{yFromBottom:0} from bottom) {w:0}x{h:0}pt page {idx + 1}");
            return pdfPath;
        }
        catch (Exception ex)
        {
            // File locked (viewer still holding it) — leave temp for host to swap after unload
            AppLogger.Warn($"In-place stamp deferred (locked): {ex.Message}");
            AppLogger.Info($"Stamped signature image → {temp} (pending replace into {pdfPath})");
            return temp;
        }
    }

    /// <summary>
    /// Bake Fill &amp; Sign typed text into page content (Acrobat-style flatten).
    /// Coordinates arrive in PDF points with origin bottom-left; converted to
    /// XGraphics' top-left space here. Prefers vector text, falls back to a
    /// supersampled transparent PNG when the font cannot be resolved.
    /// </summary>
    public string DrawFillTexts(string pdfPath, IReadOnlyList<PdfFillTextItem> items)
    {
        var valid = (items ?? []).Where(t => !string.IsNullOrWhiteSpace(t.Text)).ToList();
        if (valid.Count == 0)
            return pdfPath;

        using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        foreach (var group in valid.GroupBy(t => t.Page))
        {
            var idx = group.Key - 1;
            if (idx < 0 || idx >= doc.PageCount)
                continue;

            var page = doc.Pages[idx];
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            foreach (var t in group)
            {
                var hPt = Math.Max(6, t.HeightPt);
                var wPt = Math.Max(8, t.WidthPt);
                var fontPt = Math.Clamp(t.FontPt <= 0 ? hPt * 0.58 : t.FontPt, 4, 96);
                var yTop = Math.Clamp(page.Height - t.YFromBottom - hPt, 0, Math.Max(0, page.Height - hPt));
                var rect = new XRect(Math.Clamp(t.XPt, 0, Math.Max(0, page.Width - 8)) + 2, yTop, wPt - 4, hPt);
                if (!TryDrawVectorText(gfx, t.Text, fontPt, rect))
                    DrawTextAsImage(gfx, t.Text, fontPt, rect);
            }
        }

        var temp = IoPath.Combine(IoPath.GetTempPath(), $"netie_fill_{Guid.NewGuid():N}.pdf");
        doc.Save(temp);
        try
        {
            IoFile.Copy(temp, pdfPath, overwrite: true);
            try { IoFile.Delete(temp); } catch { /* ignore */ }
            AppLogger.Info($"Baked {valid.Count} fill text(s) → {pdfPath}");
            return pdfPath;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"In-place fill text deferred (locked): {ex.Message}");
            return temp;
        }
    }

    /// <summary>
    /// Bake Comment highlights / marks into page content (appearance stream via XGraphics Append).
    /// Coordinates arrive in PDF points with origin bottom-left; converted to XGraphics top-left here.
    /// Fail-closed: draws on a temp copy; the original path is only replaced after a successful Save.
    /// If the dest is locked, returns the baked temp so the host can swap after unload.
    /// Never uses AGPL PDF libraries.
    /// </summary>
    public string DrawHighlights(string pdfPath, IReadOnlyList<PdfHighlightItem> items)
    {
        var valid = (items ?? [])
            .Where(h => h.Page >= 1 && h.WidthPt > 0.5 && h.HeightPt > 0.5)
            .ToList();
        if (valid.Count == 0)
            return pdfPath;
        if (!IoFile.Exists(pdfPath))
            throw new FileNotFoundException("PDF not found.", pdfPath);

        var work = IoPath.Combine(IoPath.GetTempPath(), $"netie_hl_work_{Guid.NewGuid():N}.pdf");
        IoFile.Copy(pdfPath, work, overwrite: true);
        try
        {
            using var doc = PdfReader.Open(work, PdfDocumentOpenMode.Modify);
            if (doc.PageCount == 0)
                throw new InvalidOperationException("PDF has no pages.");

            var drawn = 0;
            foreach (var group in valid.GroupBy(h => h.Page))
            {
                var idx = group.Key - 1;
                if (idx < 0 || idx >= doc.PageCount)
                    continue;

                var page = doc.Pages[idx];
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                foreach (var h in group)
                {
                    var w = Math.Max(1, h.WidthPt);
                    var ht = Math.Max(1, h.HeightPt);
                    var x = Math.Clamp(h.XPt, 0, Math.Max(0, page.Width - 1));
                    // Viewer yFromBottom → XGraphics top-left (same conversion as DrawFillTexts).
                    var yTop = Math.Clamp(page.Height - h.YFromBottom - ht, 0, Math.Max(0, page.Height - ht));
                    var isMark = string.Equals(h.Kind, "mark", StringComparison.OrdinalIgnoreCase);
                    var brush = isMark
                        ? new XSolidBrush(XColor.FromArgb(110, 94, 196, 184))
                        : new XSolidBrush(XColor.FromArgb(107, 255, 214, 74));
                    gfx.DrawRectangle(brush, x, yTop, w, ht);
                    drawn++;
                }
            }

            if (drawn == 0)
                return pdfPath;

            var baked = IoPath.Combine(IoPath.GetTempPath(), $"netie_hl_{Guid.NewGuid():N}.pdf");
            doc.Save(baked);
            try
            {
                IoFile.Copy(baked, pdfPath, overwrite: true);
                try { IoFile.Delete(baked); } catch { /* ignore */ }
                AppLogger.Info($"Baked {drawn} highlight(s) → {pdfPath}");
                return pdfPath;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"In-place highlight deferred (locked): {ex.Message}");
                return baked;
            }
        }
        finally
        {
            try { if (IoFile.Exists(work)) IoFile.Delete(work); } catch { /* ignore */ }
        }
    }

    private static readonly XStringFormat FillTextFormat = new()
    {
        Alignment = XStringAlignment.Near,
        LineAlignment = XLineAlignment.Center
    };

    private static bool TryDrawVectorText(XGraphics gfx, string text, double fontPt, XRect rect)
    {
        foreach (var family in new[] { "Segoe UI", "Arial", "Helvetica" })
        {
            try
            {
                var font = new XFont(family, fontPt, XFontStyle.Regular);
                gfx.DrawString(text, font, new XSolidBrush(XColor.FromArgb(255, 26, 26, 26)), rect, FillTextFormat);
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Fill text font '{family}' failed: {ex.Message}");
            }
        }
        return false;
    }

    /// <summary>GDI fallback: render at 4× and place at 1/4 size so text stays sharp.</summary>
    private static void DrawTextAsImage(XGraphics gfx, string text, double fontPt, XRect rect)
    {
        const float ss = 4f;
        var tmp = IoPath.Combine(IoPath.GetTempPath(), $"netie_filltx_{Guid.NewGuid():N}.png");
        try
        {
            using (var font = new Font("Segoe UI", (float)(fontPt * ss), FontStyle.Regular, GraphicsUnit.Pixel))
            {
                SizeF measured;
                using (var mb = new Bitmap(1, 1))
                using (var mg = Graphics.FromImage(mb))
                {
                    mg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    measured = mg.MeasureString(text, font);
                }

                var wPx = Math.Max(4, (int)Math.Ceiling(measured.Width));
                var hPx = Math.Max(4, (int)Math.Ceiling(measured.Height));
                using var bmp = new Bitmap(wPx, hPx, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    using var brush = new SolidBrush(Color.FromArgb(255, 26, 26, 26));
                    g.DrawString(text, font, brush, 0, 0);
                }

                bmp.Save(tmp, ImageFormat.Png);
            }

            using var img = XImage.FromFile(tmp);
            // GraphicsUnit.Pixel at ss× ⇒ 1/ss pt per px puts the em back at fontPt.
            var drawW = Math.Min(rect.Width, img.PixelWidth / ss);
            var drawH = img.PixelHeight / ss;
            var top = rect.Y + Math.Max(0, (rect.Height - drawH) / 2);
            gfx.DrawImage(img, rect.X, top, drawW, drawH);
        }
        finally
        {
            try { if (IoFile.Exists(tmp)) IoFile.Delete(tmp); } catch { /* ignore */ }
        }
    }

    /// <summary>Stamp typed signature; optional placement in PDF points (origin bottom-left).</summary>
    public string StampTypedSignature(
        string pdfPath,
        string name,
        string fontFamily,
        string? savePngPath = null,
        int? pageNumber = null,
        double? xPt = null,
        double? yPtFromBottom = null,
        double? widthPt = null,
        double? heightPt = null)
    {
        var tempPng = IoPath.Combine(IoPath.GetTempPath(), $"netie_sig_{Guid.NewGuid():N}.png");
        try
        {
            RenderTypedSignaturePng(name, fontFamily, tempPng);
            if (!string.IsNullOrWhiteSpace(savePngPath))
                IoFile.Copy(tempPng, savePngPath, overwrite: true);

            return StampSignatureImage(pdfPath, tempPng, pageNumber, DefaultStampMaxWidthPt, xPt, yPtFromBottom, widthPt, heightPt);
        }
        finally
        {
            try { if (IoFile.Exists(tempPng)) IoFile.Delete(tempPng); } catch { /* ignore */ }
        }
    }

    /// <summary>Installed script-like fonts suitable for typed signatures (2–4 preferred, always ≥1).</summary>
    public static IReadOnlyList<string> GetAvailableSignatureFonts()
    {
        using var installed = new InstalledFontCollection();
        var names = new HashSet<string>(
            installed.Families.Select(f => f.Name),
            StringComparer.OrdinalIgnoreCase);

        var found = SignatureFontCandidates.Where(names.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (found.Count == 0)
            found.Add("Segoe UI");

        // Prefer up to 4 script fonts; keep Segoe UI only as last-resort filler.
        var script = found.Where(f => !f.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase)).Take(4).ToList();
        if (script.Count >= 2)
            return script;
        if (script.Count == 1)
        {
            script.Add(found.First(f => !f.Equals(script[0], StringComparison.OrdinalIgnoreCase)));
            return script;
        }

        return found.Take(4).ToList();
    }

    /// <summary>Render a typed name to a transparent PNG. Returns the file path written.
    /// Default em is 288px (3×) so stamps stay sharp after PDF embed at Fit/DPR scale.</summary>
    public static string RenderTypedSignaturePng(string name, string fontFamily, string outputPath, float emSizePx = 288f)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Type a name to sign.");

        var text = name.Trim();
        var family = string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily.Trim();
        emSizePx = Math.Clamp(emSizePx, 96f, 384f);

        using var font = CreateSignatureFont(family, emSizePx);
        var padding = (int)Math.Ceiling(emSizePx * 0.35);
        SizeF measured;
        using (var measureBmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(measureBmp))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            measured = g.MeasureString(text, font);
        }

        var width = Math.Max(8, (int)Math.Ceiling(measured.Width) + padding * 2);
        var height = Math.Max(8, (int)Math.Ceiling(measured.Height) + padding * 2);

        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var brush = new SolidBrush(Color.FromArgb(255, 18, 28, 48));
            g.DrawString(text, font, brush, padding, padding * 0.4f);
        }

        var dir = IoPath.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            IoDirectory.CreateDirectory(dir);

        bmp.Save(outputPath, ImageFormat.Png);
        return outputPath;
    }

    public int GetPageCount(string pdfPath)
    {
        using var input = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        return input.PageCount;
    }

    /// <summary>Existing page Rotate values (degrees) for each page, length = page count.</summary>
    public int[] GetPageRotations(string pdfPath)
    {
        using var input = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        var result = new int[input.PageCount];
        for (var i = 0; i < input.PageCount; i++)
            result[i] = input.Pages[i].Rotate;
        return result;
    }

    private static void AddImagePage(PdfDocument outDoc, string imagePath)
    {
        using var img = XImage.FromFile(imagePath);
        // Fit into a reasonable page: keep aspect, cap longest side ~ Letter width.
        const double maxPt = 612; // US Letter width
        var scale = Math.Min(1.0, maxPt / Math.Max(img.PointWidth, img.PointHeight));
        var w = img.PointWidth * scale;
        var h = img.PointHeight * scale;

        var page = outDoc.AddPage();
        page.Width = w;
        page.Height = h;
        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawImage(img, 0, 0, w, h);
    }

    private static Font CreateSignatureFont(string family, float emSizePx)
    {
        try
        {
            return new Font(family, emSizePx, FontStyle.Regular, GraphicsUnit.Pixel);
        }
        catch
        {
            return new Font("Segoe UI", emSizePx, FontStyle.Italic, GraphicsUnit.Pixel);
        }
    }

    private static void AppendFile(PdfDocument outDoc, string path)
    {
        using var input = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        for (var i = 0; i < input.PageCount; i++)
            outDoc.AddPage(input.Pages[i]);
    }

    private static string SiblingPath(string pdfPath, string tag)
    {
        var dir = IoPath.GetDirectoryName(pdfPath)!;
        var name = IoPath.GetFileNameWithoutExtension(pdfPath);
        return IoPath.Combine(dir, $"{name}_{tag}_{DateTime.Now:HHmmss}.pdf");
    }

    private static HashSet<int> ResolveTargets(string? pageSpec, int pageCount)
    {
        if (string.IsNullOrWhiteSpace(pageSpec))
            return Enumerable.Range(1, pageCount).ToHashSet();

        var pages = ParsePageSpec(pageSpec);
        if (pages.Count == 0)
            throw new InvalidOperationException("Enter pages like 1-3,5 or leave blank for all.");

        foreach (var p in pages)
        {
            if (p < 1 || p > pageCount)
                throw new InvalidOperationException($"Page {p} is out of range (1–{pageCount}).");
        }

        return pages.ToHashSet();
    }

    /// <summary>Parse "1-3,5,7" into ordered distinct 1-based page numbers.</summary>
    public static List<int> ParsePageSpec(string spec)
    {
        var result = new List<int>();
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Contains('-', StringComparison.Ordinal))
            {
                var bits = part.Split('-', StringSplitOptions.TrimEntries);
                if (bits.Length != 2 || !int.TryParse(bits[0], out var a) || !int.TryParse(bits[1], out var b))
                    throw new InvalidOperationException($"Invalid range: {part}");
                if (a > b) (a, b) = (b, a);
                for (var i = a; i <= b; i++)
                    result.Add(i);
            }
            else
            {
                if (!int.TryParse(part, out var n))
                    throw new InvalidOperationException($"Invalid page: {part}");
                result.Add(n);
            }
        }

        return result.Distinct().OrderBy(x => x).ToList();
    }
}
