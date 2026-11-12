using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;

namespace NetieSpace.Apps.Pdf;

/// <summary>
/// Third wave of PDF runners, all on managed engines that run anywhere: pdf-compress (PdfSharpCore),
/// pdf-to-word (PdfPig read, DocumentFormat.OpenXml write) and pdf-to-excel (same pair).
/// </summary>
public sealed class PdfTool3Provider : IToolProvider
{
    public ToolApp App => ToolApp.Pdf;

    public IEnumerable<(string ToolId, ToolRunnerDelegate Run)> Runners()
    {
        yield return ("pdf-compress", PdfCompressTool.RunAsync);
        yield return ("pdf-to-word", PdfToWordTool.RunAsync);
        yield return ("pdf-to-excel", PdfToExcelTool.RunAsync);
    }
}

internal static class PdfTool3Batch
{
    /// <summary>
    /// One output per input. All or nothing: when any input fails, outputs already written by this run are
    /// deleted and the error names the failing file, so a half-converted batch is never reported as done.
    /// </summary>
    public static Task<IReadOnlyList<string>> Run(
        ToolRequest req, IProgress<ToolProgress>? progress, CancellationToken ct,
        string suffix, string ext, Action<string, string, CancellationToken> one) =>
        PdfToolSupport.Work(ct, () =>
        {
            var names = new PdfToolSupport.OutputNamer(req.OutDir);
            var written = new List<string>();
            try
            {
                for (var i = 0; i < req.Files.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var input = req.Files[i];
                    var path = names.Next(PdfToolSupport.Stem(input) + suffix, ext);
                    try
                    {
                        one(input, path, ct);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) when (ex is not ArgumentException)
                    {
                        throw new InvalidOperationException(IoPath.GetFileName(input) + ": " + ex.Message, ex);
                    }
                    written.Add(path);
                    PdfToolSupport.Report(progress, i + 1, req.Files.Count, "done " + IoPath.GetFileName(input));
                }
            }
            catch
            {
                foreach (var w in written)
                {
                    try { if (IoFile.Exists(w)) IoFile.Delete(w); } catch { /* best effort */ }
                }
                throw;
            }
            return (IReadOnlyList<string>)written;
        });
}
