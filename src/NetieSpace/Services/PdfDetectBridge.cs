using System.Diagnostics;
using System.Text.Json;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

public sealed record PdfSnapCellResult(bool Ok, string Kind, double XPt, double YFromBottom, double WPt, double HPt);

/// <summary>Optional Rust CLI netie-pdf-detect.exe -- JS detectCellAt is the fallback.</summary>
public static class PdfDetectBridge
{
    public static PdfSnapCellResult? TrySnapCell(string pngPath, double clickXPx, double clickYPx, double pageWidthPt, double pageHeightPt)
    {
        try
        {
            var exe = FindExe();
            if (exe is null || !IoFile.Exists(pngPath))
                return null;

            var payload = JsonSerializer.Serialize(new
            {
                job = "snap-cell",
                pngPath,
                clickXPx,
                clickYPx,
                pageWidthPt,
                pageHeightPt
            });
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "snap-cell",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null)
                return null;
            p.StandardInput.Write(payload);
            p.StandardInput.Close();
            var stdout = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(250))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return null;
            }
            if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                return null;
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.True)
                return null;
            var kind = root.TryGetProperty("kind", out var k) ? k.GetString() ?? "none" : "none";
            if (kind is "none" or "")
                return new PdfSnapCellResult(true, "none", 0, 0, 0, 0);
            if (!root.TryGetProperty("xPt", out var x) || !root.TryGetProperty("yFromBottom", out var y) ||
                !root.TryGetProperty("wPt", out var w) || !root.TryGetProperty("hPt", out var h))
                return null;
            return new PdfSnapCellResult(true, kind, x.GetDouble(), y.GetDouble(), w.GetDouble(), h.GetDouble());
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"pdf-detect: {ex.Message}");
            return null;
        }
    }

    public static bool ExeAvailable => FindExe() is not null;

    private static string? FindExe()
    {
        var root = EnvLoader.FindProjectRoot();
        var candidates = new[]
        {
            IoPath.Combine(AppContext.BaseDirectory, "tools", "netie-pdf-detect", "netie-pdf-detect.exe"),
            IoPath.Combine(root, "tools", "netie-pdf-detect", "netie-pdf-detect.exe"),
            IoPath.Combine(root, "native", "netie-pdf-detect", "target", "release", "netie-pdf-detect.exe")
        };
        return candidates.FirstOrDefault(IoFile.Exists);
    }
}
