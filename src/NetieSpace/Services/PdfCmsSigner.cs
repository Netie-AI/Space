using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace NetieSpace.Services;

/// <summary>
/// Incremental PDF PKCS#7 detached signature (adbe.pkcs7.detached).
/// Uses in-box SignedCms -- no iText. Classic xref only (PdfSharpCore saves).
/// Does not replace the visual Sign stamp path.
/// </summary>
public static class PdfCmsSigner
{
    public const int ContentsReserveBytes = 8192;
    public const string SubFilter = "adbe.pkcs7.detached";

    public static byte[] CreateMinimalPdf()
    {
        var body = string.Join('\n',
            "%PDF-1.4",
            "1 0 obj<< /Type /Catalog /Pages 2 0 R >>endobj",
            "2 0 obj<< /Type /Pages /Kids [3 0 R] /Count 1 >>endobj",
            "3 0 obj<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Contents 4 0 R /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> >>endobj",
            "4 0 obj<< /Length 51 >>stream",
            "BT /F1 12 Tf 40 120 Td (Hello Netie) Tj ET",
            "endstream",
            "endobj",
            "");
        var bodyBytes = Encoding.ASCII.GetBytes(body.Replace("\r\n", "\n").Replace('\r', '\n'));
        var offsets = FindObjOffsets(bodyBytes, 1, 4);
        var xref = new StringBuilder();
        xref.Append("xref\n0 5\n");
        xref.Append("0000000000 65535 f \n");
        for (var i = 1; i <= 4; i++)
            xref.Append($"{offsets[i]:D10} 00000 n \n");
        var startxref = bodyBytes.Length;
        var tail = "trailer<< /Size 5 /Root 1 0 R >>\nstartxref\n" + startxref + "\n%%EOF\n";
        return Concat(bodyBytes, Encoding.ASCII.GetBytes(tail));
    }

    /// <summary>
    /// Same one-page file as <see cref="CreateMinimalPdf"/> but /Kids is an
    /// indirect array (Chrome/Word). Sign must still stamp page 3.
    /// </summary>
    public static byte[] CreateMinimalPdfWithIndirectKids()
    {
        var body = string.Join('\n',
            "%PDF-1.4",
            "1 0 obj<< /Type /Catalog /Pages 2 0 R >>endobj",
            "2 0 obj<< /Type /Pages /Kids 5 0 R /Count 1 >>endobj",
            "3 0 obj<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Contents 4 0 R /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> >>endobj",
            "4 0 obj<< /Length 51 >>stream",
            "BT /F1 12 Tf 40 120 Td (Hello Netie) Tj ET",
            "endstream",
            "endobj",
            "5 0 obj[3 0 R]endobj",
            "");
        var bodyBytes = Encoding.ASCII.GetBytes(body.Replace("\r\n", "\n").Replace('\r', '\n'));
        var offsets = FindObjOffsets(bodyBytes, 1, 5);
        var xref = new StringBuilder();
        xref.Append("xref\n0 6\n");
        xref.Append("0000000000 65535 f \n");
        for (var i = 1; i <= 5; i++)
            xref.Append($"{offsets[i]:D10} 00000 n \n");
        var startxref = bodyBytes.Length;
        var tail = xref + "trailer<< /Size 6 /Root 1 0 R >>\nstartxref\n" + startxref + "\n%%EOF\n";
        return Concat(bodyBytes, Encoding.ASCII.GetBytes(tail));
    }

    /// <summary>
    /// MuPDF/in-app compact names: <c>/Type/Page</c> and <c>/Kids[3 0 R]</c>
    /// (no space after the name). sample.pdf / Q3_Report.pdf use this.
    /// A regex that required <c>/Kids [</c> or <c>/Type /Page</c> would miss
    /// page 1 and write a zero-size widget Adobe skips.
    /// </summary>
    public static byte[] CreateMinimalPdfCompactNames()
    {
        var body = string.Join('\n',
            "%PDF-1.4",
            "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj",
            "2 0 obj<</Type/Pages/Count 1/Kids[3 0 R]>>endobj",
            "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 300 200]/Contents 4 0 R>>endobj",
            "4 0 obj<</Length 51>>stream",
            "BT /F1 12 Tf 40 120 Td (Hello Netie) Tj ET",
            "endstream",
            "endobj",
            "");
        var bodyBytes = Encoding.ASCII.GetBytes(body.Replace("\r\n", "\n").Replace('\r', '\n'));
        var offsets = FindObjOffsets(bodyBytes, 1, 4);
        var xref = new StringBuilder();
        xref.Append("xref\n0 5\n");
        xref.Append("0000000000 65535 f \n");
        for (var i = 1; i <= 4; i++)
            xref.Append($"{offsets[i]:D10} 00000 n \n");
        var startxref = bodyBytes.Length;
        var tail = xref + "trailer<</Size 5/Root 1 0 R>>\nstartxref\n" + startxref + "\n%%EOF\n";
        return Concat(bodyBytes, Encoding.ASCII.GetBytes(tail));
    }

    /// <summary>
    /// One-page file whose /Annots is an indirect array (Chrome/Word links).
    /// Sign must inline those refs plus the widget, not <c>[widget arrayObj]</c>.
    /// </summary>
    public static byte[] CreateMinimalPdfWithIndirectAnnots()
    {
        var body = string.Join('\n',
            "%PDF-1.4",
            "1 0 obj<< /Type /Catalog /Pages 2 0 R >>endobj",
            "2 0 obj<< /Type /Pages /Kids [3 0 R] /Count 1 >>endobj",
            "3 0 obj<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Contents 4 0 R /Annots 5 0 R /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> >>endobj",
            "4 0 obj<< /Length 51 >>stream",
            "BT /F1 12 Tf 40 120 Td (Hello Netie) Tj ET",
            "endstream",
            "endobj",
            "5 0 obj[6 0 R]endobj",
            "6 0 obj<< /Type /Annot /Subtype /Link /Rect [10 10 40 20] /Border [0 0 0] >>endobj",
            "");
        var bodyBytes = Encoding.ASCII.GetBytes(body.Replace("\r\n", "\n").Replace('\r', '\n'));
        var offsets = FindObjOffsets(bodyBytes, 1, 6);
        var xref = new StringBuilder();
        xref.Append("xref\n0 7\n");
        xref.Append("0000000000 65535 f \n");
        for (var i = 1; i <= 6; i++)
            xref.Append($"{offsets[i]:D10} 00000 n \n");
        var startxref = bodyBytes.Length;
        var tail = xref + "trailer<< /Size 7 /Root 1 0 R >>\nstartxref\n" + startxref + "\n%%EOF\n";
        return Concat(bodyBytes, Encoding.ASCII.GetBytes(tail));
    }

    public static X509Certificate2 CreateEphemeralSelfSigned(string cn = "Netie Space Test")
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={cn}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        return new X509Certificate2(cert.Export(X509ContentType.Pfx, "tmp"), "tmp",
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
    }

    public static bool HasDetachedPkcs7Marker(ReadOnlySpan<byte> pdf) =>
        IndexOf(pdf, Encoding.ASCII.GetBytes("/" + SubFilter)) >= 0;

    /// <summary>
    /// True when the file is xref-stream or hybrid (trailer plus /Type /XRef).
    /// A trailer alone is not enough -- hybrid files still break incremental PKCS#7.
    /// PdfSharp rewrite comes first; this method has no PdfSharp types.
    /// </summary>
    public static bool NeedsClassicXrefRewrite(ReadOnlySpan<byte> pdf)
    {
        if (IndexOf(pdf, "/Type /XRef"u8) >= 0 || IndexOf(pdf, "/Type/XRef"u8) >= 0)
            return true;
        var sx = LastIndexOf(pdf, "startxref"u8);
        if (sx < 0)
            return true;
        return LastIndexOf(pdf[..sx], "trailer"u8) < 0;
    }

    /// <summary>
    /// Verify the last detached PKCS#7 against /ByteRange. Does not talk to Adobe.
    /// </summary>
    public static bool TryVerify(byte[] pdf, out string? error)
    {
        error = null;
        try
        {
            if (!TryParseByteRange(pdf, out var r0, out var r1, out var r2, out var r3))
            {
                error = "ByteRange missing.";
                return false;
            }
            if (r0 != 0 || r1 <= 0 || r2 <= r1 || r2 + r3 > pdf.Length)
            {
                error = "ByteRange out of range.";
                return false;
            }

            var toSign = new byte[r1 + r3];
            Buffer.BlockCopy(pdf, r0, toSign, 0, r1);
            Buffer.BlockCopy(pdf, r2, toSign, r1, r3);

            if (!TryReadContentsHex(pdf, out var hex))
            {
                error = "Contents hex missing.";
                return false;
            }

            var cmsBytes = DecodeCmsFromPaddedHex(hex);
            var content = new ContentInfo(toSign);
            var signed = new SignedCms(content, detached: true);
            signed.Decode(cmsBytes);
            signed.CheckSignature(verifySignatureOnly: true);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void SignFile(string pdfPath, X509Certificate2 cert)
    {
        var original = File.ReadAllBytes(pdfPath);
        byte[] signed;
        try
        {
            signed = Sign(original, cert);
        }
        catch
        {
            // Leave original bytes untouched on failure.
            throw;
        }

        var dir = Path.GetDirectoryName(pdfPath) ?? ".";
        var tmp = Path.Combine(dir, $".{Path.GetFileName(pdfPath)}.cms-sign.tmp");
        File.WriteAllBytes(tmp, signed);
        File.Replace(tmp, pdfPath, destinationBackupFileName: null);
    }

    public static byte[] Sign(byte[] pdf, X509Certificate2 cert)
    {
        if (cert.GetRSAPrivateKey() is null)
            throw new InvalidOperationException("Certificate has no RSA private key.");
        if (IndexOf(pdf, "%PDF-"u8) != 0)
            throw new InvalidOperationException("Not a PDF.");
        // "xref" also matches inside startxref / /Type /XRef. Hybrid files have a
        // trailer plus an xref stream -- Adobe incremental PKCS#7 still fails.
        if (NeedsClassicXrefRewrite(pdf) || IndexOf(pdf, "xref"u8) < 0 || LastIndexOf(pdf, "startxref"u8) < 0)
            throw new InvalidOperationException("Cert sign needs a classic xref PDF. Save from Space (PdfSharp) and retry.");

        var trailer = ReadTrailer(pdf);
        var nextId = trailer.Size;
        var catalogId = nextId++;
        var acroId = nextId++;
        var fieldId = nextId++;
        var sigId = nextId++;
        var apId = nextId++;

        var hexZeros = new string('0', ContentsReserveBytes * 2);
        var byteRangePlaceholder = "[0 ********** ********** **********]";
        var when = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var name = EscapePdf(cert.GetNameInfo(X509NameType.SimpleName, false) ?? "Netie");

        var pageRef = TryFirstPageRef(pdf, trailer.RootId);
        var fieldRect = pageRef is null ? "[0 0 0 0]" : "[36 36 180 90]";
        var pageKey = pageRef is { } p ? $" /P {p} 0 R" : "";
        var apStream = BuildAppearanceStream(name);

        var block = new StringBuilder();
        block.Append('\n');
        var objStarts = new Dictionary<int, int>();

        void Mark(int id) => objStarts[id] = pdf.Length + Encoding.ASCII.GetByteCount(block.ToString());

        Mark(catalogId);
        var pagesRef = TryPagesRef(pdf, trailer.RootId);
        if (pagesRef is null)
            throw new InvalidOperationException("Cert sign could not read /Pages from the catalog.");
        block.Append($"{catalogId} 0 obj<< /Type /Catalog /Pages {pagesRef} 0 R /AcroForm {acroId} 0 R >>endobj\n");
        Mark(acroId);
        block.Append($"{acroId} 0 obj<< /Fields [{fieldId} 0 R] /SigFlags 3 >>endobj\n");
        Mark(fieldId);
        block.Append($"{fieldId} 0 obj<< /FT /Sig /Type /Annot /Subtype /Widget /F 4 /Rect {fieldRect} /V {sigId} 0 R /T (Signature1){pageKey} /AP << /N {apId} 0 R >> >>endobj\n");
        Mark(sigId);
        block.Append($"{sigId} 0 obj<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /{SubFilter} /ByteRange {byteRangePlaceholder} /Contents <{hexZeros}> /M (D:{when}Z) /Name ({name}) >>endobj\n");
        Mark(apId);
        block.Append($"{apId} 0 obj<< /Type /XObject /Subtype /Form /BBox [0 0 144 54] /Resources << /Font << /Helv << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> /Length {apStream.Length} >>stream\n");
        block.Append(apStream);
        block.Append("\nendstream\nendobj\n");

        string? pageOverride = null;
        int? pageOverrideId = null;
        if (pageRef is { } pageId)
        {
            pageOverride = RewritePageWithAnnot(pdf, pageId, fieldId);
            if (pageOverride is not null)
                pageOverrideId = pageId;
        }
        if (pageOverride is not null && pageOverrideId is { } markedPage)
        {
            Mark(markedPage);
            block.Append(pageOverride);
        }

        var insert = Encoding.ASCII.GetBytes(block.ToString());
        var withObjs = Concat(pdf, insert);

        var xrefStart = withObjs.Length;
        var xref = new StringBuilder();
        xref.Append("xref\n0 1\n0000000000 65535 f \n");
        if (pageOverride is not null && pageOverrideId is { } pid)
        {
            xref.Append($"{pid} 1\n");
            xref.Append($"{objStarts[pid]:D10} 00000 n \n");
        }
        xref.Append($"{catalogId} 5\n");
        foreach (var id in new[] { catalogId, acroId, fieldId, sigId, apId })
            xref.Append($"{objStarts[id]:D10} 00000 n \n");
        xref.Append($"trailer<< /Size {nextId} /Root {catalogId} 0 R /Prev {trailer.StartXref} >>\n");
        xref.Append($"startxref\n{xrefStart}\n%%EOF\n");
        var assembled = Concat(withObjs, Encoding.ASCII.GetBytes(xref.ToString()));

        var contentsToken = Encoding.ASCII.GetBytes("/Contents <");
        var contentsOpen = LastIndexOf(assembled, contentsToken);
        if (contentsOpen < 0)
            throw new InvalidOperationException("Contents placeholder missing.");
        var hexStart = contentsOpen + contentsToken.Length;
        var hexEnd = hexStart + (ContentsReserveBytes * 2);
        if (hexEnd >= assembled.Length || assembled[hexEnd] != (byte)'>')
            throw new InvalidOperationException("Contents placeholder size mismatch.");

        var range1Len = hexStart - 1;
        var range2Start = hexEnd + 1;
        var range2Len = assembled.Length - range2Start;
        var byteRange = $"[0 {range1Len} {range2Start} {range2Len}]";
        var padded = byteRange.PadRight(byteRangePlaceholder.Length);
        if (padded.Length != byteRangePlaceholder.Length)
            throw new InvalidOperationException("ByteRange overflow.");

        var brToken = Encoding.ASCII.GetBytes("/ByteRange ");
        var brAt = LastIndexOf(assembled, brToken);
        if (brAt < 0)
            throw new InvalidOperationException("ByteRange placeholder missing.");
        var brVal = brAt + brToken.Length;
        var brBytes = Encoding.ASCII.GetBytes(padded);
        Array.Copy(brBytes, 0, assembled, brVal, brBytes.Length);

        var toSign = new byte[range1Len + range2Len];
        Buffer.BlockCopy(assembled, 0, toSign, 0, range1Len);
        Buffer.BlockCopy(assembled, range2Start, toSign, range1Len, range2Len);

        var cms = CreateDetachedCms(toSign, cert);
        if (cms.Length > ContentsReserveBytes)
            throw new InvalidOperationException($"CMS {cms.Length} bytes exceeds reserve {ContentsReserveBytes}.");

        var hex = Convert.ToHexString(cms).ToLowerInvariant().PadRight(ContentsReserveBytes * 2, '0');
        var hexBytes = Encoding.ASCII.GetBytes(hex);
        Array.Copy(hexBytes, 0, assembled, hexStart, hexBytes.Length);
        if (!TryVerify(assembled, out var verifyErr))
            throw new InvalidOperationException("Cert sign self-check failed. " + verifyErr);
        return assembled;
    }

    private static bool TryParseByteRange(byte[] pdf, out int r0, out int r1, out int r2, out int r3)
    {
        r0 = r1 = r2 = r3 = 0;
        var brAt = LastIndexOf(pdf, "/ByteRange "u8);
        if (brAt < 0)
            return false;
        var span = Encoding.ASCII.GetString(pdf, brAt, Math.Min(96, pdf.Length - brAt));
        var m = Regex.Match(span, @"/ByteRange\s*\[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]");
        if (!m.Success)
            return false;
        r0 = int.Parse(m.Groups[1].Value);
        r1 = int.Parse(m.Groups[2].Value);
        r2 = int.Parse(m.Groups[3].Value);
        r3 = int.Parse(m.Groups[4].Value);
        return true;
    }

    private static bool TryReadContentsHex(byte[] pdf, out string hex)
    {
        hex = "";
        var token = Encoding.ASCII.GetBytes("/Contents <");
        var open = LastIndexOf(pdf, token);
        if (open < 0)
            return false;
        var hexStart = open + token.Length;
        var close = IndexOf(pdf.AsSpan(hexStart), ">"u8);
        if (close < 0)
            return false;
        hex = Encoding.ASCII.GetString(pdf, hexStart, close);
        return hex.Length > 0;
    }

    private static byte[] DecodeCmsFromPaddedHex(string hex)
    {
        var raw = Convert.FromHexString(hex.Trim());
        if (raw.Length < 2 || raw[0] != 0x30)
            throw new InvalidOperationException("CMS is not a DER SEQUENCE.");
        int headerLen;
        int contentLen;
        if ((raw[1] & 0x80) == 0)
        {
            headerLen = 2;
            contentLen = raw[1];
        }
        else
        {
            var n = raw[1] & 0x7F;
            if (n < 1 || n > 4 || 2 + n > raw.Length)
                throw new InvalidOperationException("CMS DER length is invalid.");
            headerLen = 2 + n;
            contentLen = 0;
            for (var i = 0; i < n; i++)
                contentLen = (contentLen << 8) | raw[2 + i];
        }
        var total = headerLen + contentLen;
        if (total <= 0 || total > raw.Length)
            throw new InvalidOperationException("CMS truncated.");
        var cms = new byte[total];
        Buffer.BlockCopy(raw, 0, cms, 0, total);
        return cms;
    }

    private static byte[] CreateDetachedCms(byte[] data, X509Certificate2 cert)
    {
        var content = new ContentInfo(data);
        var signed = new SignedCms(content, detached: true);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, cert)
        {
            IncludeOption = X509IncludeOption.EndCertOnly,
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1")
        };
        signed.ComputeSignature(signer);
        return signed.Encode();
    }

    private readonly record struct TrailerInfo(int Size, int RootId, int StartXref);

    private static TrailerInfo ReadTrailer(byte[] pdf)
    {
        var sx = LastIndexOf(pdf, "startxref"u8);
        if (sx < 0)
            throw new InvalidOperationException("startxref missing.");
        var after = Encoding.ASCII.GetString(pdf, sx, Math.Min(80, pdf.Length - sx));
        var m = Regex.Match(after, @"startxref\s+(\d+)");
        if (!m.Success)
            throw new InvalidOperationException("startxref offset missing.");
        var startxref = int.Parse(m.Groups[1].Value);

        var tr = LastIndexOf(pdf.AsSpan(0, sx), "trailer"u8);
        if (tr < 0)
            throw new InvalidOperationException("trailer missing (xref stream PDFs are not signed).");
        var trailerText = Encoding.ASCII.GetString(pdf, tr, sx - tr);
        var size = MatchInt(trailerText, @"/Size\s+(\d+)");
        var root = MatchInt(trailerText, @"/Root\s+(\d+)\s+0\s+R");
        return new TrailerInfo(size, root, startxref);
    }

    private static string BuildAppearanceStream(string name)
    {
        var label = string.IsNullOrWhiteSpace(name) ? "Netie" : name;
        if (label.Length > 28)
            label = label[..28];
        return string.Join('\n',
            "q",
            "0.90 0.95 0.90 rg",
            "0 0 144 54 re f",
            "0 0.35 0 rg",
            "1 w",
            "0.5 0.5 143 53 re S",
            "BT /Helv 9 Tf 6 32 Td (Digitally signed) Tj",
            $"6 16 Td ({EscapePdf(label)}) Tj ET",
            "Q");
    }

    private static string? RewritePageWithAnnot(byte[] pdf, int pageId, int fieldId)
    {
        var obj = TryReadObject(pdf, pageId);
        if (obj is null)
            return null;
        var start = obj.IndexOf("<<", StringComparison.Ordinal);
        var endTok = obj.LastIndexOf("endobj", StringComparison.Ordinal);
        if (start < 0 || endTok < 0)
            return null;
        var inner = obj[start..endTok];
        inner = AddAnnotsToPageDict(pdf, inner, fieldId);
        return $"{pageId} 0 obj\n{inner.Trim()}\nendobj\n";
    }

    private static string AddAnnotsToPageDict(byte[] pdf, string pageBody, int fieldId)
    {
        var refTok = $"{fieldId} 0 R";
        var annotsOpen = Regex.Match(pageBody, @"/Annots\s*\[");
        if (annotsOpen.Success)
            return pageBody.Insert(annotsOpen.Index + annotsOpen.Length, refTok + " ");

        var annotsRef = Regex.Match(pageBody, @"/Annots\s+(\d+)\s+0\s+R");
        if (annotsRef.Success)
        {
            var arrId = int.Parse(annotsRef.Groups[1].Value);
            var refs = new List<string> { refTok };
            var arrObj = TryReadObject(pdf, arrId);
            if (arrObj is not null)
            {
                foreach (Match m in Regex.Matches(arrObj, @"(\d+)\s+0\s+R"))
                    refs.Add($"{m.Groups[1].Value} 0 R");
            }

            return pageBody.Remove(annotsRef.Index, annotsRef.Length)
                .Insert(annotsRef.Index, $"/Annots [{string.Join(" ", refs)}]");
        }

        var streamAt = pageBody.IndexOf("stream", StringComparison.Ordinal);
        var search = streamAt >= 0 ? pageBody[..streamAt] : pageBody;
        var gt = search.LastIndexOf(">>", StringComparison.Ordinal);
        if (gt < 0)
            return pageBody;
        return pageBody.Insert(gt, $" /Annots [{refTok}]");
    }

    private static int? TryPagesRef(byte[] pdf, int catalogId)
    {
        var obj = TryReadObject(pdf, catalogId);
        if (obj is null) return null;
        var m = Regex.Match(obj, @"/Pages\s+(\d+)\s+0\s+R");
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    /// <summary>
    /// First page object id. Inline <c>/Kids [3 0 R]</c> or indirect
    /// <c>/Kids 5 0 R</c> (Chrome/Word). Follows nested /Pages kids (depth 8).
    /// </summary>
    public static int? TryFirstPageRef(byte[] pdf, int catalogId)
    {
        var pagesId = TryPagesRef(pdf, catalogId);
        if (pagesId is null) return null;
        var obj = TryReadObject(pdf, pagesId.Value);
        if (obj is null) return null;
        return FirstPageFromKids(pdf, obj, 0);
    }

    private static int? FirstPageFromKids(byte[] pdf, string pagesOrKidsObj, int depth)
    {
        if (depth > 8)
            return null;
        var inline = Regex.Match(pagesOrKidsObj, @"/Kids\s*\[\s*(\d+)\s+0\s+R");
        if (inline.Success)
            return ResolvePageOrPages(pdf, int.Parse(inline.Groups[1].Value), depth);

        var indirect = Regex.Match(pagesOrKidsObj, @"/Kids\s+(\d+)\s+0\s+R");
        if (!indirect.Success)
            return null;
        var kidsObj = TryReadObject(pdf, int.Parse(indirect.Groups[1].Value));
        if (kidsObj is null)
            return null;
        var first = Regex.Match(kidsObj, @"(\d+)\s+0\s+R");
        if (!first.Success)
            return null;
        return ResolvePageOrPages(pdf, int.Parse(first.Groups[1].Value), depth);
    }

    private static int? ResolvePageOrPages(byte[] pdf, int id, int depth)
    {
        var obj = TryReadObject(pdf, id);
        if (obj is null)
            return null;
        // /Type /Pages before /Type /Page -- "Page" is a prefix of "Pages".
        if (Regex.IsMatch(obj, @"/Type\s*/Pages"))
            return FirstPageFromKids(pdf, obj, depth + 1);
        return id;
    }

    /// <summary>
    /// Read <c>id 0 obj ... endobj</c>. Must not treat "12 0 obj" as object 2
    /// (Adobe widget would land on the wrong page).
    /// </summary>
    public static string? TryReadObject(byte[] pdf, int id)
    {
        var at = IndexOfObj(pdf, id);
        if (at < 0) return null;
        var end = IndexOf(pdf.AsSpan(at), "endobj"u8);
        if (end < 0) return null;
        return Encoding.ASCII.GetString(pdf, at, end + 6);
    }

    private static Dictionary<int, int> FindObjOffsets(byte[] pdf, int fromId, int toId)
    {
        var map = new Dictionary<int, int>();
        for (var id = fromId; id <= toId; id++)
        {
            var at = IndexOfObj(pdf, id);
            if (at < 0)
                throw new InvalidOperationException($"object {id} missing");
            map[id] = at;
        }
        return map;
    }

    /// <summary>Object header whose previous byte is not a digit (so 12 != 2).</summary>
    private static int IndexOfObj(byte[] pdf, int id)
    {
        var needle = Encoding.ASCII.GetBytes($"{id} 0 obj");
        var start = 0;
        while (start <= pdf.Length - needle.Length)
        {
            var found = IndexOf(pdf.AsSpan(start), needle);
            if (found < 0)
                return -1;
            var abs = start + found;
            if (abs == 0 || pdf[abs - 1] < (byte)'0' || pdf[abs - 1] > (byte)'9')
                return abs;
            start = abs + 1;
        }
        return -1;
    }

    private static int MatchInt(string text, string pattern)
    {
        var m = Regex.Match(text, pattern);
        if (!m.Success)
            throw new InvalidOperationException($"trailer field missing: {pattern}");
        return int.Parse(m.Groups[1].Value);
    }

    private static string EscapePdf(string s)
    {
        return s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("\n", " ").Replace("\r", " ");
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var n = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, n, 0, a.Length);
        Buffer.BlockCopy(b, 0, n, a.Length, b.Length);
        return n;
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

    private static int LastIndexOf(ReadOnlySpan<byte> hay, ReadOnlySpan<byte> needle)
    {
        for (var i = hay.Length - needle.Length; i >= 0; i--)
        {
            if (hay[i] == needle[0] && hay.Slice(i, needle.Length).SequenceEqual(needle))
                return i;
        }
        return -1;
    }
}
