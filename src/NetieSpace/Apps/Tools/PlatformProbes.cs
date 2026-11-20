using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NetieSpace.Apps.Tools;

/// <summary>
/// Windows runtime probes behind an interface so the rules are testable with fakes on any OS.
/// The real implementation lives under Apps/Tools/Windows (Windows only) and is found by reflection.
/// </summary>
public interface IWinRtProbes
{
    /// <summary>Ok = at least one Windows.Media.Ocr recognizer language is installed.</summary>
    (bool Ok, string Detail) Ocr();

    /// <summary>Ok = at least one speech voice is installed.</summary>
    (bool Ok, string Detail) Speech();

    /// <summary>Ok = a WebView2 runtime is installed and reports a version.</summary>
    (bool Ok, string Detail) WebView2();
}

/// <summary>Registry style probes that compile everywhere and answer "missing" off Windows.</summary>
public interface IPlatformProbes : IWinRtProbes
{
    /// <summary>Ok = the COM ProgID is registered and its server is registered too.</summary>
    (bool Ok, string Detail) ProgId(string progId, string engineName);
}

/// <summary>What the FFmpeg binary offers. Null filters = the list could not be read.</summary>
public interface IFfmpegInfo
{
    string? Locate();
    /// <summary>True when the binary runs and identifies itself as ffmpeg.</summary>
    bool Runs(string exe);
    IReadOnlySet<string>? Filters(string exe);
}

public sealed class PlatformProbes : IPlatformProbes
{
    private const string WinRtType = "NetieSpace.Apps.Tools.WinRt.WinRtProbes";
    private readonly IWinRtProbes? _winRt;

    public PlatformProbes() : this(LoadWinRt()) { }
    public PlatformProbes(IWinRtProbes? winRt) { _winRt = winRt; }

    private static IWinRtProbes? LoadWinRt()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var t = typeof(PlatformProbes).Assembly.GetType(WinRtType, throwOnError: false);
            return t is null ? null : Activator.CreateInstance(t) as IWinRtProbes;
        }
        catch (Exception) { return null; }
    }

    public (bool Ok, string Detail) Ocr() =>
        _winRt is null ? (false, "Windows OCR language pack") : _winRt.Ocr();

    public (bool Ok, string Detail) Speech() =>
        _winRt is null ? (false, "Windows speech voice") : _winRt.Speech();

    public (bool Ok, string Detail) WebView2()
    {
        if (!OperatingSystem.IsWindows()) return (false, "WebView2 runtime");
        if (_winRt is not null) return _winRt.WebView2();
        return RegistryWebView2() ? (true, "") : (false, "WebView2 runtime");
    }

    internal static bool RegistryWebView2()
    {
        if (!OperatingSystem.IsWindows()) return false;
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
            if (!string.IsNullOrWhiteSpace(pv) && pv != "0.0.0.0") return true;
        }
        return false;
    }

    public (bool Ok, string Detail) ProgId(string progId, string engineName)
    {
        if (!OperatingSystem.IsWindows()) return (false, engineName);
        using var k = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(progId + @"\CLSID");
        var clsid = k?.GetValue(null) as string;
        if (string.IsNullOrWhiteSpace(clsid)) return (false, engineName);
        // A ProgID whose server key is gone is a stale registration, not an installed Office.
        foreach (var server in new[] { "LocalServer32", "InprocServer32" })
        {
            using var s = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"CLSID\" + clsid + "\\" + server);
            if (s is not null) return (true, "");
        }
        return (false, engineName);
    }
}

/// <summary>Reads <c>ffmpeg -filters</c> once per process.</summary>
public sealed class FfmpegInfo : IFfmpegInfo
{
    private readonly object _gate = new();
    private string? _exe;
    private IReadOnlySet<string>? _filters;
    private bool _filtersRead;

    public string? Locate() => EngineProbes.LocateFfmpeg();

    private readonly HashSet<string> _runs = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _runGate = new();

    // One launch at a time per process: the filter engines all ask at once, and four concurrent cold starts of a
    // large exe are slower than one. Only a definite "runs" is kept; a failed or slow start is tried again.
    public bool Runs(string exe)
    {
        lock (_runGate)
        {
            if (_runs.Contains(exe)) return true;
            if (!EngineProbes.RunVersion(exe, "-version", "ffmpeg")) return false;
            _runs.Add(exe);
            return true;
        }
    }

    public IReadOnlySet<string>? Filters(string exe)
    {
        lock (_gate)
        {
            if (_filtersRead && _filters is not null && string.Equals(_exe, exe, StringComparison.OrdinalIgnoreCase)) return _filters;
            _exe = exe;
            // A failed or slow read is not remembered: it may be a cold first start, and the next ask retries.
            _filters = Read(exe);
            _filtersRead = _filters is not null;
            return _filters;
        }
    }

    private static IReadOnlySet<string>? Read(string exe)
    {
        var text = EngineProbes.RunCapture(exe, "-hide_banner -filters", EngineProbes.ProbeTimeoutMs);
        return text is null ? null : ParseFilters(text);
    }

    private static readonly System.Text.RegularExpressions.Regex FilterLine =
        new(@"^[ \t]*\S+[ \t]+(\S+)[ \t]+\S+->\S+", System.Text.RegularExpressions.RegexOptions.Multiline);

    /// <summary>Filter names from <c>ffmpeg -filters</c> text (LF or CRLF); null when none could be read.</summary>
    public static IReadOnlySet<string>? ParseFilters(string text)
    {
        // The flags column differs between FFmpeg versions (three characters in 6.x, other widths later), so
        // match "flags name inputs->outputs" without assuming its shape; legend lines never have "->".
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match m in FilterLine.Matches(text))
            set.Add(m.Groups[1].Value);
        return set.Count == 0 ? null : set;
    }
}
