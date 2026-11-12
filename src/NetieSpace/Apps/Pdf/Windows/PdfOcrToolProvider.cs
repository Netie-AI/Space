using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace NetieSpace.Apps.Pdf.WinRt;

/// <summary>
/// pdf-ocr on the Windows OCR engine: each page is rendered at 300 dpi and recognised; the result is one
/// plain-text file per PDF. No text layer is written into a PDF. Compiled only for the Windows TFM.
/// </summary>
public sealed class PdfOcrToolProvider : IToolProvider
{
    public const int OcrDpi = 300;

    public ToolApp App => ToolApp.Pdf;

    public IEnumerable<(string ToolId, ToolRunnerDelegate Run)> Runners()
    {
        yield return ("pdf-ocr", RunAsync);
    }

    /// <summary>OCR languages installed on this PC, as BCP-47 tags.</summary>
    public static IReadOnlyList<string> InstalledLanguages() =>
        OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToList();

    /// <summary>
    /// Creates the recogniser. Throws a message naming the missing language pack when there is none,
    /// either for the asked tag or for every language of the Windows user profile.
    /// </summary>
    public static OcrEngine CreateEngine(string? languageTag)
    {
        var installed = InstalledLanguages();
        var installedText = installed.Count == 0 ? "none" : string.Join(", ", installed);

        if (!string.IsNullOrWhiteSpace(languageTag))
        {
            var tag = languageTag.Trim();
            if (!Language.IsWellFormed(tag))
                throw new ArgumentException("option 'language': '" + tag + "' is not a language tag such as en-US");
            var language = new Language(tag);
            if (!OcrEngine.IsLanguageSupported(language))
                throw new InvalidOperationException(
                    "no OCR language pack is installed for " + tag + " (installed: " + installedText + ")");
            return OcrEngine.TryCreateFromLanguage(language)
                ?? throw new InvalidOperationException("the OCR engine for " + tag + " could not be created");
        }

        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
        {
            var wanted = Windows.System.UserProfile.GlobalizationPreferences.Languages;
            throw new InvalidOperationException(
                "no OCR language pack is installed for the Windows languages of this user ("
                + string.Join(", ", wanted) + "); installed OCR languages: " + installedText);
        }
        return engine;
    }

    private static async Task<IReadOnlyList<string>> RunAsync(
        ToolRequest request, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        request.Options.TryGetValue("language", out var languageTag);
        var engine = CreateEngine(languageTag);

        var outputs = new List<string>();
        try
        {
            for (var f = 0; f < request.Files.Count; f++)
            {
                var input = IoPath.GetFullPath(request.Files[f]);
                var document = await WinPdfRender.OpenAsync(input, ct);
                var total = (int)document.PageCount;
                if (total == 0)
                    throw new InvalidOperationException("no pages in " + input);

                var text = new StringBuilder();
                var recognised = 0;
                for (var i = 0; i < total; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var pageText = await RecognisePageAsync(document, i, input, engine, ct);
                    recognised += pageText.Count(c => !char.IsWhiteSpace(c));
                    text.Append("--- page ").Append(i + 1).Append(" of ").Append(total).Append(" ---\r\n");
                    text.Append(pageText.TrimEnd()).Append("\r\n\r\n");
                    progress?.Report(new ToolProgress(
                        (int)((f + (i + 1) / (double)total) * 100 / request.Files.Count),
                        $"page {i + 1} of {total}"));
                }

                if (recognised == 0)
                    throw new InvalidOperationException(
                        "no text recognised on any page of " + input + " (language " + engine.RecognizerLanguage.LanguageTag + ")");

                var stem = IoPath.GetFileNameWithoutExtension(input);
                var outPath = WinPdfRender.FreePath(request.OutDir, stem, ".txt", outputs);
                IoFile.WriteAllText(outPath, text.ToString(), new UTF8Encoding(false));
                outputs.Add(outPath);
            }
        }
        catch
        {
            WinPdfRender.DeleteQuietly(outputs);
            throw;
        }
        return outputs;
    }

    /// <summary>Recognised text of one page, one line per recognised line.</summary>
    private static async Task<string> RecognisePageAsync(
        Windows.Data.Pdf.PdfDocument document, int index, string input, OcrEngine engine, CancellationToken ct)
    {
        using var page = document.GetPage((uint)index);

        // 300 dpi, scaled down only when the page would exceed what the engine accepts.
        var scale = 1.0;
        var longest = Math.Max(page.Size.Width, page.Size.Height) / WinPdfRender.PageUnitsPerInch * OcrDpi;
        if (longest > OcrEngine.MaxImageDimension)
            scale = OcrEngine.MaxImageDimension / longest;
        var width = (uint)Math.Max(1, Math.Round(page.Size.Width / WinPdfRender.PageUnitsPerInch * OcrDpi * scale));
        var height = (uint)Math.Max(1, Math.Round(page.Size.Height / WinPdfRender.PageUnitsPerInch * OcrDpi * scale));

        using var stream = await WinPdfRender.RenderAsync(
            page, width, height, BitmapEncoder.PngEncoderId, index + 1, input, ct);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct);
        using var bitmap = await decoder
            .GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(ct);
        var result = await engine.RecognizeAsync(bitmap).AsTask(ct);
        return string.Join("\r\n", result.Lines.Select(l => l.Text));
    }
}
