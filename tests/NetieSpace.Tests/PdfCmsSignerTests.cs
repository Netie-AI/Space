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
    public void Sign_self_verifies_detached_cms()
    {
        var src = PdfCmsSigner.CreateMinimalPdf();
        using var cert = PdfCmsSigner.CreateEphemeralSelfSigned();
        var signed = PdfCmsSigner.Sign(src, cert);
        Assert.True(PdfCmsSigner.TryVerify(signed, out var err), err);
        Assert.False(PdfCmsSigner.TryVerify(src, out _));
    }
}
