using System.Collections.Generic;
using NetieSpace.Apps.Tools;

namespace NetieSpace.Apps.Pdf;

/// <summary>
/// Fourth wave of PDF runners on engines that run anywhere: pdf-repair (PdfPig read, PdfPig writer) and
/// pdf-sign-self (PdfSharpCore stamp, optional PKCS#7 through the in-box SignedCms).
/// </summary>
public sealed class PdfTool4Provider : IToolProvider
{
    public ToolApp App => ToolApp.Pdf;

    public IEnumerable<(string ToolId, ToolRunnerDelegate Run)> Runners()
    {
        yield return ("pdf-repair", PdfRepairTool.RunAsync);
        yield return ("pdf-sign-self", PdfSignSelfTool.RunAsync);
    }
}
