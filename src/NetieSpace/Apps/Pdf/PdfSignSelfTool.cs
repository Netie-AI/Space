using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using NetieSpace.Services;
using PdfSharpCore.Drawing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using static NetieSpace.Apps.Pdf.PdfToolSupport;
using Pig = UglyToad.PdfPig;

namespace NetieSpace.Apps.Pdf;

/// <summary>
/// pdf-sign-self: a visible signature mark (typed name or picture, optional date line) on chosen pages, drawn with
/// PdfSharpCore, then optionally a PKCS#7 detached signature over the finished file (PdfCmsSigner, in-box SignedCms).
/// The certificate signature covers the whole file and carries a small signature field on page 1.
/// </summary>
internal static class PdfSignSelfTool
{
    private const double Edge = 24;

    public static Task<IReadOnlyList<string>> RunAsync(ToolRequest req, IProgress<ToolProgress>? progress, CancellationToken ct) =>
        Work(ct, () =>
        {
            var mode = OptChoice(req, "mode", "name", "name", "image");
            var (vert, horiz) = PdfEditTools.ParseGrid(Opt(req, "position", "bottom-right"), "position", allowMiddle: true);
            var sizeName = OptChoice(req, "size", "medium", "small", "medium", "large");
            var widthFraction = sizeName switch { "small" => 0.16, "large" => 0.36, _ => 0.25 };
            var withDate = OptBool(req, "date", false);
            var spec = Opt(req, "pages", "end");

            var name = Opt(req, "name").Trim();
            if (mode == "name" && name.Length == 0)
                throw new ArgumentException("option 'name': the name to sign with is empty");
            RequireLatin1(name, "name");

            byte[]? imageBytes = null;
            if (mode == "image")
            {
                var imgPath = Opt(req, "image");
                if (imgPath.Length == 0) throw new ArgumentException("option 'image': give a png or jpg file for mode=image");
                if (!IoFile.Exists(imgPath)) throw new ArgumentException("option 'image': file not found: " + imgPath);
                imageBytes = LoadSignatureImage(imgPath);
            }

            var certPath = Opt(req, "cert");
            var certPassword = Opt(req, "cert_password");
            X509Certificate2? cert = null;
            if (certPath.Length > 0)
            {
                if (!IoFile.Exists(certPath)) throw new ArgumentException("option 'cert': file not found: " + certPath);
                try { cert = PdfCmsSigner.LoadPfx(certPath, certPassword); }
                catch (Exception)
                {
                    // The reader's message is not passed on: it can quote the file path and say nothing useful.
                    throw new ArgumentException("option 'cert': the certificate file could not be opened (not a PFX, or the password is wrong)");
                }
                if (cert.GetRSAPrivateKey() is null)
                {
                    cert.Dispose();
                    throw new ArgumentException("option 'cert': the certificate has no RSA private key");
                }
            }
            else if (certPassword.Length > 0)
                throw new ArgumentException("option 'cert_password': given without a certificate file in option 'cert'");

            try
            {
                var dateText = withDate ? DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
                var names = new OutputNamer(req.OutDir);
                var outputs = new List<string>();
                var input = req.Files[0];
                string? path = null;
                try
                {
                    int pageCount;
                    List<(double W, double H)> sizes;
                    List<int> targets;
                    if (Encoding.Latin1.GetString(IoFile.ReadAllBytes(input)).Contains("/ByteRange", StringComparison.Ordinal))
                        throw new InvalidOperationException("the file already carries a digital signature; rewriting it would invalidate that signature");
                    using (var doc = OpenModify(input))
                    {
                        pageCount = doc.PageCount;
                        sizes = Enumerable.Range(0, pageCount).Select(i => VisualSize(doc.Pages[i])).ToList();
                        targets = ParsePages(spec, pageCount, "pages").Distinct().ToList();
                        var done = 0;
                        foreach (var p in targets)
                        {
                            ct.ThrowIfCancellationRequested();
                            using var gfx = OpenVisualGraphics(doc.Pages[p - 1], append: true, out var vw, out var vh);
                            DrawMark(gfx, vw, vh, mode, name, imageBytes, dateText, widthFraction, vert, horiz);
                            Report(progress, ++done, targets.Count, "signed page " + p);
                        }
                        path = names.Next(Stem(input) + "_signed", ".pdf");
                        doc.Save(path);
                    }

                    if (cert is not null)
                        PdfCmsSigner.SignFile(path, cert);

                    ReadBack(input, path, pageCount, sizes, cert is not null);
                    outputs.Add(path);
                }
                catch
                {
                    try { if (path is not null && IoFile.Exists(path)) IoFile.Delete(path); } catch { /* best effort */ }
                    throw;
                }
                return (IReadOnlyList<string>)outputs;
            }
            finally
            {
                cert?.Dispose();
            }
        });

    private static void DrawMark(XGraphics gfx, double vw, double vh, string mode, string name, byte[]? imageBytes,
        string dateText, double widthFraction, string vert, string horiz)
    {
        var maxW = vw * widthFraction;
        XFont? nameFont = null, dateFont = null;
        XImage? img = null;
        double nameW = 0, nameH = 0, dateW = 0, dateH = 0;
        double imgW = 0, imgH = 0;
        try
        {
            if (mode == "name")
            {
                var fs = 40.0;
                nameFont = ResolveFont(fs, XFontStyle.Italic);
                nameW = gfx.MeasureString(name, nameFont).Width;
                if (nameW > maxW)
                {
                    fs = Math.Max(6, fs * maxW / nameW);
                    nameFont = ResolveFont(fs, XFontStyle.Italic);
                    nameW = gfx.MeasureString(name, nameFont).Width;
                }
                nameH = nameFont.GetHeight();
                if (nameW > vw - 2 * Edge)
                    throw new ArgumentException("option 'name': the name is too long to fit across the page");
            }
            else
            {
                img = XImage.FromStream(() => new MemoryStream(imageBytes!));
                imgW = maxW;
                imgH = imgW * img.PixelHeight / Math.Max(1, img.PixelWidth);
                var capH = vh * 0.2;
                if (imgH > capH) { imgH = capH; imgW = imgH * img.PixelWidth / Math.Max(1, img.PixelHeight); }
                if (name.Length > 0)
                {
                    nameFont = ResolveFont(Math.Max(6, Math.Min(11, maxW / Math.Max(1, name.Length) * 1.6)), XFontStyle.Regular);
                    nameW = Math.Min(gfx.MeasureString(name, nameFont).Width, imgW);
                    nameH = nameFont.GetHeight();
                }
            }
            if (dateText.Length > 0)
            {
                var baseSize = nameFont?.Size ?? 11;
                dateFont = ResolveFont(Math.Max(6, Math.Min(11, baseSize * 0.5 + 3)), XFontStyle.Regular);
                dateW = gfx.MeasureString(dateText, dateFont).Width;
                dateH = dateFont.GetHeight();
            }

            var blockW = Math.Max(Math.Max(nameW, dateW), imgW);
            var blockH = imgH + nameH + dateH;
            var left = horiz switch { "left" => Edge, "right" => vw - Edge - blockW, _ => (vw - blockW) / 2 };
            var top = vert switch { "top" => Edge, "bottom" => vh - Edge - blockH, _ => (vh - blockH) / 2 };
            double X(double w) => horiz switch { "left" => left, "right" => left + blockW - w, _ => left + (blockW - w) / 2 };

            var y = top;
            if (img is not null)
            {
                gfx.DrawImage(img, X(imgW), y, imgW, imgH);
                y += imgH;
            }
            if (nameFont is not null)
            {
                var ink = new XSolidBrush(XColor.FromArgb(255, 20, 40, 120));
                gfx.DrawString(name, nameFont, mode == "name" ? ink : XBrushes.Black, new XPoint(X(nameW), y + nameFont.GetHeight() * 0.8));
                y += nameH;
            }
            if (dateFont is not null)
                gfx.DrawString(dateText, dateFont, XBrushes.Black, new XPoint(X(dateW), y + dateFont.GetHeight() * 0.8));
        }
        finally
        {
            img?.Dispose();
        }
    }

    private static void RequireLatin1(string text, string key)
    {
        // The default PDF font encoding is WinAnsi: refuse what it cannot draw, as pdf-watermark does.
        foreach (var ch in text)
            if (!((ch >= ' ' && ch <= '~') || (ch >= '\u00A0' && ch <= '\u00FF')))
                throw new ArgumentException($"option '{key}': the character U+{(int)ch:X4} cannot be drawn (only Latin letters, digits and punctuation up to U+00FF are supported)");
    }

    private static byte[] LoadSignatureImage(string path)
    {
        var ext = IoPath.GetExtension(path).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg"))
            throw new ArgumentException("option 'image': only png and jpg pictures are accepted, got '" + ext + "'");
        try
        {
            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(path);
            using var ms = new MemoryStream();
            image.Save(ms, new PngEncoder());
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"option 'image': cannot decode {path}: {ex.Message}");
        }
    }

    /// <summary>Page count, page sizes and the text of the input must survive; a certificate signature must verify.</summary>
    private static void ReadBack(string input, string output, int pageCount, List<(double W, double H)> sizes, bool signed)
    {
        try
        {
            using var sharp = OpenImport(output);
            if (sharp.PageCount != pageCount)
                throw new InvalidOperationException($"the signed copy has {sharp.PageCount} page(s), expected {pageCount}");
            for (var i = 0; i < pageCount; i++)
            {
                var (w, h) = VisualSize(sharp.Pages[i]);
                if (Math.Abs(w - sizes[i].W) > 0.5 || Math.Abs(h - sizes[i].H) > 0.5)
                    throw new InvalidOperationException($"page {i + 1} changed size in the signed copy");
            }
            using var before = Pig.PdfDocument.Open(input);
            using var after = Pig.PdfDocument.Open(output);
            if (after.NumberOfPages != pageCount)
                throw new InvalidOperationException($"the signed copy has {after.NumberOfPages} page(s), expected {pageCount}");
            for (var i = 1; i <= pageCount; i++)
            {
                var original = before.GetPage(i).Text.Replace(" ", "").Replace("\n", "");
                if (original.Length > 0 && !after.GetPage(i).Text.Replace(" ", "").Replace("\n", "").Contains(original))
                    throw new InvalidOperationException($"the text of page {i} is no longer extractable in the signed copy");
            }
            if (signed)
            {
                var bytes = IoFile.ReadAllBytes(output);
                if (!PdfCmsSigner.TryVerify(bytes, out var err))
                    throw new InvalidOperationException("the certificate signature does not verify: " + err);
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("the signed copy") || ex.Message.StartsWith("the text of page") || ex.Message.StartsWith("page ") || ex.Message.StartsWith("the certificate signature"))
        {
            throw new InvalidOperationException(ex.Message + "; the signed copy was discarded", ex);
        }
    }
}
