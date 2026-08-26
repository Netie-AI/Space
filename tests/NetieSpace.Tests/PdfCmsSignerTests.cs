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
}
