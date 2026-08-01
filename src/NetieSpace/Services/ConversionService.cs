using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

public sealed class ConversionResult
{
    public bool Success { get; init; }
    public string? OutputPath { get; init; }
    public string? Tool { get; init; }
    public long Bytes { get; init; }
    public string? Error { get; init; }
}

public sealed class ConversionService
{
    private readonly string _pythonPath;
    private readonly string _enginePath;
    private readonly string _projectRoot;

    public ConversionService()
    {
        _projectRoot = ResolveProjectRoot();
        _pythonPath = ResolvePython(_projectRoot);
        _enginePath = IoPath.Combine(_projectRoot, "tools", "convert_engine.py");
        if (!IoFile.Exists(_enginePath))
            _enginePath = IoPath.Combine(EnvLoader.FindProjectRoot(), "tools", "convert_engine.py");

        AppLogger.Info($"Conversion python={_pythonPath}");
        AppLogger.Info($"Conversion engine={_enginePath} root={_projectRoot}");
    }

    public bool IsAvailable => IoFile.Exists(_enginePath);

    public Task<ConversionResult> ConvertToMarkdownAsync(string filePath, CancellationToken ct = default) =>
        RunAsync("to-md", filePath, ct);

    public Task<ConversionResult> ConvertToPdfAsync(string filePath, CancellationToken ct = default) =>
        RunAsync("to-pdf", filePath, ct);

    public Task<ConversionResult> PreviewConvertAsync(string filePath, CancellationToken ct = default) =>
        RunAsync("preview", filePath, ct);

    /// <summary>DOCX → HTML via mammoth (Space preview; not markdown).</summary>
    public Task<ConversionResult> PreviewHtmlAsync(string filePath, CancellationToken ct = default) =>
        RunAsync("preview-html", filePath, ct);

    /// <summary>PPTX → visual HTML with slide images (Space preview).</summary>
    public Task<ConversionResult> PreviewPptxHtmlAsync(string filePath, CancellationToken ct = default) =>
        RunAsync("preview-pptx-html", filePath, ct);

    /// <summary>Export to an explicit path (Share sheet Export as…).</summary>
    public Task<ConversionResult> ExportAsAsync(string filePath, string outputPath, CancellationToken ct = default) =>
        RunAsync("export-as", filePath, ct, outputPath);

    public Task<ConversionResult> PdfWatermarkAsync(
        string filePath, string outputPath, string text,
        double opacity = 0.25, int angle = 45, string pages = "all",
        CancellationToken ct = default) =>
        RunAsync("pdf-watermark", filePath, ct, outputPath,
            "--text", text, "--opacity", opacity.ToString("0.###"),
            "--angle", angle.ToString(), "--pages", pages);

    public Task<ConversionResult> PdfProtectAsync(
        string filePath, string outputPath, string password, string? owner = null,
        CancellationToken ct = default)
    {
        var args = new List<string> { "--password", password };
        if (!string.IsNullOrWhiteSpace(owner))
        {
            args.Add("--owner");
            args.Add(owner);
        }
        return RunAsync("pdf-protect", filePath, ct, outputPath, args.ToArray());
    }

    public Task<ConversionResult> PdfUnlockAsync(
        string filePath, string outputPath, string password, CancellationToken ct = default) =>
        RunAsync("pdf-unlock", filePath, ct, outputPath, "--password", password);

    public Task<ConversionResult> PdfOcrAsync(
        string filePath, string outputPath, string lang = "eng", bool force = false,
        CancellationToken ct = default)
    {
        var args = new List<string> { "--lang", lang };
        if (force) args.Add("--force");
        return RunAsync("pdf-ocr", filePath, ct, outputPath, args.ToArray());
    }

    private async Task<ConversionResult> RunAsync(
        string action, string filePath, CancellationToken ct,
        string? outputPath = null, params string[] extraArgs)
    {
        if (!IsAvailable)
        {
            return new ConversionResult
            {
                Success = false,
                Error = "Conversion engine not found. Ensure tools/convert_engine.py exists."
            };
        }

        var argParts = new List<string> { $"\"{_enginePath}\"", action, $"\"{filePath}\"" };
        if (outputPath is not null)
            argParts.Add($"\"{outputPath}\"");
        foreach (var a in extraArgs)
        {
            // Quote values that look like paths or contain spaces; leave flags bare.
            if (a.StartsWith("--", StringComparison.Ordinal) || a is "true" or "false")
                argParts.Add(a);
            else
                argParts.Add($"\"{a}\"");
        }

        var psi = new ProcessStartInfo
        {
            FileName = _pythonPath,
            Arguments = string.Join(' ', argParts),
            WorkingDirectory = _projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        PrependTesseractToPath(psi);

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        try
        {
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                // Engine may print warnings before JSON — take last JSON object line
                var jsonLine = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .LastOrDefault(l => l.StartsWith('{'));
                if (jsonLine is not null)
                {
                    using var doc = JsonDocument.Parse(jsonLine);
                    var root = doc.RootElement;
                    return new ConversionResult
                    {
                        Success = root.GetProperty("success").GetBoolean(),
                        OutputPath = root.TryGetProperty("output_path", out var p) ? p.GetString() : null,
                        Tool = root.TryGetProperty("tool", out var t) ? t.GetString() : null,
                        Bytes = root.TryGetProperty("bytes", out var b) ? b.GetInt64() : 0,
                        Error = root.TryGetProperty("error", out var e) ? e.GetString() : null
                    };
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Conversion JSON parse failed: {ex.Message}");
        }

        return new ConversionResult
        {
            Success = false,
            Error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr
        };
    }

    private static string ResolveProjectRoot()
    {
        var root = EnvLoader.FindProjectRoot();
        // Portable dist has tools/ but .venv lives in the repo root above dist\
        if (IoDirectory.Exists(IoPath.Combine(root, ".venv", "Scripts")))
            return root;

        var parent = IoDirectory.GetParent(root);
        while (parent is not null)
        {
            if (IoDirectory.Exists(IoPath.Combine(parent.FullName, ".venv", "Scripts")) &&
                IoFile.Exists(IoPath.Combine(parent.FullName, "tools", "convert_engine.py")))
                return parent.FullName;

            if (IoFile.Exists(IoPath.Combine(parent.FullName, "NetieSpace.sln")))
                return parent.FullName;

            parent = parent.Parent;
        }

        return root;
    }

    /// <summary>
    /// ocrmypdf/pdf-ocr in convert_engine call <c>tesseract</c> via PATH — Windows default install is not always on PATH.
    /// </summary>
    private static void PrependTesseractToPath(ProcessStartInfo psi)
    {
        const string tesseractDir = @"C:\Program Files\Tesseract-OCR";
        var tesseractExe = IoPath.Combine(tesseractDir, "tesseract.exe");
        if (!IoFile.Exists(tesseractExe))
            return;

        var existing = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (existing.Contains(tesseractDir, StringComparison.OrdinalIgnoreCase))
            return;

        if (psi.Environment.Count == 0)
        {
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
                psi.Environment[entry.Key.ToString()!] = entry.Value?.ToString() ?? "";
        }

        psi.Environment["PATH"] = tesseractDir + IoPath.PathSeparator + existing;
    }

    private static string ResolvePython(string projectRoot)
    {
        var candidates = new[]
        {
            IoPath.Combine(projectRoot, ".venv", "Scripts", "python.exe"),
            IoPath.Combine(projectRoot, ".venv312", "Scripts", "python.exe"),
            IoPath.Combine(AppContext.BaseDirectory, ".venv", "Scripts", "python.exe"),
        };

        foreach (var c in candidates)
        {
            if (IoFile.Exists(c))
                return c;
        }

        return "python";
    }
}
