using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using UglyToad.PdfPig;

namespace NetieSpace.Apps.Pdf;

/// <summary>
/// PDF tool runners. S50-E2-a ships exactly one, <c>pdf-extract-text</c> on PdfPig, to prove the
/// pipeline end to end. S50-E3 owns this file after merge.
/// </summary>
public sealed class PdfToolProvider : IToolProvider
{
    public ToolApp App => ToolApp.Pdf;

    public IEnumerable<(string ToolId, ToolRunnerDelegate Run)> Runners()
    {
        yield return ("pdf-extract-text", ExtractTextAsync);
    }

    private static Task<IReadOnlyList<string>> ExtractTextAsync(
        ToolRequest request, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            if (request.Options.TryGetValue("detailed", out var detailed) && detailed == "true")
                throw new NotSupportedException("detailed (per-run coordinates) mode is not built in this version");

            var input = request.Files[0];
            var sb = new StringBuilder();
            using (var doc = PdfDocument.Open(input))
            {
                var total = doc.NumberOfPages;
                for (var n = 1; n <= total; n++)
                {
                    ct.ThrowIfCancellationRequested();
                    var page = doc.GetPage(n);
                    if (n > 1) sb.Append('\n');
                    sb.Append(page.Text);
                    progress?.Report(new ToolProgress(n * 100 / Math.Max(1, total), "page " + n + " of " + total));
                }
            }

            if (sb.ToString().Trim().Length == 0)
                throw new InvalidOperationException("no extractable text layer in " + input + " (scanned PDF? use pdf-ocr)");

            var outPath = IoPath.Combine(request.OutDir, IoPath.GetFileNameWithoutExtension(input) + ".txt");
            IoFile.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            return new[] { outPath };
        }, ct);
    }
}
