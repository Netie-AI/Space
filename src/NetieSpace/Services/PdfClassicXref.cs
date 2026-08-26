using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace NetieSpace.Services;

/// <summary>
/// Rewrite xref-stream / hybrid PDFs to classic xref via PdfSharpCore.
/// No WPF types. PKCS#7 cert sign needs this first.
/// </summary>
public static class PdfClassicXref
{
    /// <summary>
    /// No-op (false) when the file is already classic xref. True after a rewrite.
    /// Throws if PdfSharp cannot produce a classic trailer (Sign would refuse).
    /// </summary>
    public static bool RewriteFile(string pdfPath)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !IoFile.Exists(pdfPath))
            throw new FileNotFoundException("PDF not found", pdfPath);

        var bytes = IoFile.ReadAllBytes(pdfPath);
        if (!PdfCmsSigner.NeedsClassicXrefRewrite(bytes))
            return false;

        var dir = IoPath.GetDirectoryName(pdfPath) ?? ".";
        var tmp = IoPath.Combine(dir, $".{IoPath.GetFileName(pdfPath)}.xref-rewrite.tmp");
        try
        {
            try
            {
                using (var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify))
                    doc.Save(tmp);
            }
            catch
            {
                using var src = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
                using var outDoc = new PdfDocument();
                for (var i = 0; i < src.PageCount; i++)
                    outDoc.AddPage(src.Pages[i]);
                outDoc.Save(tmp);
            }

            var rewritten = IoFile.ReadAllBytes(tmp);
            if (PdfCmsSigner.NeedsClassicXrefRewrite(rewritten))
            {
                try { IoFile.Delete(tmp); } catch { /* temp */ }
                throw new InvalidOperationException(
                    "PdfSharp rewrite still left an xref-stream or hybrid PDF.");
            }

            IoFile.Replace(tmp, pdfPath, destinationBackupFileName: null);
            return true;
        }
        catch
        {
            try { if (IoFile.Exists(tmp)) IoFile.Delete(tmp); } catch { /* temp */ }
            throw;
        }
    }
}
