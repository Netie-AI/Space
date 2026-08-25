using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>
/// CPU-friendly OCR: local Tesseract first, optional Baidu cloud when keys exist.
/// </summary>
public static class ImageOcrService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public sealed record OcrResult(bool Ok, string Text, string Engine, string? Error = null);

    public static async Task<OcrResult> RecognizeAsync(string imagePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return new OcrResult(false, "", "none", "Image missing");

        // Try several page-segmentation modes before giving up locally. The old code ran
        // only --psm 3 (fully automatic), which is weak on the two shapes people
        // actually photograph: a single uniform block, and sparse text such as a
        // receipt or a form. A poor psm-3 read used to send the image to the cloud;
        // now it just means we have not tried the right mode yet.
        //
        //   3  fully automatic (default)
        //   6  a single uniform block of text
        //  11  sparse text, find as much as possible in no particular order
        var local = new OcrResult(false, "", "none", "Tesseract not installed");
        foreach (var psm in new[] { 3, 6, 11 })
        {
            var attempt = await TryTesseractAsync(imagePath, ct, psm).ConfigureAwait(false);
            if (attempt.Ok && attempt.Text.Trim().Length > local.Text.Trim().Length)
                local = attempt;
            if (local.Text.Trim().Length >= 20)
                return local;
        }

        // Cloud OCR is opt-in and off by default (Space#3). It used to fire whenever the
        // local read returned under 20 characters, which inverted the risk: the fainter
        // and more sensitive the document, the more likely it left the machine - with no
        // prompt. NETIE.md promises the customer their data does not leave unless they
        // say it can, so this now requires them to have said so.
        if (CloudOcrEnabled())
        {
            var baidu = await TryBaiduAsync(imagePath, ct).ConfigureAwait(false);
            if (baidu.Ok)
                return baidu;
        }

        if (local.Ok && local.Text.Trim().Length > 0)
            return local;

        return new OcrResult(false, "", local.Engine,
            local.Error ?? "OCR unavailable - install Tesseract, or enable cloud OCR in settings");
    }

    /// <summary>Cloud OCR sends the image off the machine, so it is opt-in.</summary>
    private static bool CloudOcrEnabled()
    {
        var v = Environment.GetEnvironmentVariable("NETIE_SPACE_OCR_CLOUD");
        return string.Equals(v, "1", StringComparison.Ordinal)
            || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<OcrResult> TryTesseractAsync(string imagePath, CancellationToken ct, int psm = 3)
    {
        var exe = FindTesseract();
        if (exe is null)
            return new OcrResult(false, "", "none", "Tesseract not installed");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"\"{imagePath}\" stdout -l eng --psm {psm}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null)
                return new OcrResult(false, "", "tesseract", "Failed to start");

            var stdout = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var text = stdout.Trim();
            return new OcrResult(text.Length > 0, text, "tesseract");
        }
        catch (Exception ex)
        {
            return new OcrResult(false, "", "tesseract", ex.Message);
        }
    }

    private static string? FindTesseract()
    {
        var candidates = new[]
        {
            @"C:\Program Files\Tesseract-OCR\tesseract.exe",
            @"C:\Program Files (x86)\Tesseract-OCR\tesseract.exe",
            "tesseract"
        };
        foreach (var c in candidates)
        {
            if (c == "tesseract")
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "where",
                        Arguments = "tesseract",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi);
                    var o = p?.StandardOutput.ReadToEnd() ?? "";
                    p?.WaitForExit(2000);
                    var line = o.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(line) && File.Exists(line))
                        return line;
                }
                catch { /* ignore */ }
                continue;
            }

            if (File.Exists(c))
                return c;
        }

        return null;
    }

    private static async Task<OcrResult> TryBaiduAsync(string imagePath, CancellationToken ct)
    {
        var apiKey = Environment.GetEnvironmentVariable("BAIDU_OCR_API_KEY")
                     ?? Environment.GetEnvironmentVariable("BAIDU_API_KEY");
        var secret = Environment.GetEnvironmentVariable("BAIDU_OCR_SECRET_KEY")
                     ?? Environment.GetEnvironmentVariable("BAIDU_SECRET_KEY");
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secret))
            return new OcrResult(false, "", "none", "No Baidu OCR keys");

        try
        {
            var tokenUrl =
                $"https://aip.baidubce.com/oauth/2.0/token?grant_type=client_credentials&client_id={Uri.EscapeDataString(apiKey)}&client_secret={Uri.EscapeDataString(secret)}";
            using var tokenRes = await Http.GetAsync(tokenUrl, ct).ConfigureAwait(false);
            var tokenJson = await tokenRes.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var tokenDoc = JsonDocument.Parse(tokenJson);
            if (!tokenDoc.RootElement.TryGetProperty("access_token", out var tokEl))
                return new OcrResult(false, "", "baidu", "Token failed");

            var token = tokEl.GetString();
            var bytes = await File.ReadAllBytesAsync(imagePath, ct).ConfigureAwait(false);
            var b64 = Convert.ToBase64String(bytes);
            var ocrUrl = $"https://aip.baidubce.com/rest/2.0/ocr/v1/general_basic?access_token={token}";
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["image"] = b64
            });
            content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
            using var ocrRes = await Http.PostAsync(ocrUrl, content, ct).ConfigureAwait(false);
            var ocrJson = await ocrRes.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var ocrDoc = JsonDocument.Parse(ocrJson);
            if (!ocrDoc.RootElement.TryGetProperty("words_result", out var words) ||
                words.ValueKind != JsonValueKind.Array)
                return new OcrResult(false, "", "baidu", "No words_result");

            var sb = new StringBuilder();
            foreach (var w in words.EnumerateArray())
            {
                if (w.TryGetProperty("words", out var line))
                    sb.AppendLine(line.GetString());
            }

            var text = sb.ToString().Trim();
            return new OcrResult(text.Length > 0, text, "baidu");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Baidu OCR: {ex.Message}");
            return new OcrResult(false, "", "baidu", ex.Message);
        }
    }
}
