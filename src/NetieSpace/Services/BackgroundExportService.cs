using NetieSpace.Helpers;
using NetieSpace.Views;

namespace NetieSpace.Services;

public sealed class ExportProgressReport
{
    public double Percent { get; init; }
    public string Message { get; init; } = "";
}

/// <summary>Runs FFmpeg exports that survive PreviewWindow close (Space toggle).</summary>
public sealed class BackgroundExportService
{
    private static BackgroundExportService? _instance;
    public static BackgroundExportService Instance => _instance ??= new();

    private int _active;

    public bool IsExporting => _active > 0;

    public async Task<FfmpegResult> RunAsync(
        string label,
        string outputPath,
        Func<IProgress<ExportProgressReport>?, CancellationToken, Task<FfmpegResult>> work,
        IProgress<ExportProgressReport>? uiProgress = null,
        CancellationToken ct = default)
    {
        Interlocked.Increment(ref _active);
        try
        {
            var relay = new Progress<ExportProgressReport>(r =>
            {
                uiProgress?.Report(r);
            });

            var result = await Task.Run(() => work(relay, ct), ct);
            if (result.Success && !string.IsNullOrWhiteSpace(result.OutputPath) && IoFile.Exists(result.OutputPath))
                ToastHud.ShowExportComplete(result.OutputPath);
            else if (!result.Success)
                AppLogger.Warn($"{label} failed: {result.Error}");

            return result;
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }
}
