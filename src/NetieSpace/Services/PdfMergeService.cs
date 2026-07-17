using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>Backward-compatible wrapper — prefer <see cref="PdfEditService"/>.</summary>
public sealed class PdfMergeService
{
    private readonly PdfEditService _edit = new();

    public string Merge(string primaryPdf, IEnumerable<string> additionalPdfs) =>
        _edit.Merge(primaryPdf, additionalPdfs);
}
