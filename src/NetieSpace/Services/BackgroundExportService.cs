using NetieSpace.Helpers;
using NetieSpace.Views;

namespace NetieSpace.Services;

public sealed class ExportProgressReport
{
    public double Percent { get; init; }
    public string Message { get; init; } = "";
}

public sealed class CaptionJobResult
{
    public bool Success { get; init; }
    public string? SrtPath { get; init; }
    public string? Error { get; init; }
    public int CueCount { get; init; }
}

/// <summary>Runs FFmpeg exports and other long jobs that survive PreviewWindow close (Space toggle).</summary>
public sealed class BackgroundExportService
{
    private static BackgroundExportService? _instance;
    public static BackgroundExportService Instance => _instance ??= new();

    private int _activeExports;
    private int _activeCaptions;

    public bool IsExporting => _activeExports > 0;
    public bool IsCaptionGenerating => _activeCaptions > 0;

    public async Task<FfmpegResult> RunAsync(
        string label,
        string outputPath,
        Func<IProgress<ExportProgressReport>?, CancellationToken, Task<FfmpegResult>> work,
        IProgress<ExportProgressReport>? uiProgress = null,
        CancellationToken ct = default)
    {
        Interlocked.Increment(ref _activeExports);
        try
        {
            var result = await RunJobAsync(label, work, uiProgress, ct);
            if (result.Success && !string.IsNullOrWhiteSpace(result.OutputPath) && IoFile.Exists(result.OutputPath))
                ToastHud.ShowExportComplete(result.OutputPath);
            else if (!result.Success)
                AppLogger.Warn($"{label} failed: {result.Error}");

            return result;
        }
        finally
        {
            Interlocked.Decrement(ref _activeExports);
        }
    }

    public async Task<CaptionJobResult> RunCaptionAsync(
        string videoPath,
        FfmpegService ffmpeg,
        IProgress<ExportProgressReport>? uiProgress = null,
        CancellationToken ct = default)
    {
        Interlocked.Increment(ref _activeCaptions);
        try
        {
            var svc = new WhisperSubtitleService(ffmpeg);
            var srt = await RunJobAsync(
                "Caption generation",
                async (p, token) => await svc.GenerateSrtAsync(videoPath, p, token),
                uiProgress,
                ct);

            if (srt is null)
            {
                return new CaptionJobResult
                {
                    Success = false,
                    Error = svc.LastError
                        ?? "Captions need whisper.cpp — run scripts\\download-whisper.ps1"
                };
            }

            var cueCount = WhisperSubtitleService.CountCues(srt);
            ToastHud.Show($"Captions ready ({cueCount} cues)", IoPath.GetDirectoryName(srt) ?? "");

            return new CaptionJobResult
            {
                Success = true,
                SrtPath = srt,
                CueCount = cueCount
            };
        }
        finally
        {
            Interlocked.Decrement(ref _activeCaptions);
        }
    }

    private static async Task<T> RunJobAsync<T>(
        string label,
        Func<IProgress<ExportProgressReport>?, CancellationToken, Task<T>> work,
        IProgress<ExportProgressReport>? uiProgress = null,
        CancellationToken ct = default)
    {
        var relay = new Progress<ExportProgressReport>(r => uiProgress?.Report(r));
        try
        {
            return await Task.Run(() => work(relay, ct), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLogger.Warn($"{label} failed: {ex.Message}");
            throw;
        }
    }
}
