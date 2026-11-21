using System.IO;
using System.Text;
using System.Text.Json;
using NetieSpace.Apps.Tools;
using NetieSpace.Apps.Video;

namespace NetieSpace.Apps.Shell.Panels.Video;

/// <summary>
/// <c>NetieSpace.exe --tool vid-flow --in clip.mp4 --out DIR [--json FILE] --opt in=8 --opt out=115 --opt preset=whatsapp</c> (W6-4).
/// The quick edit is not a catalog row (the tool list and its floor stay as they are), so the tool CLI does not know it; this
/// is the same grammar, exit codes (0 ok, 1 failed, 2 bad arguments or unavailable) and JSON shape, for the one id. It is what
/// the exe-level check drives, so the output a person gets from the filmstrip page is the output the check measures.
/// </summary>
public static class VideoFlowCli
{
    public static bool Wants(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "--tool" && args[i + 1] == VideoToolProvider.FlowToolId) return true;
        return false;
    }

    public static int Run(string[] args) => Run(args, Console.Out, Console.Error, ToolRegistry.Default);

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, ToolRegistry registry)
    {
        try
        {
            string? outDir = null, jsonPath = null;
            var inputs = new List<string>();
            var opts = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < args.Length; i++)
            {
                string Next(string flag) => i + 1 < args.Length ? args[++i] : throw new ArgumentException(flag + " needs a value");
                switch (args[i])
                {
                    case "--tool": Next("--tool"); break;
                    case "--in": inputs.Add(Next("--in")); break;
                    case "--out": outDir = Next("--out"); break;
                    case "--json": jsonPath = Next("--json"); break;
                    case "--opt":
                    {
                        var kv = Next("--opt");
                        var eq = kv.IndexOf('=');
                        if (eq <= 0) throw new ArgumentException("--opt needs key=value, got '" + kv + "'");
                        opts[kv[..eq]] = kv[(eq + 1)..];
                        break;
                    }
                    default: throw new ArgumentException("unknown argument '" + args[i] + "'");
                }
            }
            if (outDir is null) throw new ArgumentException("--out <dir> is required");

            // The same engine, so the same availability as the page's own tools (ffmpeg present and cleared).
            var basis = registry.Find("vid-trim") ?? throw new InvalidOperationException("vid-trim is not in the catalog");
            var tool = VideoToolProvider.FlowTool(basis.Availability);
            var result = ToolRunner.RunAsync(tool, new ToolRequest(inputs, outDir, opts), null, CancellationToken.None).GetAwaiter().GetResult();
            if (result.Ok) foreach (var o in result.Outputs) stdout.WriteLine(o);
            else stderr.WriteLine($"{result.ToolId}: {result.Error}");
            if (jsonPath is not null)
            {
                var doc = new { ok = result.Ok, tool = result.ToolId, engine = result.Engine, outputs = result.Outputs, error = result.Error, reason = result.Reason, elapsedMs = result.ElapsedMs };
                var dir = IoPath.GetDirectoryName(IoPath.GetFullPath(jsonPath));
                if (!string.IsNullOrEmpty(dir)) IoDirectory.CreateDirectory(dir);
                IoFile.WriteAllText(jsonPath, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            }
            return result.Outcome switch { ToolOutcome.Ok => 0, ToolOutcome.Failed => 1, _ => 2 };
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine("bad arguments: " + ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            stderr.WriteLine("flow cli failed: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }
}
