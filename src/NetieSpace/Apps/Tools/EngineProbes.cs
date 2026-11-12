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
    private const int ProbeTimeoutMs = 8000;

    public (bool Ok, string Detail) Probe(EngineRef engine)
    {
        try
        {
            var key = engine.ProbeKey ?? "inproc";
            if (key == "inproc") return (true, "");
            if (key == "cloud") return (false, "cloud service");
            if (key == "windows") return Win(engine.Name);
            if (key == "winrt") return WinRt();
            if (key == "webview2") return WebView2();
            if (key == "ffmpeg") return Ffmpeg();
            if (key == "tesseract") return Tesseract();
            if (key == "whisper") return Whisper();
            if (key.StartsWith("com:", StringComparison.Ordinal)) return Com(key.Substring(4), engine.Name);
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

    private static (bool, string) WebView2()
    {
        if (!OperatingSystem.IsWindows()) return (false, "WebView2 runtime");
        const string clientKey = @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        const string clientKey32 = @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        foreach (var (hive, path) in new[]
                 {
                     (Microsoft.Win32.Registry.LocalMachine, clientKey32),
                     (Microsoft.Win32.Registry.LocalMachine, clientKey),
                     (Microsoft.Win32.Registry.CurrentUser, clientKey),
                 })
        {
            using var k = hive.OpenSubKey(path);
            var pv = k?.GetValue("pv") as string;
            if (!string.IsNullOrWhiteSpace(pv) && pv != "0.0.0.0")
                return (true, "");
        }
        return (false, "WebView2 runtime");
    }

    private static (bool, string) Com(string progId, string name)
    {
        if (!OperatingSystem.IsWindows()) return (false, name);
        using var k = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(progId + @"\CLSID");
        return k is not null ? (true, "") : (false, name);
    }

    private static IEnumerable<string> Roots()
    {
        var list = new List<string> { AppContext.BaseDirectory };
        try { list.Add(ProjectRoot.Find()); } catch { /* ignore */ }
        return list.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static (bool, string) Ffmpeg()
    {
        var exe = new List<string>();
        foreach (var root in Roots())
        {
            exe.Add(IoPath.Combine(root, "tools", "ffmpeg", "ffmpeg.exe"));
            exe.Add(IoPath.Combine(root, "tools", "ffmpeg", "ffmpeg"));
        }
        var path = exe.FirstOrDefault(IoFile.Exists) ?? FindOnPath("ffmpeg");
        if (path is null) return (false, "FFmpeg");
        return RunVersion(path, "-version", "ffmpeg") ? (true, "") : (false, "FFmpeg");
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
        var ok = RunExitsZero(py, "-c \"import " + module + "\"", ProbeTimeoutMs * 2);
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

    private static bool RunVersion(string exe, string flag, string expectInOutput)
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
}
