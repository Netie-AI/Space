using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;

namespace NetieSpace.Apps.Bridge;

/// <summary>
/// <c>--send-to-phone FILE [--yes] [--json FILE]</c>. Without --yes: one disclosure line, nothing sent, exit 2.
/// With --yes: sends once and prints the result. Exit 0 sent or duplicate, 1 failed, 2 not confirmed or bad arguments.
/// </summary>
public static class BridgeCli
{
    public const string SendFlag = "--send-to-phone";
    public const int ExitOk = 0, ExitFailed = 1, ExitUsage = 2;

    /// <summary>True when the args are a bridge command, so the exe can route them here.</summary>
    public static bool Handles(string[] args) => args.Any(a => string.Equals(a, SendFlag, StringComparison.Ordinal));

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, HttpMessageHandler? handler = null)
        => Run(args, stdout, stderr, handler, Environment.GetEnvironmentVariable);

    internal static int Run(string[] args, TextWriter stdout, TextWriter stderr, HttpMessageHandler? handler, Func<string, string?> getEnv)
        => RunAsync(args, stdout, stderr, handler, getEnv, CancellationToken.None).GetAwaiter().GetResult();

    public static Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, HttpMessageHandler? handler = null, CancellationToken cancel = default)
        => RunAsync(args, stdout, stderr, handler, Environment.GetEnvironmentVariable, cancel);

    internal static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, HttpMessageHandler? handler,
        Func<string, string?> getEnv, CancellationToken cancel)
    {
        if (!TryParse(args, out var file, out var yes, out var jsonPath, out var usageError))
        {
            stderr.WriteLine(usageError);
            stderr.WriteLine("usage: " + SendFlag + " <FILE> [--yes] [--json <file>]");
            return ExitUsage;
        }

        string baseUrl;
        try { baseUrl = AirGptDrop.ResolveBaseUrl(getEnv("AIRGPT_URL")); }
        catch (ArgumentException e)
        {
            stderr.WriteLine(e.Message + ". Nothing was sent.");
            WriteJson(jsonPath, "not_confirmed", null, 0, e.Message, file, 0, stderr);
            return ExitUsage;
        }

        if (jsonPath is not null && SamePath(jsonPath, file))
        {
            stderr.WriteLine("--json must not point at the file being sent. Nothing was sent.");
            return ExitUsage;
        }

        using var drop = new AirGptDrop(baseUrl, handler);
        var name = Display(Path.GetFileName(file));

        // Cheap pre-check so the disclosure never describes a transfer that cannot happen.
        FileInfo? info = null;
        try { info = new FileInfo(file); } catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { }
        if (info is null || !info.Exists)
            return Finish(new DropResult(DropStatus.FileMissing, null, 0, "file not found"), file, drop.Port, jsonPath, stdout, stderr);
        if (info.Length > AirGptDrop.MaxBytes)
            return Finish(new DropResult(DropStatus.TooLarge, null, 0, info.Length + " bytes"), file, drop.Port, jsonPath, stdout, stderr);

        var line = $"'{name}' ({info.Length} bytes) will be copied to AirGPT on this PC at port {drop.Port}; devices paired with AirGPT can then fetch it.";
        if (!yes)
        {
            stdout.WriteLine("Not sent. " + line + " Re-run with --yes to send.");
            WriteJson(jsonPath, "not_confirmed", null, 0, "re-run with --yes to send", file, drop.Port, stderr);
            return ExitUsage;
        }

        stdout.WriteLine("Sending: " + line);
        DropResult result;
        try { result = await drop.SendAsync(file, cancel: cancel).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            stderr.WriteLine("Cancelled. The file may or may not have reached AirGPT.");
            WriteJson(jsonPath, "cancelled", null, 0, "cancelled", file, drop.Port, stderr);
            return ExitFailed;
        }
        return Finish(result, file, drop.Port, jsonPath, stdout, stderr);
    }

    private static int Finish(DropResult r, string file, int port, string? jsonPath, TextWriter stdout, TextWriter stderr)
    {
        var detail = string.IsNullOrEmpty(r.Message) ? "" : ": " + r.Message;
        switch (r.Status)
        {
            case DropStatus.Sent:
                stdout.WriteLine(r.MessageId is null ? "Sent to AirGPT (no message id in the reply)." : $"Sent to AirGPT (message id {r.MessageId}).");
                break;
            case DropStatus.Duplicate:
                stdout.WriteLine("AirGPT already has this file (same content). Nothing new was stored.");
                break;
            case DropStatus.NotRunning:
                stderr.WriteLine($"AirGPT is not running on this PC at port {port}. Nothing was sent.");
                break;
            case DropStatus.TooLarge:
                stderr.WriteLine($"File is too large for AirGPT (limit 100 MB){detail}. Nothing was sent.");
                break;
            case DropStatus.FileMissing:
                stderr.WriteLine($"File not found or not readable{detail}. Nothing was sent.");
                break;
            default:
                stderr.WriteLine(r.HttpStatus > 0
                    ? $"AirGPT refused the file (HTTP {r.HttpStatus}){detail}"
                    : $"AirGPT did not confirm the file{detail}");
                break;
        }
        WriteJson(jsonPath, StatusName(r.Status), r.MessageId, r.HttpStatus, r.Message, file, port, stderr);
        return r.Status is DropStatus.Sent or DropStatus.Duplicate ? ExitOk : ExitFailed;
    }

    private static string StatusName(DropStatus s) => s switch
    {
        DropStatus.Sent => "sent",
        DropStatus.Duplicate => "duplicate",
        DropStatus.NotRunning => "not_running",
        DropStatus.Refused => "refused",
        DropStatus.TooLarge => "too_large",
        _ => "file_missing",
    };

    private static void WriteJson(string? path, string status, string? id, int http, string? message, string file, int port, TextWriter stderr)
    {
        if (path is null) return;
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                status,
                messageId = id,
                httpStatus = http,
                message,
                file = Path.GetFileName(file),
                port,
            });
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            stderr.WriteLine("Could not write --json file: " + AirGptDrop.Clean(e.Message));
        }
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static string Display(string name) => AirGptDrop.Clean(name).Replace('\'', '`');

    private static bool TryParse(string[] args, out string file, out bool yes, out string? json, out string error)
    {
        file = ""; yes = false; json = null; error = "";
        var haveFile = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case SendFlag:
                    if (haveFile) { error = SendFlag + " given twice"; return false; }
                    if (i + 1 >= args.Length || args[i + 1] is "--yes" or "--json" || args[i + 1].Length == 0)
                    { error = SendFlag + " needs a file"; return false; }
                    file = args[++i]; haveFile = true;
                    break;
                case "--yes":
                    if (yes) { error = "--yes given twice"; return false; }
                    yes = true;
                    break;
                case "--json":
                    if (json is not null) { error = "--json given twice"; return false; }
                    if (i + 1 >= args.Length || args[i + 1].Length == 0) { error = "--json needs a file"; return false; }
                    json = args[++i];
                    break;
                default:
                    error = "unknown argument: " + AirGptDrop.Clean(args[i]);
                    return false;
            }
        }
        if (!haveFile) error = SendFlag + " needs a file";
        return haveFile;
    }
}
