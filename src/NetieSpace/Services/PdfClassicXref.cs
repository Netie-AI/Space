using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace NetieSpace.Services;

/// <summary>
/// Rewrite xref-stream / hybrid PDFs to classic xref.
/// Prefer dropping incremental xref-stream tails (Chrome/Word). PdfSharp is the fallback.
/// No WPF types. PKCS#7 cert sign needs this first.
/// </summary>
public static class PdfClassicXref
{
    /// <summary>
    /// Walk %%EOF from the end. First prefix that is already classic xref wins.
    /// Chrome/Word hybrids keep a classic increment, then append /Type /XRef.
    /// </summary>
    public static bool TryStripToClassic(ReadOnlySpan<byte> pdf, out byte[] classic)
    {
        classic = [];
        if (pdf.Length == 0)
            return false;
        if (!PdfCmsSigner.NeedsClassicXrefRewrite(pdf))
        {
            classic = pdf.ToArray();
            return true;
        }

        var ends = FindEofEnds(pdf);
        for (var i = ends.Count - 1; i >= 0; i--)
        {
            var slice = pdf[..ends[i]];
            if (slice.Length > 0 && !PdfCmsSigner.NeedsClassicXrefRewrite(slice))
            {
                classic = slice.ToArray();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// No-op (false) when the file is already classic xref. True after a rewrite.
    /// Throws if neither strip nor PdfSharp can produce a classic trailer (Sign would refuse).
    /// </summary>
    public static bool RewriteFile(string pdfPath)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !IoFile.Exists(pdfPath))
            throw new System.IO.FileNotFoundException("PDF not found", pdfPath);

        var bytes = IoFile.ReadAllBytes(pdfPath);
        if (!PdfCmsSigner.NeedsClassicXrefRewrite(bytes))
            return false;

        var dir = IoPath.GetDirectoryName(pdfPath) ?? ".";
        var tmp = IoPath.Combine(dir, $".{IoPath.GetFileName(pdfPath)}.xref-rewrite.tmp");
        try
        {
            if (TryStripToClassic(bytes, out var stripped) &&
                stripped.Length < bytes.Length &&
                !PdfCmsSigner.NeedsClassicXrefRewrite(stripped))
            {
                IoFile.WriteAllBytes(tmp, stripped);
            }
            else
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

    private static List<int> FindEofEnds(ReadOnlySpan<byte> pdf)
    {
        var ends = new List<int>();
        ReadOnlySpan<byte> token = "%%EOF"u8;
        var start = 0;
        while (start <= pdf.Length - token.Length)
        {
            var at = IndexOf(pdf[start..], token);
            if (at < 0)
                break;
            var end = start + at + token.Length;
            while (end < pdf.Length && (pdf[end] == (byte)'\r' || pdf[end] == (byte)'\n'))
                end++;
            ends.Add(end);
            start = start + at + token.Length;
        }
        return ends;
    }

    private static int IndexOf(ReadOnlySpan<byte> hay, ReadOnlySpan<byte> needle)
    {
        for (var i = 0; i <= hay.Length - needle.Length; i++)
        {
            if (hay[i] == needle[0] && hay.Slice(i, needle.Length).SequenceEqual(needle))
                return i;
        }
        return -1;
    }
}
