using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>Managed sparse embedding when Rust sidecar is absent — compact, no network.</summary>
public static class ManagedSparseEmbed
{
    private static readonly Regex TokenSplit = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

    public static LocalEmbedding Encode(string text, int dims = 256)
    {
        var values = new float[dims];
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in TokenSplit.Split(text.ToLowerInvariant()))
        {
            if (raw.Length < 3 || raw.Length > 32)
                continue;
            counts[raw] = counts.GetValueOrDefault(raw) + 1;
        }

        foreach (var (token, count) in counts)
        {
            var h = StableHash(token);
            var idx = (int)(h % (uint)dims);
            var sign = (h & 1) == 0 ? 1f : -1f;
            values[idx] += sign * MathF.Log(1 + count);
        }

        // L2 normalize
        double sumSq = 0;
        for (var i = 0; i < dims; i++)
            sumSq += values[i] * values[i];
        var norm = Math.Sqrt(sumSq);
        if (norm > 1e-9)
        {
            for (var i = 0; i < dims; i++)
                values[i] = (float)(values[i] / norm);
        }

        var top = counts.OrderByDescending(kv => kv.Value).Take(16).Select(kv => kv.Key).ToArray();
        return new LocalEmbedding
        {
            Dimensions = dims,
            Values = values,
            TopTokens = top
        };
    }

    private static uint StableHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (var c in s)
            {
                h ^= c;
                h *= 16777619;
            }
            return h;
        }
    }
}

/// <summary>Optional Rust CLI netie-embed.exe — falls back silently.</summary>
public static class RustEmbedBridge
{
    public static LocalEmbedding? TryEncode(string text)
    {
        try
        {
            var exe = FindExe();
            if (exe is null)
                return null;

            var sample = text.Length > 12000 ? text[..12000] : text;
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "encode --dims 256",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null)
                return null;
            p.StandardInput.Write(sample);
            p.StandardInput.Close();
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(4000);
            if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                return null;
            return JsonSerializer.Deserialize<LocalEmbedding>(stdout);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Rust embed bridge: {ex.Message}");
            return null;
        }
    }

    private static string? FindExe()
    {
        var candidates = new[]
        {
            IoPath.Combine(AppContext.BaseDirectory, "tools", "netie-embed", "netie-embed.exe"),
            IoPath.Combine(EnvLoader.FindProjectRoot(), "tools", "netie-embed", "netie-embed.exe"),
            IoPath.Combine(EnvLoader.FindProjectRoot(), "native", "netie-embed", "target", "release", "netie-embed.exe")
        };
        return candidates.FirstOrDefault(IoFile.Exists);
    }
}
