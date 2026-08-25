using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>
/// Local-first OCR. Tesseract (when installed) and Windows OCR are tried on-device.
/// Cloud OCR (Baidu) stays opt-in via <c>NETIE_SPACE_OCR_CLOUD=1</c> — never the default.
/// </summary>
public static class ImageOcrService
{
    public const string CloudEnvName = "NETIE_SPACE_OCR_CLOUD";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public sealed record OcrResult(bool Ok, string Text, string Engine, string? Error = null);

    /// <summary>One line (or word cluster) in image pixels, origin top-left.</summary>
    public sealed record OcrBox(string Text, double X, double Y, double Width, double Height);

    public sealed record OcrLayoutResult(
        bool Ok,
        string FullText,
        string Engine,
        int ImageWidth,
        int ImageHeight,
        IReadOnlyList<OcrBox> Lines,
        string? Error = null);

    /// <summary>Cloud OCR sends the image off the machine, so it is opt-in.</summary>
    public static bool IsCloudEnabled
    {
        get
        {
            var v = Environment.GetEnvironmentVariable(CloudEnvName);
            return string.Equals(v, "1", StringComparison.Ordinal)
                   || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool IsLocalAvailable =>
        FindTesseract() is not null || FindPowerShell() is not null;

    public static async Task<OcrResult> RecognizeAsync(string imagePath, CancellationToken ct = default)
    {
        var layout = await RecognizeLayoutAsync(imagePath, ct).ConfigureAwait(false);
        return new OcrResult(layout.Ok, layout.FullText, layout.Engine, layout.Error);
    }

    public static async Task<OcrLayoutResult> RecognizeLayoutAsync(string imagePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return Fail("none", "Image missing");

        var (imgW, imgH) = ProbeImageSize(imagePath);

        // Tesseract first when installed — TSV boxes are the most usable seed for fill overlays.
        OcrLayoutResult? best = null;
        if (FindTesseract() is not null)
        {
            foreach (var psm in new[] { 6, 3, 11 })
            {
                var attempt = await TryTesseractLayoutAsync(imagePath, imgW, imgH, psm, ct).ConfigureAwait(false);
                if (attempt.Ok && (best is null || attempt.FullText.Trim().Length > best.FullText.Trim().Length))
                    best = attempt;
                if (best is { FullText.Length: >= 20 } && best.Lines.Count > 0)
                    return best;
            }
        }

        // Windows OCR is zero-install on Windows 10+ (PowerShell WinRT). Used when Tesseract
        // is missing or the local read was too thin to seed overlays.
        if (best is null || best.FullText.Trim().Length < 12 || best.Lines.Count == 0)
        {
            var win = await TryWindowsOcrAsync(imagePath, imgW, imgH, ct).ConfigureAwait(false);
            if (win.Ok && (best is null || win.FullText.Trim().Length > best.FullText.Trim().Length))
                best = win;
        }

        if (IsCloudEnabled)
        {
            var baidu = await TryBaiduAsync(imagePath, ct).ConfigureAwait(false);
            if (baidu.Ok)
            {
                return new OcrLayoutResult(
                    true, baidu.Text, baidu.Engine, imgW, imgH,
                    LinesFromPlainText(baidu.Text, imgW, imgH), null);
            }
        }

        if (best is { Ok: true } && best.FullText.Trim().Length > 0)
            return best;

        var engine = best?.Engine ?? (FindTesseract() is null && FindPowerShell() is null ? "none" : best?.Engine ?? "none");
        var err = best?.Error
                  ?? (IsCloudEnabled
                      ? "OCR unavailable"
                      : "OCR unavailable — install Tesseract, or set NETIE_SPACE_OCR_CLOUD=1 for cloud OCR");
        return Fail(engine, err, imgW, imgH);
    }

    /// <summary>
    /// Map pixel boxes (top-left) onto a PDF page (points, origin bottom-left)
    /// using the same conversion as stamps / highlights.
    /// </summary>
    public static IReadOnlyList<PdfFillTextItem> ToFillTexts(
        OcrLayoutResult layout,
        int page,
        double pageWidthPt,
        double pageHeightPt,
        int maxBoxes = 80)
    {
        if (!layout.Ok || layout.ImageWidth <= 0 || layout.ImageHeight <= 0)
            return [];

        var items = new List<PdfFillTextItem>();
        foreach (var line in layout.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text) || line.Width < 2 || line.Height < 2)
                continue;

            var (xPt, yFromBottom, wPt, hPt) = PdfJsViewerService.CanvasToPdfBottomLeft(
                line.X, line.Y, line.Width, line.Height,
                layout.ImageWidth, layout.ImageHeight,
                pageWidthPt, pageHeightPt);
            var fontPt = Math.Clamp(hPt * 0.72, 6, 28);
            items.Add(new PdfFillTextItem(page, xPt, yFromBottom, Math.Max(8, wPt), Math.Max(8, hPt), fontPt, line.Text.Trim()));
            if (items.Count >= maxBoxes)
                break;
        }

        return items;
    }

    private static OcrLayoutResult Fail(string engine, string error, int w = 0, int h = 0) =>
        new(false, "", engine, w, h, [], error);

    private static (int Width, int Height) ProbeImageSize(string path)
    {
        try
        {
            using var img = Image.FromFile(path);
            return (img.Width, img.Height);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static async Task<OcrLayoutResult> TryTesseractLayoutAsync(
        string imagePath, int imgW, int imgH, int psm, CancellationToken ct)
    {
        var exe = FindTesseract();
        if (exe is null)
            return Fail("none", "Tesseract not installed", imgW, imgH);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"\"{imagePath}\" stdout -l eng --psm {psm} tsv",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null)
                return Fail("tesseract", "Failed to start", imgW, imgH);

            var stdout = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var lines = ParseTesseractTsv(stdout);
            var text = string.Join("\n", lines.Select(l => l.Text).Where(t => t.Length > 0)).Trim();
            if (imgW <= 0 || imgH <= 0)
                InferSizeFromBoxes(lines, out imgW, out imgH);
            return new OcrLayoutResult(text.Length > 0, text, "tesseract", imgW, imgH, lines, null);
        }
        catch (Exception ex)
        {
            return Fail("tesseract", ex.Message, imgW, imgH);
        }
    }

    internal static IReadOnlyList<OcrBox> ParseTesseractTsv(string tsv)
    {
        // TSV columns: level page_num block_num par_num line_num word_num left top width height conf text
        // level 5 = word. Group by (page, block, par, line) so one overlay per line.
        var groups = new Dictionary<(int, int, int, int), List<(int Left, int Top, int W, int H, string Text)>>();
        foreach (var raw in tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.StartsWith("level", StringComparison.OrdinalIgnoreCase))
                continue;
            var cols = raw.Split('\t');
            if (cols.Length < 12)
                continue;
            if (!int.TryParse(cols[0], out var level) || level != 5)
                continue;
            if (!int.TryParse(cols[1], out var page) ||
                !int.TryParse(cols[2], out var block) ||
                !int.TryParse(cols[3], out var par) ||
                !int.TryParse(cols[4], out var line) ||
                !int.TryParse(cols[6], out var left) ||
                !int.TryParse(cols[7], out var top) ||
                !int.TryParse(cols[8], out var w) ||
                !int.TryParse(cols[9], out var h))
                continue;
            var word = cols[11].Trim();
            if (word.Length == 0)
                continue;
            var key = (page, block, par, line);
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }
            list.Add((left, top, w, h, word));
        }

        var boxes = new List<OcrBox>();
        foreach (var words in groups.Values)
        {
            var left = words.Min(w => w.Left);
            var top = words.Min(w => w.Top);
            var right = words.Max(w => w.Left + w.W);
            var bottom = words.Max(w => w.Top + w.H);
            var text = string.Join(" ", words.Select(w => w.Text));
            boxes.Add(new OcrBox(text, left, top, Math.Max(1, right - left), Math.Max(1, bottom - top)));
        }

        return boxes;
    }

    private static void InferSizeFromBoxes(IReadOnlyList<OcrBox> lines, out int w, out int h)
    {
        if (lines.Count == 0)
        {
            w = 0;
            h = 0;
            return;
        }

        w = (int)Math.Ceiling(lines.Max(l => l.X + l.Width));
        h = (int)Math.Ceiling(lines.Max(l => l.Y + l.Height));
    }

    private static async Task<OcrLayoutResult> TryWindowsOcrAsync(
        string imagePath, int imgW, int imgH, CancellationToken ct)
    {
        var ps = FindPowerShell();
        if (ps is null)
            return Fail("windows", "PowerShell not found", imgW, imgH);

        var scriptPath = IoPath.Combine(IoPath.GetTempPath(), $"netie_winocr_{Guid.NewGuid():N}.ps1");
        var jsonPath = IoPath.Combine(IoPath.GetTempPath(), $"netie_winocr_{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(scriptPath, WindowsOcrScript, ct).ConfigureAwait(false);
            var psi = new ProcessStartInfo
            {
                FileName = ps,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -ImagePath \"{imagePath}\" -OutJson \"{jsonPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null)
                return Fail("windows", "Failed to start PowerShell", imgW, imgH);

            var finished = await Task.Run(() => proc.WaitForExit(18_000), ct).ConfigureAwait(false);
            if (!finished)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return Fail("windows", "Windows OCR timed out", imgW, imgH);
            }

            if (!File.Exists(jsonPath))
                return Fail("windows", "Windows OCR produced no output", imgW, imgH);

            var json = await File.ReadAllTextAsync(jsonPath, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False)
            {
                var err = root.TryGetProperty("error", out var e) ? e.GetString() : "Windows OCR failed";
                return Fail("windows", err ?? "Windows OCR failed", imgW, imgH);
            }

            var text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            var lines = new List<OcrBox>();
            if (root.TryGetProperty("lines", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    var lineText = el.TryGetProperty("text", out var lt) ? lt.GetString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(lineText))
                        continue;
                    lines.Add(new OcrBox(
                        lineText.Trim(),
                        el.TryGetProperty("x", out var x) ? x.GetDouble() : 0,
                        el.TryGetProperty("y", out var y) ? y.GetDouble() : 0,
                        el.TryGetProperty("w", out var w) ? w.GetDouble() : 0,
                        el.TryGetProperty("h", out var h) ? h.GetDouble() : 0));
                }
            }

            if (lines.Count == 0 && text.Length > 0)
                lines.AddRange(LinesFromPlainText(text, imgW, imgH));

            return new OcrLayoutResult(text.Trim().Length > 0, text.Trim(), "windows", imgW, imgH, lines, null);
        }
        catch (Exception ex)
        {
            return Fail("windows", ex.Message, imgW, imgH);
        }
        finally
        {
            try { if (File.Exists(scriptPath)) File.Delete(scriptPath); } catch { /* ignore */ }
            try { if (File.Exists(jsonPath)) File.Delete(jsonPath); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Windows PowerShell 5.1 WinRT OCR. Writes JSON {ok,text,lines:[{text,x,y,w,h}]} .
    /// </summary>
    private const string WindowsOcrScript = """
param([string]$ImagePath,[string]$OutJson)
$ErrorActionPreference = 'Stop'
function Write-Ocr($obj) {
  $obj | ConvertTo-Json -Compress -Depth 6 | Set-Content -Path $OutJson -Encoding UTF8
}
try {
  Add-Type -AssemblyName System.Runtime.WindowsRuntime | Out-Null
  $asTaskGeneric = [System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } |
    Select-Object -First 1
  function Await-WinRt($op, [type]$resultType) {
    $asTask = $asTaskGeneric.MakeGenericMethod($resultType).Invoke($null, @($op))
    $null = $asTask.Wait(15000)
    return $asTask.Result
  }
  [Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime] | Out-Null
  [Windows.Graphics.Imaging.BitmapDecoder,Windows.Graphics.Imaging,ContentType=WindowsRuntime] | Out-Null
  [Windows.Media.Ocr.OcrEngine,Windows.Foundation,ContentType=WindowsRuntime] | Out-Null
  $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
  if (-not $engine) {
    $eng = New-Object Windows.Globalization.Language 'en'
    $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($eng)
  }
  if (-not $engine) { Write-Ocr @{ ok = $false; error = 'No Windows OCR language pack' }; exit 0 }
  $file = Await-WinRt ([Windows.Storage.StorageFile]::GetFileFromPathAsync($ImagePath)) ([Windows.Storage.StorageFile])
  $stream = Await-WinRt ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
  $decoder = Await-WinRt ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
  $bitmap = Await-WinRt ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
  $ocr = Await-WinRt ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
  $lines = @()
  foreach ($line in $ocr.Lines) {
    $left = [double]::MaxValue; $top = [double]::MaxValue; $right = 0; $bottom = 0
    foreach ($w in $line.Words) {
      $r = $w.BoundingRect
      if ($r.X -lt $left) { $left = $r.X }
      if ($r.Y -lt $top) { $top = $r.Y }
      if (($r.X + $r.Width) -gt $right) { $right = $r.X + $r.Width }
      if (($r.Y + $r.Height) -gt $bottom) { $bottom = $r.Y + $r.Height }
    }
    if ($left -eq [double]::MaxValue) { continue }
    $lines += @{ text = $line.Text; x = [math]::Round($left,1); y = [math]::Round($top,1); w = [math]::Round($right-$left,1); h = [math]::Round($bottom-$top,1) }
  }
  Write-Ocr @{ ok = $true; text = $ocr.Text; lines = $lines }
} catch {
  Write-Ocr @{ ok = $false; error = $_.Exception.Message }
}
""";

    private static IReadOnlyList<OcrBox> LinesFromPlainText(string text, int imgW, int imgH)
    {
        var rows = text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rows.Length == 0)
            return [];

        var w = imgW > 0 ? imgW : 800;
        var h = imgH > 0 ? imgH : 1100;
        var lineH = Math.Max(14.0, h / Math.Max(8, rows.Length + 2));
        var boxes = new List<OcrBox>();
        for (var i = 0; i < rows.Length; i++)
        {
            var pad = w * 0.06;
            boxes.Add(new OcrBox(rows[i], pad, lineH * (i + 0.4), w - pad * 2, lineH * 0.85));
        }
        return boxes;
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
                    var locator = OperatingSystem.IsWindows() ? "where" : "which";
                    var psi = new ProcessStartInfo
                    {
                        FileName = locator,
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

    private static string? FindPowerShell()
    {
        foreach (var c in new[]
                 {
                     @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                     "powershell.exe"
                 })
        {
            if (c.Contains('\\', StringComparison.Ordinal) && File.Exists(c))
                return c;
            if (!c.Contains('\\', StringComparison.Ordinal) && OperatingSystem.IsWindows())
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
