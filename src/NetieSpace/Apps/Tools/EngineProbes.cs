using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NetieSpace.Helpers;

namespace NetieSpace.Apps.Tools;

public interface IEngineProbe
{
    /// <summary>Ok = the engine is usable here. Detail = short name of what is missing when not ok.</summary>
    (bool Ok, string Detail) Probe(EngineRef engine);
}

/// <summary>
/// Honest per-engine probes (R-0011): a file existing is not enough for a runtime, so out-of-process
/// engines are executed with a version flag, COM is probed by ProgID, and Python wheels by import.
/// Distilled from the ConversionService / WhisperSubtitleService locate logic; no Services reference.
/// </summary>
public sealed class EngineProbes : IEngineProbe
{
    /// <summary>A probe that runs longer than this counts as missing.</summary>
    public const int ProbeTimeoutMs = 2000;

    private readonly IPlatformProbes _platform;
    private readonly IFfmpegInfo _ffmpeg;
    private readonly int _budgetMs;

    public EngineProbes() : this(null, null, ProbeTimeoutMs) { }

    public EngineProbes(IPlatformProbes? platform, IFfmpegInfo? ffmpeg, int budgetMs = ProbeTimeoutMs)
    {
        _platform = platform ?? new PlatformProbes();
        _ffmpeg = ffmpeg ?? new FfmpegInfo();
        _budgetMs = budgetMs;
    }

    /// <summary>Runs the probe on a worker and gives up after the budget: slow, throwing or hung = missing.</summary>
    public (bool Ok, string Detail) Probe(EngineRef engine)
    {
        var key = engine.ProbeKey ?? "inproc";
        if (key == "inproc") return (true, "");
        // A dedicated thread, not the pool: pool workers blocked waiting on a queued probe would eat the budget.
        // Engines that start a process get one more attempt: the first start of a big exe on a cold Windows
        // machine (virus scan, cold disk) can exceed the budget while the second, warm start takes milliseconds.
        var attempts = StartsProcess(key) ? 2 : 1;
        for (var i = 0; i < attempts; i++)
        {
            (bool Ok, string Detail) result = (false, engine.Name);
            var worker = new System.Threading.Thread(() => result = ProbeCore(engine)) { IsBackground = true, Name = "engine-probe" };
            worker.Start();
            if (worker.Join(_budgetMs)) return result;
        }
        return (false, engine.Name + " (probe timed out)");
    }

    private static bool StartsProcess(string key) =>
        key == "ffmpeg" || key == "tesseract" || key == "whisper"
        || key.StartsWith("ffmpeg-filters:", StringComparison.Ordinal)
        || key.StartsWith("python:", StringComparison.Ordinal);

    private (bool Ok, string Detail) ProbeCore(EngineRef engine)
    {
        try
        {
            var key = engine.ProbeKey ?? "inproc";
            if (key == "inproc") return (true, "");
            if (key == "cloud") return (false, "cloud service");
            if (key == "windows") return Win(engine.Name);
            if (key == "winrt") return WinRt();
            if (key == "winrt-ocr") return Gated(_platform.Ocr, "Windows OCR language pack");
            if (key == "winrt-speech") return Gated(_platform.Speech, "Windows speech voice");
            if (key == "webview2") return Gated(_platform.WebView2, "WebView2 runtime");
            if (key == "ffmpeg") return Ffmpeg();
            if (key.StartsWith("ffmpeg-filters:", StringComparison.Ordinal)) return FfmpegFilters(key.Substring(15));
            if (key == "tesseract") return Tesseract();
            if (key == "whisper") return Whisper();
            if (key.StartsWith("com:", StringComparison.Ordinal)) return _platform.ProgId(key.Substring(4), engine.Name);
            if (key.StartsWith("python:", StringComparison.Ordinal)) return PythonWheel(key.Substring(7));
            if (key.StartsWith("path:", StringComparison.Ordinal)) return OnPath(key.Substring(5), engine.Name);
            if (key.StartsWith("file:", StringComparison.Ordinal)) return FileProbe(key.Substring(5), engine.Name);
            return (false, engine.Name);
        }
        catch (Exception)
        {
            // A probe that throws is a probe that failed: report missing, never "present".
            return (false, engine.Name);
        }
    }

    private static (bool, string) Win(string name) =>
        OperatingSystem.IsWindows() ? (true, "") : (false, name);

    private static (bool, string) WinRt() =>
        OperatingSystem.IsWindows() && Environment.OSVersion.Version >= new Version(10, 0, 17763)
            ? (true, "")
            : (false, "Windows 10 1809 or later");

    /// <summary>The injected probe (which answers missing off Windows); a probe answering "no" with no detail gets the default name.</summary>
    private static (bool, string) Gated(Func<(bool Ok, string Detail)> probe, string name)
    {
        var (ok, detail) = probe();
        return ok ? (true, "") : (false, string.IsNullOrWhiteSpace(detail) ? name : detail);
    }

    private static IEnumerable<string> Roots()
    {
        var list = new List<string> { AppContext.BaseDirectory };
        try { list.Add(ProjectRoot.Find()); } catch { /* ignore */ }
        return list.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    internal static string? LocateFfmpeg()
    {
        var exe = new List<string>();
        foreach (var root in Roots())
        {
            exe.Add(IoPath.Combine(root, "tools", "ffmpeg", "ffmpeg.exe"));
            exe.Add(IoPath.Combine(root, "tools", "ffmpeg", "ffmpeg"));
        }
        return exe.FirstOrDefault(IoFile.Exists) ?? FindOnPath("ffmpeg");
    }

    private (bool, string) Ffmpeg()
    {
        var path = _ffmpeg.Locate();
        if (path is null) return (false, "FFmpeg");
        return _ffmpeg.Runs(path) ? (true, "") : (false, "FFmpeg");
    }

    /// <summary>
    /// Key spec: alternatives split by '|', each a comma list of filters that must all exist, e.g.
    /// <c>vidstabdetect,vidstabtransform|deshake</c>. The reason names the first alternative's filters.
    /// </summary>
    private (bool, string) FfmpegFilters(string spec)
    {
        var path = _ffmpeg.Locate();
        if (path is null || !_ffmpeg.Runs(path)) return (false, "FFmpeg");
        var have = _ffmpeg.Filters(path);
        var alts = spec.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(a => a.Split(',', StringSplitOptions.RemoveEmptyEntries)).ToList();
        var label = "FFmpeg filter " + string.Join(" or ", alts.Select(a => string.Join("+", a)));
        if (have is null) return (false, label);
        return alts.Any(a => a.All(have.Contains)) ? (true, "") : (false, label);
    }

    private static (bool, string) Tesseract()
    {
        var known = IoPath.Combine(@"C:\Program Files\Tesseract-OCR", "tesseract.exe");
        var path = FindOnPath("tesseract") ?? (IoFile.Exists(known) ? known : null);
        if (path is null) return (false, "Tesseract");
        return RunVersion(path, "--version", "tesseract") ? (true, "") : (false, "Tesseract");
    }

    private static (bool, string) Whisper()
    {
        string? cli = null;
        string? model = null;
        foreach (var root in Roots())
        {
            var c = IoPath.Combine(root, "tools", "whisper", "whisper-cli.exe");
            // main.exe in whisper.cpp 1.7.6+ is a tiny deprecation stub; a real CLI is large.
            if (cli is null && IoFile.Exists(c) && new System.IO.FileInfo(c).Length >= 100_000) cli = c;
            foreach (var m in new[]
                     {
                         IoPath.Combine(root, "tools", "whisper", "models", "ggml-small.bin"),
                         IoPath.Combine(root, "tools", "whisper", "ggml-small.bin"),
                     })
                if (model is null && IoFile.Exists(m)) model = m;
        }
        if (cli is null) return (false, "whisper.cpp");
        if (model is null) return (false, "whisper model");
        return (true, "");
    }

    private static (bool, string) PythonWheel(string module)
    {
        var py = FindPython();
        if (py is null) return (false, "python runtime");
        var ok = RunExitsZero(py, "-c \"import " + module + "\"", ProbeTimeoutMs);
        return ok ? (true, "") : (false, module == "fitz" ? "PyMuPDF" : module);
    }

    /// <summary>Same candidates ConversionService.ResolvePython tries; no PATH fallback, a shipped runtime is the claim.</summary>
    private static string? FindPython()
    {
        var candidates = new List<string>();
        foreach (var root in Roots())
        {
            candidates.Add(IoPath.Combine(root, ".venv", "Scripts", "python.exe"));
            candidates.Add(IoPath.Combine(root, ".venv312", "Scripts", "python.exe"));
            var parent = IoDirectory.GetParent(root);
            while (parent is not null)
            {
                if (IoFile.Exists(IoPath.Combine(parent.FullName, "tools", "convert_engine.py")))
                    candidates.Add(IoPath.Combine(parent.FullName, ".venv", "Scripts", "python.exe"));
                parent = parent.Parent;
            }
        }
        return candidates.FirstOrDefault(IoFile.Exists);
    }

    private static (bool, string) OnPath(string exe, string name) =>
        FindOnPath(exe) is not null ? (true, "") : (false, name);

    private static (bool, string) FileProbe(string spec, string name)
    {
        var parts = spec.Split('|');
        var rel = parts[0].Replace('/', IoPath.DirectorySeparatorChar);
        foreach (var root in Roots())
            if (IoFile.Exists(IoPath.Combine(root, rel))) return (true, "");
        return (false, parts.Length > 1 ? parts[1] : name);
    }

    private static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(IoPath.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in new[] { ".exe", ".bat", ".cmd", "" })
            {
                try
                {
                    var p = IoPath.Combine(dir.Trim('"'), name + ext);
                    if (IoFile.Exists(p)) return p;
                }
                catch { /* bad PATH entry */ }
            }
        }
        return null;
    }

    internal static bool RunVersion(string exe, string flag, string expectInOutput)
    {
        var psi = new ProcessStartInfo(exe, flag)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        if (p is null) return false;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(ProbeTimeoutMs))
        {
            try { p.Kill(true); } catch { /* gone */ }
            return false;
        }
        var text = (stdout.Result + stderr.Result);
        return p.ExitCode == 0 && text.Contains(expectInOutput, StringComparison.OrdinalIgnoreCase);
    }

    private static bool RunExitsZero(string exe, string args, int timeoutMs)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        if (p is null) return false;
        _ = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(true); } catch { /* gone */ }
            return false;
        }
        return p.ExitCode == 0;
    }

    /// <summary>Stdout plus stderr of a short process, or null when it fails, is slow or cannot start.</summary>
    internal static string? RunCapture(string exe, string args, int timeoutMs)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        if (p is null) return null;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(true); } catch { /* gone */ }
            return null;
        }
        return p.ExitCode == 0 ? stdout.Result + stderr.Result : null;
    }
}
