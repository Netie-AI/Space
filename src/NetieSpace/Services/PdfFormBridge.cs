using System.Diagnostics;
using System.Text.Json;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

public sealed record PdfFormListResult(bool Ok, bool Xfa, IReadOnlyList<string> Names, string? Error);

/// <summary>Optional Go CLI netie-pdf-form.exe -- PdfPig rects stay the geometry source.</summary>
public static class PdfFormBridge
{
    public static PdfFormListResult? TryList(string pdfPath)
    {
        try
        {
            var exe = FindExe();
            if (exe is null || !IoFile.Exists(pdfPath))
                return null;
            var payload = JsonSerializer.Serialize(new { job = "form-list", @in = pdfPath });
            var stdout = Run(exe, "form-list", payload, 4000);
            if (stdout is null)
                return null;
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            var xfa = root.TryGetProperty("xfa", out var xfaEl) && xfaEl.ValueKind == JsonValueKind.True;
            var err = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            var names = new List<string>();
            if (root.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in fields.EnumerateArray())
                {
                    var n = f.TryGetProperty("name", out var nm) ? nm.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(n))
                        names.Add(n!);
                }
            }
            return new PdfFormListResult(ok, xfa, names, err);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"pdf-form list: {ex.Message}");
            return null;
        }
    }

    public static bool ExeAvailable => FindExe() is not null;

    private static string? Run(string exe, string args, string stdin, int timeoutMs)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p is null)
            return null;
        p.StandardInput.Write(stdin);
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(timeoutMs))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return null;
        }
        return string.IsNullOrWhiteSpace(stdout) ? null : stdout.Trim();
    }

    private static string? FindExe()
    {
        var root = EnvLoader.FindProjectRoot();
        var candidates = new[]
        {
            IoPath.Combine(AppContext.BaseDirectory, "tools", "netie-pdf-form", "netie-pdf-form.exe"),
            IoPath.Combine(root, "tools", "netie-pdf-form", "netie-pdf-form.exe"),
            IoPath.Combine(root, "native", "netie-pdf-form", "netie-pdf-form.exe")
        };
        return candidates.FirstOrDefault(IoFile.Exists);
    }
}
