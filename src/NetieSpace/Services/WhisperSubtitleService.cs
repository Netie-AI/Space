using System.Diagnostics;
using System.IO;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>
/// Local AI captions via whisper.cpp (CPU). Requires whisper-cli.exe (not the deprecated main.exe stub).
/// Install: scripts\download-whisper.ps1
/// </summary>
public sealed class WhisperSubtitleService
{
    private readonly FfmpegService _ffmpeg;
    public string? LastError { get; private set; }

    public WhisperSubtitleService(FfmpegService ffmpeg) => _ffmpeg = ffmpeg;

    public static string? FindWhisperCli()
    {
        var root = EnvLoader.FindProjectRoot();
        // Prefer real CLI only — main.exe in v1.7.6+ is a 28KB deprecation stub that exits 1
        var candidates = new[]
        {
            IoPath.Combine(AppContext.BaseDirectory, "tools", "whisper", "whisper-cli.exe"),
            IoPath.Combine(root, "tools", "whisper", "whisper-cli.exe"),
        };
        foreach (var path in candidates)
        {
            if (!IoFile.Exists(path)) continue;
            try
            {
                var len = new System.IO.FileInfo(path).Length;
                if (len < 100_000) continue; // stub / corrupt
                return path;
            }
            catch { /* try next */ }
        }

        return null;
    }

    public static string? FindModel()
    {
        var root = EnvLoader.FindProjectRoot();
        var candidates = new[]
        {
            IoPath.Combine(AppContext.BaseDirectory, "tools", "whisper", "models", "ggml-small.bin"),
            IoPath.Combine(root, "tools", "whisper", "models", "ggml-small.bin"),
            IoPath.Combine(AppContext.BaseDirectory, "tools", "whisper", "ggml-small.bin"),
            IoPath.Combine(root, "tools", "whisper", "ggml-small.bin"),
        };
        return candidates.FirstOrDefault(IoFile.Exists);
    }

    public static int CountCues(string srtPath)
    {
        try
        {
            var text = IoFile.ReadAllText(srtPath);
            return System.Text.RegularExpressions.Regex
                .Split(text.Trim(), @"\r?\n\r?\n")
                .Count(block => block.Contains("-->", StringComparison.Ordinal));
        }
        catch
        {
            return 0;
        }
    }

    public async Task<string?> GenerateSrtAsync(
        string videoPath,
        IProgress<ExportProgressReport>? progress = null,
        CancellationToken ct = default)
    {
        LastError = null;
        var cli = FindWhisperCli();
        var model = FindModel();
        if (cli is null || model is null)
        {
            LastError = "Missing whisper-cli.exe — run scripts\\download-whisper.ps1 (not main.exe stub).";
            return null;
        }

        if (!_ffmpeg.IsAvailable)
        {
            LastError = "FFmpeg required to extract audio.";
            return null;
        }

        var dir = IoPath.GetDirectoryName(videoPath)!;
        var stem = IoPath.GetFileNameWithoutExtension(videoPath);
        var wav = IoPath.Combine(dir, $"_ns_whisper_{Guid.NewGuid():N}.wav");
        var outBase = IoPath.Combine(dir, $"{stem}_captions");
        var srt = outBase + ".srt";

        try
        {
            progress?.Report(new ExportProgressReport { Percent = 8, Message = "Extracting audio…" });
            var extract = await _ffmpeg.RunPublicAsync(
                $"-y -i \"{videoPath}\" -vn -acodec pcm_s16le -ar 16000 -ac 1 \"{wav}\"",
                wav, ct);
            if (!extract)
            {
                LastError = "Could not extract audio for captions.";
                return null;
            }

            progress?.Report(new ExportProgressReport { Percent = 28, Message = "Transcribing (whisper.cpp)…" });

            var threads = Math.Clamp(Environment.ProcessorCount, 2, 8);
            var psi = new ProcessStartInfo
            {
                FileName = cli,
                Arguments =
                    $"-m \"{model}\" -f \"{wav}\" -l auto -t {threads} -np -osrt -of \"{outBase}\"",
                WorkingDirectory = IoPath.GetDirectoryName(cli)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi)
                          ?? throw new InvalidOperationException("Failed to start whisper-cli");

            var stderr = new System.Text.StringBuilder();
            var whisperPercent = 28.0;
            p.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data))
                    return;
                stderr.AppendLine(e.Data);
                whisperPercent = Math.Min(88, whisperPercent + 0.35);
                progress?.Report(new ExportProgressReport
                {
                    Percent = whisperPercent,
                    Message = "Transcribing (whisper.cpp)…"
                });
            };
            p.BeginErrorReadLine();

            var stdout = await p.StandardOutput.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            var stderrText = stderr.ToString();

            // Prefer exact path; also accept whisper writing into cwd
            if (!IoFile.Exists(srt))
            {
                var alt = IoPath.Combine(IoPath.GetDirectoryName(cli)!, IoPath.GetFileName(srt));
                if (IoFile.Exists(alt))
                {
                    IoFile.Copy(alt, srt, overwrite: true);
                    try { IoFile.Delete(alt); } catch { /* ignore */ }
                }
            }

            if (!IoFile.Exists(srt))
            {
                var detail = string.IsNullOrWhiteSpace(stderrText) ? stdout.Trim() : stderrText.Trim();
                if (detail.Length > 280) detail = detail[^280..];
                LastError = string.IsNullOrWhiteSpace(detail)
                    ? $"whisper exited {p.ExitCode} — no SRT written."
                    : $"whisper exited {p.ExitCode}: {detail}";
                return null;
            }

            progress?.Report(new ExportProgressReport { Percent = 92, Message = "Normalizing timings…" });
            NormalizeSrtTimings(srt);
            progress?.Report(new ExportProgressReport { Percent = 100, Message = "Captions ready" });

            AppLogger.Info($"Captions → {srt}");
            return srt;
        }
        finally
        {
            try { if (IoFile.Exists(wav)) IoFile.Delete(wav); } catch { /* ignore */ }
        }
    }

    /// <summary>Ensure each cue ends before the next starts (whisper sometimes overlaps).</summary>
    public static void NormalizeSrtTimings(string srtPath)
    {
        try
        {
            var text = IoFile.ReadAllText(srtPath);
            var blocks = System.Text.RegularExpressions.Regex.Split(text.Trim(), @"\r?\n\r?\n");
            var cues = new List<(TimeSpan Start, TimeSpan End, string Body)>();
            foreach (var block in blocks)
            {
                var lines = block.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length < 2) continue;
                var arrow = lines.FirstOrDefault(l => l.Contains("-->", StringComparison.Ordinal));
                if (arrow is null) continue;
                var parts = arrow.Split("-->", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) continue;
                if (!TryParseSrtTime(parts[0], out var a) || !TryParseSrtTime(parts[1], out var b))
                    continue;
                var bodyStart = Array.IndexOf(lines, arrow) + 1;
                var body = string.Join("\n", lines.Skip(bodyStart));
                if (b <= a) b = a + TimeSpan.FromMilliseconds(400);
                cues.Add((a, b, body));
            }

            cues.Sort((x, y) => x.Start.CompareTo(y.Start));
            for (var i = 0; i < cues.Count - 1; i++)
            {
                var cur = cues[i];
                var next = cues[i + 1];
                if (cur.End > next.Start)
                    cues[i] = (cur.Start, next.Start - TimeSpan.FromMilliseconds(40), cur.Body);
            }

            using var sw = new StreamWriter(srtPath, false, new System.Text.UTF8Encoding(false));
            for (var i = 0; i < cues.Count; i++)
            {
                var (a, b, body) = cues[i];
                sw.WriteLine(i + 1);
                sw.WriteLine($"{FormatSrt(a)} --> {FormatSrt(b)}");
                sw.WriteLine(body);
                sw.WriteLine();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"SRT normalize: {ex.Message}");
        }
    }

    private static bool TryParseSrtTime(string s, out TimeSpan t)
    {
        t = default;
        s = s.Trim().Replace(',', '.');
        return TimeSpan.TryParseExact(s, @"hh\:mm\:ss\.fff", null, out t)
               || TimeSpan.TryParseExact(s, @"h\:mm\:ss\.fff", null, out t);
    }

    private static string FormatSrt(TimeSpan t) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}";
}
