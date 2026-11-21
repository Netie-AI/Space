using System.Security.Cryptography;
using NetieSpace.Apps.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit.Abstractions;
using static NetieSpace.Tests.VideoToolTestSupport;

namespace NetieSpace.Tests;

/// <summary>
/// Audit angle (b): every tool that runs, driven with no options at all through ToolCli.RunWith. Each must
/// either produce a correct, readable result or fail with a message that names what is missing, never crash,
/// hang or leave a stray file behind. Driven from the registry, so a tool added later is covered automatically.
/// </summary>
public class AuditDefaultsTests
{
    private readonly ITestOutputHelper _out;
    public AuditDefaultsTests(ITestOutputHelper output) { _out = output; }

    private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".gif", ".bmp" };

    private enum Kind { Pdf, Image, Video, Audio, Subtitle, Other }

    private static Kind KindOf(string ext) => ext.ToLowerInvariant() switch
    {
        ".pdf" => Kind.Pdf,
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff" or ".webp" => Kind.Image,
        ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm" or ".wmv" or ".m4v" or ".mpeg" or ".3gp" or ".ts" => Kind.Video,
        ".mp3" or ".wav" or ".m4a" or ".aac" or ".flac" or ".ogg" => Kind.Audio,
        ".srt" or ".vtt" => Kind.Subtitle,
        _ => Kind.Other,
    };

    private static string Make(string dir, string ext, int slot)
    {
        var name = "in" + slot + ext;
        switch (KindOf(ext))
        {
            case Kind.Pdf: return PdfKit.Make(dir, name, 3, "AUD" + slot);
            case Kind.Image:
            {
                var fmt = ext.TrimStart('.').Replace("jpeg", "jpg");
                return ImageToolTestSupport.NoisyImage(dir, name, fmt, 160, 120);
            }
            case Kind.Video: return MakeVideo(dir, name, 2);
            case Kind.Audio: return MakeAudio(dir, name, 2);
            case Kind.Subtitle:
            {
                var p = IoPath.Combine(dir, name);
                IoFile.WriteAllText(p, "1\n00:00:00,000 --> 00:00:01,500\nHello\n\n");
                return p;
            }
            case Kind.Other when ext.Equals(".html", StringComparison.OrdinalIgnoreCase) || ext.Equals(".htm", StringComparison.OrdinalIgnoreCase):
            {
                var p = IoPath.Combine(dir, name);
                IoFile.WriteAllText(p, "<!doctype html><html><body><h1>Hello from Netie</h1><p>Audit page.</p></body></html>");
                return p;
            }
            case Kind.Other when ext.Equals(".txt", StringComparison.OrdinalIgnoreCase):
            {
                var p = IoPath.Combine(dir, name);
                IoFile.WriteAllText(p, "Hello from Netie.\n");
                return p;
            }
            default: throw new InvalidOperationException("no fixture for " + ext);
        }
    }

    /// <summary>One sensible input per required slot; later slots prefer a different kind than slot 0.</summary>
    private static List<string> Inputs(ToolEntry e, string dir)
    {
        var types = e.AcceptedTypes.Count > 0 ? e.AcceptedTypes.ToList() : new List<string> { ".pdf" };
        var n = Math.Max(1, e.MinFiles);
        var files = new List<string>();
        var kinds = types.Select(KindOf).Distinct().ToList();
        for (var i = 0; i < n; i++)
        {
            string ext;
            if (e.Id == "vid-add-audio") ext = i == 0 ? ".mp4" : ".mp3";
            else if (e.Id == "vid-add-image") ext = i == 0 ? ".mp4" : ".png";
            // Given a subtitle file this tool writes a temp file that VideoTool2OverlayTests asserts is absent, and
            // the suite runs classes in parallel, so it gets the video alone (and refuses, which is also a default).
            else if (e.Id == "vid-subtitles") ext = ".mp4";
            else
            {
                ext = types[0];
                // The catalog lists video types for the slideshow, but the tool takes pictures and music.
                if (e.Id == "vid-slideshow") ext = ".png";
                // A tool taking several kinds and several files: spread the slots over the kinds.
                if (n > 1 && kinds.Count > 1 && i < kinds.Count)
                    ext = types.First(t => KindOf(t) == kinds[i]);
            }
            files.Add(Make(dir, ext, i));
        }
        return files;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(IoFile.ReadAllBytes(path)));

    private static void AssertReadable(string tool, string path)
    {
        var ext = IoPath.GetExtension(path).ToLowerInvariant();
        switch (KindOf(ext))
        {
            case Kind.Pdf:
                Assert.True(PdfKit.PageCount(path) > 0, tool + " wrote a PDF with no pages: " + path);
                break;
            case Kind.Image:
            {
                var info = SixLabors.ImageSharp.Image.Identify(path);
                Assert.True(info is not null && info.Width > 0 && info.Height > 0, tool + " wrote an unreadable image: " + path);
                break;
            }
            case Kind.Video:
            case Kind.Audio:
            {
                var pf = Probe(path);
                Assert.True(pf.Duration > 0, tool + " wrote a media file with no duration: " + path);
                break;
            }
            default:
                Assert.True(new FileInfo(path).Length > 0, tool + " wrote an empty file: " + path);
                break;
        }
    }

    [Fact]
    public void Every_runnable_tool_with_no_options_succeeds_correctly_or_names_what_is_missing()
    {
        var registry = ToolRegistry.Load(probe: new EngineProbes());
        // vid-stabilize is left to VideoTool2StabilizeTests: it writes a temp file that those tests assert is absent.
        var tools = registry.All().Where(t => t.Availability.Available && t.Runner is not null && t.Entry.Id != "vid-stabilize").ToList();
        Assert.True(tools.Count >= 51, "expected at least 51 runnable tools, found " + tools.Count);

        var problems = new List<string>();
        var succeeded = 0;
        var refused = 0;
        foreach (var t in tools)
        {
            var e = t.Entry;
            var inDir = ToolTestSupport.NewTempDir();
            var outDir = IoPath.Combine(ToolTestSupport.NewTempDir(), "out");
            List<string> inputs;
            try { inputs = Inputs(e, inDir); }
            catch (Exception ex)
            {
                problems.Add($"{e.Id}: test could not build an input ({ex.Message}); extend Inputs() for this tool");
                continue;
            }
            var before = inputs.ToDictionary(f => f, Hash);

            var args = new List<string> { "--tool", e.Id, "--out", outDir };
            foreach (var f in inputs) { args.Add("--in"); args.Add(f); }
            var so = new StringWriter();
            var se = new StringWriter();
            var task = Task.Run(() => ToolCli.RunWith(args.ToArray(), so, se, registry));
            if (!task.Wait(TimeSpan.FromSeconds(120)))
            {
                problems.Add($"{e.Id}: hung (no result after 120 s)");
                continue;
            }
            var exit = task.Result;
            var stdout = so.ToString();
            var stderr = se.ToString();
            var outFiles = IoDirectory.Exists(outDir) ? IoDirectory.GetFileSystemEntries(outDir, "*", SearchOption.AllDirectories) : Array.Empty<string>();

            foreach (var f in inputs)
                if (!IoFile.Exists(f) || Hash(f) != before[f])
                    problems.Add($"{e.Id}: the input {IoPath.GetFileName(f)} was changed or removed");

            if (exit == 0)
            {
                succeeded++;
                var claimed = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
                if (claimed.Count == 0) problems.Add($"{e.Id}: exit 0 but no output named");
                foreach (var c in claimed)
                {
                    try
                    {
                        if (!c.StartsWith(outDir, StringComparison.Ordinal)) problems.Add($"{e.Id}: output {c} is outside the output folder");
                        else if (inputs.Contains(c)) problems.Add($"{e.Id}: output overwrote an input");
                        else AssertReadable(e.Id, c);
                    }
                    catch (Exception ex) { problems.Add($"{e.Id}: {ex.Message}"); }
                }
                if (outFiles.Length != claimed.Count)
                    problems.Add($"{e.Id}: {outFiles.Length} entries in the output folder but {claimed.Count} named");
            }
            else
            {
                refused++;
                if (exit != 1 && exit != 2) problems.Add($"{e.Id}: unexpected exit code {exit}");
                if (string.IsNullOrWhiteSpace(stderr)) problems.Add($"{e.Id}: failed with no message");
                else if (!stderr.Contains("option", StringComparison.OrdinalIgnoreCase) && !stderr.Contains("needs", StringComparison.OrdinalIgnoreCase)
                         && !stderr.Contains("required", StringComparison.OrdinalIgnoreCase)
                         && !stderr.Contains("not built", StringComparison.OrdinalIgnoreCase) && !stderr.Contains("smaller", StringComparison.OrdinalIgnoreCase))
                    problems.Add($"{e.Id}: failed without naming what is missing: {stderr.Trim()}");
                if (stderr.Contains("NullReference") || stderr.Contains("IndexOutOfRange") || stderr.Contains("KeyNotFound")
                    || stderr.Contains("InvalidCast") || stderr.Contains("NotImplemented"))
                    problems.Add($"{e.Id}: crashed instead of validating: {stderr.Trim()}");
                if (outFiles.Length != 0)
                    problems.Add($"{e.Id}: failed but left {outFiles.Length} entries in the output folder: {string.Join(", ", outFiles.Select(IoPath.GetFileName))}");
            }
            _out.WriteLine($"{e.Id}: exit {exit}" + (exit == 0 ? "" : " | " + stderr.Trim().Replace('\n', ' ')));
        }
        _out.WriteLine($"{tools.Count} tools: {succeeded} produced a result, {refused} refused with a message");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
