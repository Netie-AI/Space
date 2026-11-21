using System.Collections.Generic;
using NetieSpace.Apps.Tools;

namespace NetieSpace.Apps.Pdf;

/// <summary>
/// The PDF runners built on PdfSharpCore and PdfPig beyond pdf-extract-text. A tool appears here only
/// when its output was produced from a real input and read back in the tests.
/// </summary>
public sealed class PdfEditToolProvider : IToolProvider
{
    public ToolApp App => ToolApp.Pdf;

    public IEnumerable<(string ToolId, ToolRunnerDelegate Run)> Runners()
    {
        yield return ("pdf-merge", PdfOrganizeTools.MergeAsync);
        yield return ("pdf-split", PdfOrganizeTools.SplitAsync);
        yield return ("pdf-remove-pages", PdfOrganizeTools.RemovePagesAsync);
        yield return ("pdf-extract-pages", PdfOrganizeTools.ExtractPagesAsync);
        yield return ("pdf-organize", PdfOrganizeTools.OrganizeAsync);
        yield return ("pdf-rotate", PdfEditTools.RotateAsync);
        yield return ("pdf-page-numbers", PdfEditTools.PageNumbersAsync);
        yield return ("pdf-watermark", PdfEditTools.WatermarkAsync);
        yield return ("pdf-crop", PdfEditTools.CropAsync);
        yield return ("pdf-protect", PdfSecurityTools.ProtectAsync);
        yield return ("pdf-unlock", PdfSecurityTools.UnlockAsync);
        yield return ("pdf-from-jpg", PdfImageTools.FromImagesAsync);
        yield return ("pdf-compare", PdfTextTools.CompareAsync);
        yield return ("pdf-to-markdown", PdfTextTools.ToMarkdownAsync);
    }
}
