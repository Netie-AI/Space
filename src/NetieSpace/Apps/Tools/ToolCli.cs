using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace NetieSpace.Apps.Tools;

/// <summary>
/// Headless tool CLI. <see cref="Run(string[])"/> is the exact entry the S50-E1-a pre-mutex dispatcher
/// reaches by reflection. Exit codes: 0 ok, 1 runner failure, 2 unavailable or bad arguments.
/// Stderr always says why.
/// </summary>
public static class ToolCli
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static int Run(string[] args) =>
        RunWith(args, Console.Out, Console.Error, ToolRegistry.Default);

    public static int RunWith(string[] args, TextWriter stdout, TextWriter stderr, ToolRegistry registry)
    {
        try
        {
            return Dispatch(args ?? Array.Empty<string>(), stdout, stderr, registry);
        }
        catch (Exception ex)
        {
            stderr.WriteLine("tool cli failed: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static int Dispatch(string[] args, TextWriter stdout, TextWriter stderr, ToolRegistry registry)
    {
        bool list = false;
        string? toolId = null, outDir = null, jsonPath = null;
        var inputs = new List<string>();
        var opts = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < args.Length; i++)
        {
            string Next(string flag)
            {
                if (i + 1 >= args.Length) throw new ArgumentException(flag + " needs a value");
                return args[++i];
            }

            try
            {
                switch (args[i])
                {
                    case "--list-tools": list = true; break;
                    case "--tool": toolId = Next("--tool"); break;
                    case "--in": inputs.Add(Next("--in")); break;
                    case "--out": outDir = Next("--out"); break;
                    case "--json": jsonPath = Next("--json"); break;
                    case "--opt":
                    {
                        var kv = Next("--opt");
                        var eq = kv.IndexOf('=');
                        if (eq <= 0) throw new ArgumentException("--opt needs key=value, got '" + kv + "'");
                        opts[kv.Substring(0, eq)] = kv.Substring(eq + 1);
                        break;
                    }
                    default: throw new ArgumentException("unknown argument '" + args[i] + "'");
                }
            }
            catch (ArgumentException ex)
            {
                stderr.WriteLine("bad arguments: " + ex.Message);
                return 2;
            }
        }

        if (list == (toolId is not null))
        {
            stderr.WriteLine("bad arguments: give exactly one of --list-tools or --tool <id>");
            return 2;
        }

        return list ? ListTools(registry, jsonPath, stdout, stderr) : RunTool(registry, toolId!, inputs, outDir, opts, jsonPath, stdout, stderr);
    }

    private static string AvailText(ToolAvailability a) => a.Available ? "available" : "unavailable";

    private static int ListTools(ToolRegistry registry, string? jsonPath, TextWriter stdout, TextWriter stderr)
    {
        var all = registry.All();
        foreach (var t in all)
        {
            var e = t.Entry;
            stdout.WriteLine(string.Join("\t", e.Id, e.App.ToString().ToLowerInvariant(), e.Engine.Name,
                e.Engine.License.ToString(), AvailText(t.Availability), t.Availability.Text));
        }

        if (jsonPath is not null)
        {
            var doc = new
            {
                count = all.Count,
                tools = all.Select(t => new
                {
                    id = t.Entry.Id,
                    app = t.Entry.App.ToString().ToLowerInvariant(),
                    engine = t.Entry.Engine.Name,
                    licence = t.Entry.Engine.License.ToString(),
                    available = t.Availability.Available,
                    reason = t.Availability.Available ? null : t.Availability.Text,
                }).ToList(),
            };
            if (!TryWrite(jsonPath, JsonSerializer.Serialize(doc, Json), stderr)) return 1;
        }
        return 0;
    }

    private static int RunTool(ToolRegistry registry, string id, List<string> inputs, string? outDir,
        Dictionary<string, string> opts, string? jsonPath, TextWriter stdout, TextWriter stderr)
    {
        var tool = registry.Find(id);
        if (tool is null)
        {
            var r = new ToolResult(false, ToolOutcome.Invalid, id, "", Array.Empty<string>(), "unknown tool id: " + id, null, 0);
            return Finish(r, jsonPath, stdout, stderr);
        }
        if (outDir is null)
        {
            var r = new ToolResult(false, ToolOutcome.Invalid, id, tool.Entry.Engine.Name, Array.Empty<string>(), "--out <dir> is required", null, 0);
            return Finish(r, jsonPath, stdout, stderr);
        }

        var request = new ToolRequest(inputs, outDir, opts);
        var result = ToolRunner.RunAsync(tool, request, null, CancellationToken.None).GetAwaiter().GetResult();
        return Finish(result, jsonPath, stdout, stderr);
    }

    private static int Finish(ToolResult r, string? jsonPath, TextWriter stdout, TextWriter stderr)
    {
        if (r.Ok)
            foreach (var o in r.Outputs) stdout.WriteLine(o);
        else
            stderr.WriteLine($"{r.ToolId}: {r.Error}");

        if (jsonPath is not null)
        {
            var doc = new
            {
                ok = r.Ok,
                tool = r.ToolId,
                engine = r.Engine,
                outputs = r.Outputs,
                error = r.Error,
                reason = r.Reason,
                elapsedMs = r.ElapsedMs,
            };
            if (!TryWrite(jsonPath, JsonSerializer.Serialize(doc, Json), stderr) && r.Ok) return 1;
        }

        return r.Outcome switch
        {
            ToolOutcome.Ok => 0,
            ToolOutcome.Failed => 1,
            _ => 2,
        };
    }

    private static bool TryWrite(string path, string text, TextWriter stderr)
    {
        try
        {
            var dir = IoPath.GetDirectoryName(IoPath.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) IoDirectory.CreateDirectory(dir);
            IoFile.WriteAllText(path, text, new UTF8Encoding(false));
            return true;
        }
        catch (Exception ex)
        {
            stderr.WriteLine("could not write --json file " + path + ": " + ex.Message);
            return false;
        }
    }
}
