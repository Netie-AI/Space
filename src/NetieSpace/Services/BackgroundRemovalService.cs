using System.Diagnostics;
using System.Text.Json;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

public sealed class BackgroundRemovalResult
{
    public bool Success { get; init; }
    public bool Available { get; init; }
    public string? OutputPath { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Optional offline BG removal via Python rembg (onnxruntime U2-Net).
/// Install: .\.venv\Scripts\pip install rembg — see optional/README.md.
/// </summary>
public sealed class BackgroundRemovalService
{
    private readonly string _pythonPath;
    private readonly string _scriptPath;
    private readonly string _projectRoot;
    private bool? _rembgCached;

    public BackgroundRemovalService()
    {
        _projectRoot = ResolveProjectRoot();
        _pythonPath = ResolvePython(_projectRoot);
        _scriptPath = IoPath.Combine(_projectRoot, "tools", "remove_bg.py");
        if (!IoFile.Exists(_scriptPath))
            _scriptPath = IoPath.Combine(EnvLoader.FindProjectRoot(), "tools", "remove_bg.py");

        AppLogger.Info($"BG removal script={_scriptPath} python={_pythonPath}");
    }

    public bool IsScriptPresent => IoFile.Exists(_scriptPath);

    public async Task<bool> IsRembgAvailableAsync(CancellationToken ct = default)
    {
        if (_rembgCached is bool cached)
            return cached;

        if (!IsScriptPresent)
        {
            _rembgCached = false;
            return false;
        }

        var result = await RunAsync("check", null, null, ct);
        _rembgCached = result.Available && result.Success;
        return _rembgCached.Value;
    }

    public async Task<(bool Ok, string Message)> EnsureInstalledAsync(CancellationToken ct = default)
    {
        if (await IsRembgAvailableAsync(ct))
            return (true, "rembg ready");

        var pip = IoPath.Combine(IoPath.GetDirectoryName(_pythonPath) ?? "", "pip.exe");
        if (!IoFile.Exists(pip))
            pip = _pythonPath;

        var req = IoPath.Combine(_projectRoot, "optional", "requirements-rembg.txt");
        var args = IoFile.Exists(req)
            ? $"-m pip install -r \"{req}\""
            : "-m pip install rembg";

        // Bypass execution policy via powershell wrapper when needed; prefer direct python -m pip
        var psi = new ProcessStartInfo
        {
            FileName = _pythonPath,
            Arguments = args,
            WorkingDirectory = _projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            AppLogger.Info($"Auto-install rembg: {_pythonPath} {args}");
            using var process = Process.Start(psi);
            if (process is null)
                return (false, "Could not start pip.");

            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            _rembgCached = null;

            if (process.ExitCode != 0)
            {
                // Fallback: PowerShell -ExecutionPolicy Bypass
                var ps = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments =
                        $"-NoProfile -ExecutionPolicy Bypass -Command \"& '{_pythonPath}' -m pip install rembg\"",
                    WorkingDirectory = _projectRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p2 = Process.Start(ps);
                if (p2 is null)
                    return (false, TrimInstallError(stderr, stdout));
                await p2.WaitForExitAsync(ct);
                _rembgCached = null;
                if (p2.ExitCode != 0)
                    return (false, TrimInstallError(await p2.StandardError.ReadToEndAsync(ct), stderr));
            }

            var ok = await IsRembgAvailableAsync(ct);
            return ok
                ? (true, "rembg installed")
                : (false, "pip finished but rembg check failed — restart Netie Space.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static string TrimInstallError(string a, string b)
    {
        var s = string.IsNullOrWhiteSpace(a) ? b : a;
        s = s.Trim();
        return s.Length > 240 ? s[^240..] : s;
    }

    public async Task<BackgroundRemovalResult> RemoveAsync(
        string inputPath,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        if (!IsScriptPresent)
        {
            return new BackgroundRemovalResult
            {
                Success = false,
                Available = false,
                Error =
                    "tools/remove_bg.py not found. Rebuild with project root, or copy tools/ next to the exe. " +
                    "See optional/README.md."
            };
        }

        if (!IoFile.Exists(_pythonPath) && _pythonPath == "python")
        {
            return new BackgroundRemovalResult
            {
                Success = false,
                Available = false,
                Error =
                    "No project .venv Python found. From repo root: python -m venv .venv && " +
                    ".\\.venv\\Scripts\\pip install -r optional\\requirements-rembg.txt " +
                    "— or republish with scripts\\publish.ps1 -IncludeVenv."
            };
        }

        var output = string.IsNullOrWhiteSpace(outputPath)
            ? SiblingNobgPng(inputPath)
            : outputPath;

        var result = await RunAsync("remove", inputPath, output, ct);
        if (result.Available)
            _rembgCached = true;
        else if (result.Error?.Contains("rembg not installed", StringComparison.OrdinalIgnoreCase) == true)
            _rembgCached = false;

        return result;
    }

    public static string SiblingNobgPng(string inputPath)
    {
        var dir = IoPath.GetDirectoryName(inputPath) ?? "";
        var stem = IoPath.GetFileNameWithoutExtension(inputPath);
        return IoPath.Combine(dir, $"{stem}_nobg.png");
    }

    private async Task<BackgroundRemovalResult> RunAsync(
        string action,
        string? inputPath,
        string? outputPath,
        CancellationToken ct)
    {
        var args = action == "check"
            ? $"\"{_scriptPath}\" check"
            : $"\"{_scriptPath}\" remove \"{inputPath}\" \"{outputPath}\"";

        var psi = new ProcessStartInfo
        {
            FileName = _pythonPath,
            Arguments = args,
            WorkingDirectory = _projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = new Process { StartInfo = psi };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                var jsonLine = stdout
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .LastOrDefault(l => l.StartsWith('{'));
                if (jsonLine is not null)
                {
                    using var doc = JsonDocument.Parse(jsonLine);
                    var root = doc.RootElement;
                    return new BackgroundRemovalResult
                    {
                        Success = root.TryGetProperty("success", out var s) && s.GetBoolean(),
                        Available = !root.TryGetProperty("available", out var a) || a.GetBoolean(),
                        OutputPath = root.TryGetProperty("output_path", out var p) ? p.GetString() : outputPath,
                        Error = root.TryGetProperty("error", out var e) ? e.GetString() : null
                    };
                }
            }

            return new BackgroundRemovalResult
            {
                Success = false,
                Available = false,
                Error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr.Trim()
            };
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"BG removal failed: {ex.Message}");
            return new BackgroundRemovalResult
            {
                Success = false,
                Available = false,
                Error = ex.Message
            };
        }
    }

    private static string ResolveProjectRoot()
    {
        var root = EnvLoader.FindProjectRoot();
        if (IoDirectory.Exists(IoPath.Combine(root, ".venv", "Scripts")))
            return root;

        var parent = IoDirectory.GetParent(root);
        while (parent is not null)
        {
            if (IoDirectory.Exists(IoPath.Combine(parent.FullName, ".venv", "Scripts")) &&
                IoFile.Exists(IoPath.Combine(parent.FullName, "tools", "remove_bg.py")))
                return parent.FullName;

            if (IoFile.Exists(IoPath.Combine(parent.FullName, "NetieSpace.sln")))
                return parent.FullName;

            parent = parent.Parent;
        }

        return root;
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
