using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.Content;
using PdfSharpCore.Pdf.Content.Objects;
using PdfSharpCore.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using static NetieSpace.Apps.Pdf.PdfToolSupport;

namespace NetieSpace.Apps.Pdf;

/// <summary>
/// pdf-compress on PdfSharpCore. Images placed on a page at more than a pixel-per-inch threshold are
/// downsampled to the level's target resolution and re-encoded as JPEG; identical images are merged;
/// streams are Flate-compressed; objects nothing points at are not written. The result is refused unless it
/// is smaller than the input and page count plus every page's text are unchanged (read back with PdfPig).
/// Images with transparency (soft mask or colour key mask), stencil masks, palettes, CMYK, other bit depths
/// and images used anywhere other than directly on a page are left byte for byte as they were.
/// </summary>
internal static class PdfCompressTool
{
    private sealed record Level(string Name, double TargetPpi, double ThresholdPpi, int Quality);

    private static readonly Level[] Levels =
    {
        new("low", 200, 300, 85),
        new("recommended", 150, 200, 70),
        new("extreme", 96, 130, 45),
    };

    public static Task<IReadOnlyList<string>> RunAsync(ToolRequest req, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var levelName = OptChoice(req, "level", "recommended", "low", "recommended", "extreme");
        var level = Levels.First(l => l.Name == levelName);
        return PdfTool3Batch.Run(req, progress, ct, "_compressed", ".pdf", (input, output, token) =>
        {
            var inBytes = IoFile.ReadAllBytes(input);
            var outBytes = Compress(inBytes, input, level, token, out var note);
            if (outBytes.Length >= inBytes.Length)
                throw new InvalidOperationException(
                    $"already compact: best result {outBytes.Length} bytes is not smaller than the original {inBytes.Length} bytes ({note})");
            VerifySameContent(inBytes, outBytes);
            IoFile.WriteAllBytes(output, outBytes);
        });
    }

    // ----------------------------------------------------------------------------------------------

    private static byte[] Compress(byte[] pdf, string label, Level level, CancellationToken ct, out string note)
    {
        PdfDocument doc;
        try
        {
            using var ms = new MemoryStream(pdf, false);
            doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
        }
        catch (PdfReaderException ex)
        {
            throw new InvalidOperationException(
                $"cannot open {label}: {ex.Message} (password protected files need pdf-unlock first; damaged files need pdf-repair)", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"cannot read {label} as a PDF: {ex.Message}", ex);
        }

        using (doc)
        {
            if (doc.PageCount == 0) throw new InvalidOperationException("PDF has no pages");
            var merged = MergeDuplicateImages(doc, ct);
            var recoded = RecodeImages(doc, level, ct);
            var zipped = ZipPlainStreams(doc);
            note = $"{recoded} image(s) re-encoded, {merged} duplicate image(s) merged, {zipped} stream(s) compressed";

            doc.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
            doc.Options.CompressContentStreams = true;
            doc.Options.NoCompression = false;
            using var o = new MemoryStream();
            doc.Save(o, false);
            return o.ToArray();
        }
    }

    private static void VerifySameContent(byte[] before, byte[] after)
    {
        using var a = UglyToad.PdfPig.PdfDocument.Open(before);
        using var b = UglyToad.PdfPig.PdfDocument.Open(after);
        if (a.NumberOfPages != b.NumberOfPages)
            throw new InvalidOperationException($"compression changed the page count ({a.NumberOfPages} -> {b.NumberOfPages}); nothing written");
        for (var i = 1; i <= a.NumberOfPages; i++)
            if (a.GetPage(i).Text != b.GetPage(i).Text)
                throw new InvalidOperationException($"compression changed the text of page {i}; nothing written");
    }

    // ----- object graph helpers -----

    private static IEnumerable<PdfItem> Children(PdfItem item)
    {
        switch (item)
        {
            case PdfDictionary d:
                foreach (var v in d.Elements.Values) yield return v;
                break;
            case PdfArray a:
                foreach (var v in a.Elements) yield return v;
                break;
        }
    }

    /// <summary>How many times each object id is referenced anywhere in the document.</summary>
    private static Dictionary<int, int> CountReferences(PdfDocument doc)
    {
        var counts = new Dictionary<int, int>();
        var seen = new HashSet<PdfItem>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<PdfItem>();
        foreach (var o in doc.Internals.GetAllObjects()) stack.Push(o);
        stack.Push(doc.Internals.Catalog);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (!seen.Add(cur)) continue;
            foreach (var c in Children(cur))
            {
                if (c is PdfReference r)
                {
                    counts[r.ObjectID.ObjectNumber] = counts.GetValueOrDefault(r.ObjectID.ObjectNumber) + 1;
                }
                else stack.Push(c);
            }
        }
        // The trailer's /Root and /Info pointers also count; they never point at images, so ignore them.
        return counts;
    }

    private static string Name(PdfDictionary d, string key) =>
        d.Elements.ContainsKey(key) && d.Elements[key] is PdfName n ? n.Value : "";

    private static bool IsImage(PdfDictionary d) => Name(d, "/Subtype") == "/Image";

    private static PdfDictionary? Resolve(PdfItem? item) => item switch
    {
        PdfReference r => r.Value as PdfDictionary,
        PdfDictionary d => d,
        _ => null,
    };

    private static IEnumerable<PdfDictionary> PageXObjectDicts(PdfDocument doc)
    {
        for (var i = 0; i < doc.PageCount; i++)
        {
            var page = doc.Pages[i];
            var res = FindResources(page);
            var xo = res is null ? null : Resolve(res.Elements.ContainsKey("/XObject") ? res.Elements["/XObject"] : null);
            if (xo is not null) yield return xo;
        }
    }

    private static PdfDictionary? FindResources(PdfDictionary node)
    {
        for (var depth = 0; node is not null && depth < 32; depth++)
        {
            if (node.Elements.ContainsKey("/Resources")) return Resolve(node.Elements["/Resources"]);
            node = Resolve(node.Elements.ContainsKey("/Parent") ? node.Elements["/Parent"] : null)!;
        }
        return null;
    }

    private static bool HasTransparency(PdfDictionary img) =>
        img.Elements.ContainsKey("/SMask") || img.Elements.ContainsKey("/Mask") ||
        (img.Elements.ContainsKey("/ImageMask") && img.Elements["/ImageMask"] is PdfBoolean { Value: true });

    // ----- duplicate images -----

    private static int MergeDuplicateImages(PdfDocument doc, CancellationToken ct)
    {
        var canon = new Dictionary<string, PdfReference>();
        var remap = new Dictionary<int, PdfReference>();
        foreach (var xo in PageXObjectDicts(doc).Distinct())
        {
            foreach (var key in xo.Elements.Keys.ToList())
            {
                ct.ThrowIfCancellationRequested();
                if (xo.Elements[key] is not PdfReference r || r.Value is not PdfDictionary img) continue;
                if (!IsImage(img) || img.Stream is null || HasTransparency(img)) continue;
                var fp = Fingerprint(img);
                if (canon.TryGetValue(fp, out var first))
                {
                    if (first.ObjectID.ObjectNumber != r.ObjectID.ObjectNumber)
                        remap[r.ObjectID.ObjectNumber] = first;
                }
                else canon[fp] = r;
            }
        }
        if (remap.Count == 0) return 0;

        var seen = new HashSet<PdfItem>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<PdfItem>();
        foreach (var o in doc.Internals.GetAllObjects()) stack.Push(o);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (!seen.Add(cur)) continue;
            if (cur is PdfDictionary d)
            {
                foreach (var k in d.Elements.Keys.ToList())
                {
                    var v = d.Elements[k];
                    if (v is PdfReference rr && remap.TryGetValue(rr.ObjectID.ObjectNumber, out var to)) d.Elements[k] = to;
                    else if (v is not PdfReference) stack.Push(v);
                }
            }
            else if (cur is PdfArray a)
            {
                for (var i = 0; i < a.Elements.Count; i++)
                {
                    var v = a.Elements[i];
                    if (v is PdfReference rr && remap.TryGetValue(rr.ObjectID.ObjectNumber, out var to)) a.Elements[i] = to;
                    else if (v is not PdfReference) stack.Push(v);
                }
            }
        }
        return remap.Count;
    }

    private static string Fingerprint(PdfDictionary img)
    {
        using var sha = SHA256.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(img.Stream.Value));
        string K(string key) => img.Elements.ContainsKey(key) ? img.Elements[key].ToString() ?? "" : "";
        return string.Join("|", hash, K("/Width"), K("/Height"), K("/ColorSpace"), K("/BitsPerComponent"), K("/Filter"), K("/DecodeParms"), K("/Decode"));
    }

    // ----- image recoding -----

    private static int RecodeImages(PdfDocument doc, Level level, CancellationToken ct)
    {
        var refs = CountReferences(doc);
        // Highest pixel density each image is drawn at, and how many page resource entries name it.
        var ppi = new Dictionary<int, double>();
        var named = new Dictionary<int, int>();
        var objs = new Dictionary<int, PdfDictionary>();

        for (var i = 0; i < doc.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var page = doc.Pages[i];
            var res = FindResources(page);
            var xo = res is null ? null : Resolve(res.Elements.ContainsKey("/XObject") ? res.Elements["/XObject"] : null);
            if (xo is null) continue;
            var byName = new Dictionary<string, int>();
            foreach (var key in xo.Elements.Keys)
            {
                if (xo.Elements[key] is PdfReference r && r.Value is PdfDictionary d && IsImage(d))
                {
                    byName[key] = r.ObjectID.ObjectNumber;
                    objs[r.ObjectID.ObjectNumber] = d;
                    named[r.ObjectID.ObjectNumber] = named.GetValueOrDefault(r.ObjectID.ObjectNumber) + 1;
                }
            }
            if (byName.Count == 0) continue;
            foreach (var (name, scaleX, scaleY) in PlacedImages(page))
            {
                if (!byName.TryGetValue(name, out var id)) continue;
                var d = objs[id];
                var w = d.Elements.GetInteger("/Width");
                var h = d.Elements.GetInteger("/Height");
                if (scaleX < 0.5 || scaleY < 0.5 || w <= 0 || h <= 0) continue;
                var p = Math.Min(w / (scaleX / 72.0), h / (scaleY / 72.0));
                ppi[id] = Math.Max(ppi.GetValueOrDefault(id), p);
            }
        }

        var recoded = 0;
        foreach (var (id, density) in ppi)
        {
            ct.ThrowIfCancellationRequested();
            var img = objs[id];
            // Used somewhere other than a page resource entry (form, pattern, annotation): ppi is unknown.
            if (refs.GetValueOrDefault(id) != named.GetValueOrDefault(id)) continue;
            if (density < level.ThresholdPpi) continue;
            if (HasTransparency(img)) continue;
            if (TryRecode(img, level, density)) recoded++;
        }
        return recoded;
    }

    /// <summary>Walks a page's content streams tracking the CTM; yields (image resource name, drawn width pt, drawn height pt).</summary>
    private static List<(string Name, double W, double H)> PlacedImages(PdfPage page)
    {
        var result = new List<(string, double, double)>();
        CSequence seq;
        try { seq = ContentReader.ReadContent(page); }
        catch { return result; }

        var stack = new Stack<double[]>();
        var m = new[] { 1.0, 0, 0, 1, 0, 0 };
        foreach (var item in seq)
        {
            if (item is not COperator op) continue;
            switch (op.OpCode.OpCodeName)
            {
                case OpCodeName.q: stack.Push((double[])m.Clone()); break;
                case OpCodeName.Q: if (stack.Count > 0) m = stack.Pop(); break;
                case OpCodeName.cm:
                {
                    if (op.Operands.Count != 6) break;
                    var v = new double[6];
                    var ok = true;
                    for (var k = 0; k < 6; k++)
                    {
                        if (op.Operands[k] is CReal r) v[k] = r.Value;
                        else if (op.Operands[k] is CInteger n) v[k] = n.Value;
                        else ok = false;
                    }
                    if (ok) m = Multiply(v, m);
                    break;
                }
                case OpCodeName.Do:
                    if (op.Operands.Count == 1 && op.Operands[0] is CName cn)
                        result.Add((cn.Name, Math.Sqrt(m[0] * m[0] + m[1] * m[1]), Math.Sqrt(m[2] * m[2] + m[3] * m[3])));
                    break;
            }
        }
        return result;
    }

    // [a b c d e f] matrices, a new transform applied before the current one.
    private static double[] Multiply(double[] n, double[] c) => new[]
    {
        n[0] * c[0] + n[1] * c[2], n[0] * c[1] + n[1] * c[3],
        n[2] * c[0] + n[3] * c[2], n[2] * c[1] + n[3] * c[3],
        n[4] * c[0] + n[5] * c[2] + c[4], n[4] * c[1] + n[5] * c[3] + c[5],
    };

    private static bool TryRecode(PdfDictionary img, Level level, double density)
    {
        if (img.Stream is null) return false;
        var w = img.Elements.GetInteger("/Width");
        var h = img.Elements.GetInteger("/Height");
        if (w <= 0 || h <= 0 || (long)w * h > 120_000_000L) return false;
        if (img.Elements.ContainsKey("/Decode")) return false;
        if (img.Elements.ContainsKey("/BitsPerComponent") && img.Elements.GetInteger("/BitsPerComponent") != 8) return false;

        var cs = img.Elements.ContainsKey("/ColorSpace") && img.Elements["/ColorSpace"] is PdfName csn ? csn.Value : "";
        var gray = cs == "/DeviceGray";
        if (!gray && cs != "/DeviceRGB") return false;

        var filter = FilterOf(img);
        if (filter is null) return false;

        var oldLen = img.Stream.Value.Length;
        SixLabors.ImageSharp.Image? decoded = null;
        try
        {
            if (filter == "/DCTDecode")
            {
                try
                {
                    decoded = gray
                        ? SixLabors.ImageSharp.Image.Load<L8>(img.Stream.Value)
                        : SixLabors.ImageSharp.Image.Load<Rgb24>(img.Stream.Value);
                }
                catch { return false; }
                if (decoded.Width != w || decoded.Height != h) return false;
            }
            else if (filter == "/FlateDecode")
            {
                var raw = InflateWithPredictor(img, w, h, gray ? 1 : 3);
                if (raw is null) return false;
                if (LooksLikeGraphic(raw, gray ? 1 : 3)) return false;
                decoded = gray
                    ? SixLabors.ImageSharp.Image.LoadPixelData<L8>(raw, w, h)
                    : SixLabors.ImageSharp.Image.LoadPixelData<Rgb24>(raw, w, h);
            }
            else return false;

            var scale = Math.Min(1.0, level.TargetPpi / density);
            var nw = Math.Max(1, (int)Math.Round(w * scale));
            var nh = Math.Max(1, (int)Math.Round(h * scale));
            if (nw != w || nh != h)
                decoded.Mutate(c => c.Resize(nw, nh, KnownResamplers.Triangle));

            byte[] jpeg;
            using (var ms = new MemoryStream())
            {
                decoded.SaveAsJpeg(ms, new JpegEncoder { Quality = level.Quality });
                jpeg = ms.ToArray();
            }
            if (jpeg.Length >= oldLen) return false;

            // A greyscale picture saved by this encoder carries three colour components; label it truthfully.
            var comps = JpegComponents(jpeg);
            if (comps != 1 && comps != 3) return false;
            img.Stream.Value = jpeg;
            img.Elements.SetName("/Filter", "/DCTDecode");
            img.Elements.Remove("/DecodeParms");
            img.Elements.SetInteger("/Width", nw);
            img.Elements.SetInteger("/Height", nh);
            img.Elements.SetInteger("/BitsPerComponent", 8);
            img.Elements.SetName("/ColorSpace", comps == 1 ? "/DeviceGray" : "/DeviceRGB");
            return true;
        }
        finally { decoded?.Dispose(); }
    }

    private static string? FilterOf(PdfDictionary img)
    {
        if (!img.Elements.ContainsKey("/Filter")) return null;
        var f = img.Elements["/Filter"];
        if (f is PdfArray a)
        {
            if (a.Elements.Count != 1) return null;
            f = a.Elements[0];
        }
        return f is PdfName n ? n.Value : null;
    }

    /// <summary>Few distinct colours means a diagram or screenshot: JPEG would smear it, so leave it alone.</summary>
    private static bool LooksLikeGraphic(byte[] raw, int comps)
    {
        var seen = new HashSet<int>();
        var step = Math.Max(1, raw.Length / comps / 20000);
        for (var i = 0; i + comps <= raw.Length; i += comps * step)
        {
            var key = comps == 1 ? raw[i] : (raw[i] << 16) | (raw[i + 1] << 8) | raw[i + 2];
            seen.Add(key);
            if (seen.Count > 256) return false;
        }
        return true;
    }

    private static int JpegComponents(byte[] j)
    {
        var i = 2;
        while (i + 9 < j.Length)
        {
            if (j[i] != 0xFF) { i++; continue; }
            var marker = j[i + 1];
            if (marker is 0xC0 or 0xC1 or 0xC2) return j[i + 9];
            if (marker is 0xD8 or 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { i += 2; continue; }
            i += 2 + ((j[i + 2] << 8) | j[i + 3]);
        }
        return 0;
    }

    /// <summary>Inflates a Flate image and undoes a PNG row predictor. Null when the layout is not plain 8 bit samples.</summary>
    private static byte[]? InflateWithPredictor(PdfDictionary img, int w, int h, int comps)
    {
        byte[] inflated;
        try
        {
            using var src = new MemoryStream(img.Stream.Value, false);
            using var z = new ZLibStream(src, CompressionMode.Decompress);
            using var dst = new MemoryStream();
            z.CopyTo(dst);
            inflated = dst.ToArray();
        }
        catch { return null; }

        var predictor = 1;
        if (img.Elements.ContainsKey("/DecodeParms"))
        {
            var dp = img.Elements["/DecodeParms"];
            if (dp is PdfArray pa) { if (pa.Elements.Count != 1) return null; dp = pa.Elements[0]; }
            if (dp is PdfReference dr) dp = dr.Value;
            if (dp is not PdfDictionary pd) return null;
            if (pd.Elements.ContainsKey("/Predictor")) predictor = pd.Elements.GetInteger("/Predictor");
            if (predictor >= 10)
            {
                var colors = pd.Elements.ContainsKey("/Colors") ? pd.Elements.GetInteger("/Colors") : 1;
                var bpc = pd.Elements.ContainsKey("/BitsPerComponent") ? pd.Elements.GetInteger("/BitsPerComponent") : 8;
                var cols = pd.Elements.ContainsKey("/Columns") ? pd.Elements.GetInteger("/Columns") : 1;
                if (colors != comps || bpc != 8 || cols != w) return null;
            }
        }

        var rowLen = w * comps;
        if (predictor == 1)
            return inflated.Length == rowLen * h ? inflated : null;
        if (predictor < 10 || predictor > 15) return null;

        if (inflated.Length != (rowLen + 1) * h) return null;
        var outp = new byte[rowLen * h];
        for (var y = 0; y < h; y++)
        {
            var ft = inflated[y * (rowLen + 1)];
            var s = y * (rowLen + 1) + 1;
            var d = y * rowLen;
            for (var x = 0; x < rowLen; x++)
            {
                int cur = inflated[s + x];
                int a = x >= comps ? outp[d + x - comps] : 0;
                int b = y > 0 ? outp[d - rowLen + x] : 0;
                int c = x >= comps && y > 0 ? outp[d - rowLen + x - comps] : 0;
                int v;
                switch (ft)
                {
                    case 0: v = cur; break;
                    case 1: v = cur + a; break;
                    case 2: v = cur + b; break;
                    case 3: v = cur + ((a + b) >> 1); break;
                    case 4:
                    {
                        var p = a + b - c;
                        var pa = Math.Abs(p - a); var pb = Math.Abs(p - b); var pc = Math.Abs(p - c);
                        v = cur + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
                        break;
                    }
                    default: return null;
                }
                outp[d + x] = (byte)v;
            }
        }
        return outp;
    }

    // ----- stream compression -----

    private static int ZipPlainStreams(PdfDocument doc)
    {
        var n = 0;
        foreach (var o in doc.Internals.GetAllObjects())
        {
            if (o is not PdfDictionary d || d.Stream is null) continue;
            if (d.Elements.ContainsKey("/Filter")) continue;
            var type = Name(d, "/Type");
            if (type is "/Metadata" or "/XRef") continue;
            if (d.Stream.Value.Length < 64) continue;
            var before = d.Stream.Value.Length;
            d.Stream.Zip();
            if (d.Stream.Value.Length < before) n++;
        }
        return n;
    }
}
