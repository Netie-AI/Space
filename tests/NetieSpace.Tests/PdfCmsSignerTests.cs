using System.Security.Cryptography.X509Certificates;
using NetieSpace.Services;

namespace NetieSpace.Tests;

/// <summary>
/// PKCS#7 detached incremental sign. Adobe Reader check is still human.
/// </summary>
public class PdfCmsSignerTests
{
    [Fact]
    public void Minimal_pdf_is_classic_xref()
    {
        var pdf = PdfCmsSigner.CreateMinimalPdf();
        var text = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("startxref", text);
        Assert.Contains("xref", text);
        Assert.DoesNotContain("iText", text);
    }

    [Fact]
    public void TryReadObject_does_not_match_12_as_2()
    {
        var src = PdfCmsSigner.CreateMinimalPdf();
        var header = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n");
        var fake = System.Text.Encoding.ASCII.GetBytes("12 0 obj<< /Type /Fake >>endobj\n");
        var mixed = new byte[src.Length + fake.Length];
        Buffer.BlockCopy(src, 0, mixed, 0, header.Length);
        Buffer.BlockCopy(fake, 0, mixed, header.Length, fake.Length);
        Buffer.BlockCopy(src, header.Length, mixed, header.Length + fake.Length, src.Length - header.Length);

        var obj2 = PdfCmsSigner.TryReadObject(mixed, 2);
        Assert.NotNull(obj2);
        Assert.Contains("/Type /Pages", obj2);
        Assert.DoesNotContain("/Type /Fake", obj2);
        var obj12 = PdfCmsSigner.TryReadObject(mixed, 12);
        Assert.NotNull(obj12);
        Assert.Contains("/Type /Fake", obj12);
        Assert.Contains("/Type /Page ", PdfCmsSigner.TryReadObject(src, 3));
    }

    [Fact]
    public void TryFirstPageRef_resolves_indirect_kids_array()
    {
        Assert.Equal(3, PdfCmsSigner.TryFirstPageRef(PdfCmsSigner.CreateMinimalPdf(), 1));
        var indirect = PdfCmsSigner.CreateMinimalPdfWithIndirectKids();
        Assert.Contains("/Kids 5 0 R", System.Text.Encoding.ASCII.GetString(indirect));
        Assert.Equal(3, PdfCmsSigner.TryFirstPageRef(indirect, 1));
    }

    [Fact]
    public void TryFirstPageRef_resolves_compact_kids_and_type()
    {
        var compact = PdfCmsSigner.CreateMinimalPdfCompactNames();
        var text = System.Text.Encoding.ASCII.GetString(compact);
        Assert.Contains("/Kids[3 0 R]", text);
        Assert.Contains("/Type/Page", text);
        Assert.DoesNotContain("/Kids [", text);
        Assert.DoesNotContain("/Type /Page", text);
        Assert.Equal(3, PdfCmsSigner.TryFirstPageRef(compact, 1));
    }

    [Fact]
    public void Sign_embeds_detached_pkcs7_and_keeps_hello()
    {
        var src = PdfCmsSigner.CreateMinimalPdf();
        using var cert = PdfCmsSigner.CreateEphemeralSelfSigned();
        var signed = PdfCmsSigner.Sign(src, cert);

        Assert.True(signed.Length > src.Length);
        Assert.True(PdfCmsSigner.HasDetachedPkcs7Marker(signed));
        var text = System.Text.Encoding.ASCII.GetString(signed);
        Assert.Contains("Hello Netie", text);
        Assert.Contains("/ByteRange [0 ", text);
        Assert.DoesNotContain("**********", text);
        Assert.Contains("/Contents <", text);
        Assert.Contains("/Annots [", text);
        Assert.Contains("/AP << /N ", text);
        Assert.Contains("/Subtype /Form", text);
        Assert.DoesNotContain("iText", text);
    }

    [Fact]
    public void SignFile_writes_marker_in_place()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netie-cms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "doc.pdf");
        File.WriteAllBytes(path, PdfCmsSigner.CreateMinimalPdf());
        using var cert = PdfCmsSigner.CreateEphemeralSelfSigned();
        PdfCmsSigner.SignFile(path, cert);
        Assert.True(PdfCmsSigner.HasDetachedPkcs7Marker(File.ReadAllBytes(path)));
        try { Directory.Delete(dir, true); } catch { /* temp */ }
    }

    [Fact]
    public void NeedsClassicXrefRewrite_detects_missing_trailer()
    {
        var ok = PdfCmsSigner.CreateMinimalPdf();
        Assert.False(PdfCmsSigner.NeedsClassicXrefRewrite(ok));
        var streamy = System.Text.Encoding.ASCII.GetBytes(
            "%PDF-1.5\n1 0 obj<< /Type /XRef >>endobj\nstartxref\n9\n%%EOF\n");
        Assert.True(PdfCmsSigner.NeedsClassicXrefRewrite(streamy));
    }

    [Fact]
    public void NeedsClassicXrefRewrite_detects_hybrid_trailer_plus_xref_stream()
    {
        var hybrid = System.Text.Encoding.ASCII.GetBytes(
            "%PDF-1.5\n1 0 obj<< /Type /Catalog >>endobj\n" +
            "trailer<< /Size 2 /Root 1 0 R >>\n" +
            "2 0 obj<< /Type /XRef /W [1 2 1] >>endobj\nstartxref\n80\n%%EOF\n");
        Assert.True(PdfCmsSigner.NeedsClassicXrefRewrite(hybrid));
        Assert.False(PdfCmsSigner.NeedsClassicXrefRewrite(PdfCmsSigner.CreateMinimalPdf()));
    }

    [Fact]
    public void Sign_self_verifies_detached_cms()
    {
        var src = PdfCmsSigner.CreateMinimalPdf();
        using var cert = PdfCmsSigner.CreateEphemeralSelfSigned();
        var signed = PdfCmsSigner.Sign(src, cert);
        Assert.True(PdfCmsSigner.TryVerify(signed, out var err), err);
        Assert.False(PdfCmsSigner.TryVerify(src, out _));
    }

    [Fact]
    public void Sign_refuses_hybrid_trailer_plus_xref_stream()
    {
        // Classic xref keyword plus /Type /XRef -- startxref contains "xref" so the
        // old substring check would accept this and Adobe would miss the signature.
        var hybrid = System.Text.Encoding.ASCII.GetBytes(
            "%PDF-1.5\n1 0 obj<< /Type /Catalog /Pages 2 0 R >>endobj\n" +
            "2 0 obj<< /Type /Pages /Kids [3 0 R] /Count 1 >>endobj\n" +
            "3 0 obj<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] >>endobj\n" +
            "xref\n0 4\n0000000000 65535 f \n0000000009 00000 n \n" +
            "0000000068 00000 n \n0000000124 00000 n \n" +
            "trailer<< /Size 4 /Root 1 0 R >>\n" +
            "4 0 obj<< /Type /XRef /W [1 2 1] >>endobj\nstartxref\n180\n%%EOF\n");
        Assert.True(PdfCmsSigner.NeedsClassicXrefRewrite(hybrid));
        using var cert = PdfCmsSigner.CreateEphemeralSelfSigned();
        var ex = Assert.Throws<InvalidOperationException>(() => PdfCmsSigner.Sign(hybrid, cert));
        Assert.Contains("classic xref", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sign_refuses_catalog_without_pages_ref()
    {
        var body = string.Join('\n',
            "%PDF-1.4",
            "1 0 obj<< /Type /Catalog >>endobj",
            "2 0 obj<< /Type /Pages /Kids [3 0 R] /Count 1 >>endobj",
            "3 0 obj<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] >>endobj",
            "");
        var bodyBytes = System.Text.Encoding.ASCII.GetBytes(body);
        var tail = "xref\n0 4\n0000000000 65535 f \n0000000009 00000 n \n" +
                   "0000000055 00000 n \n0000000111 00000 n \n" +
                   "trailer<< /Size 4 /Root 1 0 R >>\nstartxref\n" + bodyBytes.Length + "\n%%EOF\n";
        var pdf = new byte[bodyBytes.Length + System.Text.Encoding.ASCII.GetByteCount(tail)];
        Buffer.BlockCopy(bodyBytes, 0, pdf, 0, bodyBytes.Length);
        System.Text.Encoding.ASCII.GetBytes(tail, 0, tail.Length, pdf, bodyBytes.Length);
        Assert.False(PdfCmsSigner.NeedsClassicXrefRewrite(pdf));
        using var cert = PdfCmsSigner.CreateEphemeralSelfSigned();
        var ex = Assert.Throws<InvalidOperationException>(() => PdfCmsSigner.Sign(pdf, cert));
        Assert.Contains("/Pages", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sign_resolves_indirect_kids_onto_first_page()
    {
        var src = PdfCmsSigner.CreateMinimalPdfWithIndirectKids();
        using var cert = PdfCmsSigner.CreateEphemeralSelfSigned();
        var signed = PdfCmsSigner.Sign(src, cert);
        Assert.True(PdfCmsSigner.TryVerify(signed, out var err), err);
        var text = System.Text.Encoding.ASCII.GetString(signed);
        Assert.Contains("/Type /Sig", text);
        Assert.Contains("/P 3 0 R", text);
        Assert.Contains("/Annots [", text);
        Assert.DoesNotContain("/Rect [0 0 0 0]", text);
    }

    [Fact]
    public void Sign_compact_names_stamps_first_page()
    {
        var src = PdfCmsSigner.CreateMinimalPdfCompactNames();
        using var cert = PdfCmsSigner.CreateEphemeralSelfSigned();
        var signed = PdfCmsSigner.Sign(src, cert);
        Assert.True(PdfCmsSigner.TryVerify(signed, out var err), err);
        var text = System.Text.Encoding.ASCII.GetString(signed);
        Assert.Contains("/P 3 0 R", text);
        Assert.Contains("/Annots [", text);
        Assert.Contains("Digitally signed", text);
        Assert.DoesNotContain("/Rect [0 0 0 0]", text);
    }

    [Theory]
    [InlineData("sample.pdf", 4)]
    [InlineData("Q3_Report.pdf", 4)]
    public void Sign_in_app_pdf_stamps_page_4(string fileName, int pageId)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        Assert.True(File.Exists(fixture), fixture);
        var src = File.ReadAllBytes(fixture);
        Assert.False(PdfCmsSigner.NeedsClassicXrefRewrite(src));
        var raw = System.Text.Encoding.ASCII.GetString(src);
        Assert.Contains("/Kids[4 0 R]", raw);
        Assert.Contains("/Type/Page", raw);
        Assert.Equal(pageId, PdfCmsSigner.TryFirstPageRef(src, 1));

        using var cert = PdfCmsSigner.CreateEphemeralSelfSigned();
        var signed = PdfCmsSigner.Sign(src, cert);
        Assert.True(PdfCmsSigner.TryVerify(signed, out var err), err);
        var text = System.Text.Encoding.ASCII.GetString(signed);
        Assert.Contains($"/P {pageId} 0 R", text);
        Assert.Contains("/Annots [", text);
        Assert.Contains("Digitally signed", text);
        Assert.DoesNotContain("/Rect [0 0 0 0]", text);
    }

    [Fact]
    public void Sign_inlines_indirect_annots_array_onto_first_page()
    {
        var src = PdfCmsSigner.CreateMinimalPdfWithIndirectAnnots();
        Assert.Contains("/Annots 5 0 R", System.Text.Encoding.ASCII.GetString(src));
        using var cert = PdfCmsSigner.CreateEphemeralSelfSigned();
        var signed = PdfCmsSigner.Sign(src, cert);
        Assert.True(PdfCmsSigner.TryVerify(signed, out var err), err);
        var text = System.Text.Encoding.ASCII.GetString(signed);
        var lastPage = text.LastIndexOf("3 0 obj", StringComparison.Ordinal);
        Assert.True(lastPage >= 0);
        var pageDict = text[lastPage..];
        var end = pageDict.IndexOf("endobj", StringComparison.Ordinal);
        if (end > 0)
            pageDict = pageDict[..end];
        Assert.Contains("/Annots [", pageDict, StringComparison.Ordinal);
        Assert.Contains("6 0 R", pageDict, StringComparison.Ordinal);
        Assert.DoesNotContain("5 0 R", pageDict, StringComparison.Ordinal);
        Assert.DoesNotContain("/Rect [0 0 0 0]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RewriteFile_noop_on_classic_minimal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netie-xref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "classic.pdf");
        File.WriteAllBytes(path, PdfCmsSigner.CreateMinimalPdf());
        try
        {
            Assert.False(PdfClassicXref.RewriteFile(path));
            Assert.False(PdfCmsSigner.NeedsClassicXrefRewrite(File.ReadAllBytes(path)));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void RewriteFile_strips_hybrid_marker_then_sign_verifies()
    {
        // PdfSharp Open fails on this shape ("no trailer"). Strip to last classic %%EOF.
        var dir = Path.Combine(Path.GetTempPath(), "netie-xref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "hybrid.pdf");
        var hybrid = HybridAfterEof();
        File.WriteAllBytes(path, hybrid);
        Assert.True(PdfCmsSigner.NeedsClassicXrefRewrite(hybrid));
        try
        {
            Assert.True(PdfClassicXref.TryStripToClassic(hybrid, out var stripped));
            Assert.True(stripped.Length < hybrid.Length);
            Assert.True(PdfClassicXref.RewriteFile(path));
            var after = File.ReadAllBytes(path);
            Assert.False(PdfCmsSigner.NeedsClassicXrefRewrite(after));
            using var cert = PdfCmsSigner.CreateEphemeralSelfSigned();
            var signed = PdfCmsSigner.Sign(after, cert);
            Assert.True(PdfCmsSigner.TryVerify(signed, out var err), err);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void RewriteFile_strips_incremental_xref_stream_then_sign_verifies()
    {
        // Chrome/Word: classic increment, then a second startxref/%%EOF with /Type /XRef.
        var dir = Path.Combine(Path.GetTempPath(), "netie-xref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "chrome-hybrid.pdf");
        var hybrid = IncrementalXrefStream();
        File.WriteAllBytes(path, hybrid);
        Assert.True(PdfCmsSigner.NeedsClassicXrefRewrite(hybrid));
        try
        {
            Assert.True(PdfClassicXref.RewriteFile(path));
            var after = File.ReadAllBytes(path);
            Assert.False(PdfCmsSigner.NeedsClassicXrefRewrite(after));
            Assert.Contains("Hello Netie", System.Text.Encoding.ASCII.GetString(after));
            using var cert = PdfCmsSigner.CreateEphemeralSelfSigned();
            Assert.True(PdfCmsSigner.TryVerify(PdfCmsSigner.Sign(after, cert), out var err), err);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void RewriteFile_first_gen_xref_stream_then_sign_verifies()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "xrefstream-mupdf.pdf");
        Assert.True(File.Exists(fixture), fixture);
        var src = File.ReadAllBytes(fixture);
        Assert.True(PdfCmsSigner.NeedsClassicXrefRewrite(src));
        Assert.False(PdfClassicXref.TryStripToClassic(src, out _));

        var dir = Path.Combine(Path.GetTempPath(), "netie-xref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "chrome-save.pdf");
        File.Copy(fixture, path);
        try
        {
            Assert.True(PdfClassicXref.RewriteFile(path));
            var after = File.ReadAllBytes(path);
            Assert.False(PdfCmsSigner.NeedsClassicXrefRewrite(after));
            Assert.Contains("/Type /Page", System.Text.Encoding.ASCII.GetString(after));
            using var cert = PdfCmsSigner.CreateEphemeralSelfSigned();
            Assert.True(PdfCmsSigner.TryVerify(PdfCmsSigner.Sign(after, cert), out var err), err);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void Sign_after_disk_pfx_load_matches_ui()
    {
        // Playbook: make-test-pfx.ps1 then More... Cert sign (PFX)...
        // UI used to new X509Certificate2(..., Exportable) only.
        var dir = Path.Combine(Path.GetTempPath(), "netie-pfx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var minted = PdfCmsSigner.CreateEphemeralSelfSigned();
            var pfxPath = Path.Combine(dir, "netie-test.pfx");
            File.WriteAllBytes(pfxPath, minted.Export(X509ContentType.Pfx, "test"));
            using var ui = PdfCmsSigner.LoadPfx(pfxPath, "test");
            Assert.NotNull(ui.GetRSAPrivateKey());

            var sample = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.pdf");
            Assert.True(File.Exists(sample), sample);
            var pdfPath = Path.Combine(dir, "sample.pdf");
            File.Copy(sample, pdfPath);
            if (PdfCmsSigner.NeedsClassicXrefRewrite(File.ReadAllBytes(pdfPath)))
                Assert.True(PdfClassicXref.RewriteFile(pdfPath));
            PdfCmsSigner.SignFile(pdfPath, ui);
            Assert.True(PdfCmsSigner.TryVerify(File.ReadAllBytes(pdfPath), out var err), err);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void PdfSharp_new_document_is_classic_or_rewritable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "netie-xref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "pdfsharp.pdf");
        try
        {
            using (var doc = new PdfSharpCore.Pdf.PdfDocument())
            {
                doc.AddPage();
                doc.Save(path);
            }

            var bytes = File.ReadAllBytes(path);
            if (PdfCmsSigner.NeedsClassicXrefRewrite(bytes))
                Assert.True(PdfClassicXref.RewriteFile(path));
            var after = File.ReadAllBytes(path);
            Assert.False(PdfCmsSigner.NeedsClassicXrefRewrite(after));
            using var cert = PdfCmsSigner.CreateEphemeralSelfSigned();
            Assert.True(PdfCmsSigner.TryVerify(PdfCmsSigner.Sign(after, cert), out var err), err);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp */ }
        }
    }

    private static byte[] HybridAfterEof()
    {
        var classic = PdfCmsSigner.CreateMinimalPdf();
        var extra = System.Text.Encoding.ASCII.GetBytes("\n8 0 obj<< /Type /XRef /W [1 2 1] >>endobj\n");
        var hybrid = new byte[classic.Length + extra.Length];
        Buffer.BlockCopy(classic, 0, hybrid, 0, classic.Length);
        Buffer.BlockCopy(extra, 0, hybrid, classic.Length, extra.Length);
        return hybrid;
    }

    private static byte[] IncrementalXrefStream()
    {
        var classic = PdfCmsSigner.CreateMinimalPdf();
        var tail = "\n8 0 obj<< /Type /XRef /W [1 2 1] /Size 5 >>stream\n\x00\x00\x00\nendstream\nendobj\nstartxref\n" +
                   classic.Length + "\n%%EOF\n";
        var extra = System.Text.Encoding.ASCII.GetBytes(tail);
        var hybrid = new byte[classic.Length + extra.Length];
        Buffer.BlockCopy(classic, 0, hybrid, 0, classic.Length);
        Buffer.BlockCopy(extra, 0, hybrid, classic.Length, extra.Length);
        return hybrid;
    }
}
