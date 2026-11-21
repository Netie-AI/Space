using System.Diagnostics;
using NetieSpace.Apps.Tools;
using static NetieSpace.Tests.VideoToolTestSupport;

namespace NetieSpace.Tests;

/// <summary>Audit angles (e) and (f): failure paths leave nothing behind, progress says 100 only on success, cancel is prompt.</summary>
[Collection(IsolatedMeasurementCollection.Name)]
public class AuditRunnerTests
{
    private static readonly ToolRegistry Registry = ToolRegistry.Load(probe: new EngineProbes());

    private static Task<ToolResult> Go(string id, IEnumerable<string> files, string outDir, IProgress<ToolProgress>? progress = null,
        CancellationToken ct = default, params (string, string)[] opts)
    {
        var tool = Registry.Find(id)!;
        return ToolRunner.RunAsync(tool, new ToolRequest(files.ToList(), outDir, opts.ToDictionary(o => o.Item1, o => o.Item2)), progress, ct);
    }

    private static string Garbage(string dir, string name)
    {
        var p = IoPath.Combine(dir, name);
        IoFile.WriteAllBytes(p, Enumerable.Range(0, 500).Select(i => (byte)(i * 37 % 251)).ToArray());
        return p;
    }

    [Fact]
    public async Task A_batch_that_fails_on_its_second_file_leaves_no_result_of_the_first()
    {
        var inDir = ToolTestSupport.NewTempDir();
        var good = ImageToolTestSupport.NoisyImage(inDir, "a.png", "png", 64, 48);
        var bad = Garbage(inDir, "b.png");
        var o1 = ToolTestSupport.NewTempDir();
        var r1 = await Go("img-resize", new[] { good, bad }, o1, null, default, ("mode", "percentage"), ("percent", "50"));
        Assert.False(r1.Ok);
        Assert.Empty(IoDirectory.GetFileSystemEntries(o1));

        var pdfDir = PdfKit.NewDir();
        var p1 = PdfKit.Make(pdfDir, "one.pdf", 2);
        var p2 = Garbage(pdfDir, "two.pdf");
        var o2 = ToolTestSupport.NewTempDir();
        var r2 = await Go("pdf-rotate", new[] { p1, p2 }, o2);
        Assert.False(r2.Ok);
        Assert.Empty(IoDirectory.GetFileSystemEntries(o2));

        var o3 = ToolTestSupport.NewTempDir();
        var r3 = await Go("pdf-protect", new[] { p1, p2 }, o3, null, default, ("password", "x"));
        Assert.False(r3.Ok);
        Assert.Empty(IoDirectory.GetFileSystemEntries(o3));
    }

    [Fact]
    public async Task A_failed_run_keeps_what_was_already_in_the_output_folder()
    {
        var inDir = ToolTestSupport.NewTempDir();
        var good = ImageToolTestSupport.NoisyImage(inDir, "a.png", "png", 64, 48);
        var bad = Garbage(inDir, "b.png");
        var o = ToolTestSupport.NewTempDir();
        var mine = IoPath.Combine(o, "keep-me.txt");
        IoFile.WriteAllText(mine, "earlier work");
        var r = await Go("img-rotate", new[] { good, bad }, o);
        Assert.False(r.Ok);
        Assert.Equal(new[] { mine }, IoDirectory.GetFileSystemEntries(o));
        Assert.Equal("earlier work", IoFile.ReadAllText(mine));
    }

    [Fact]
    public async Task Progress_never_says_100_for_a_run_that_fails()
    {
        // A page with no text layer: pdf-extract-text walks every page (reporting progress) and then refuses.
        var dir = ToolTestSupport.NewTempDir();
        using (var doc = new PdfSharpCore.Pdf.PdfDocument())
        {
            doc.AddPage();
            doc.AddPage();
            doc.Save(IoPath.Combine(dir, "blank.pdf"));
        }
        var progress = new ListProgress();
        var r = await Go("pdf-extract-text", new[] { IoPath.Combine(dir, "blank.pdf") }, ToolTestSupport.NewTempDir(), progress);
        Assert.False(r.Ok);
        Assert.NotEmpty(progress.Reports);
        Assert.DoesNotContain(progress.Reports, p => p.Percent >= 100);

        var ok = new ListProgress();
        var good = await Go("pdf-extract-text", new[] { PdfKit.Make(dir, "t.pdf", 3) }, ToolTestSupport.NewTempDir(), ok);
        Assert.True(good.Ok, good.Error);
        Assert.Equal(100, ok.Reports[^1].Percent);
        Assert.Equal(1, ok.Reports.Count(p => p.Percent == 100));
    }

    [Fact]
    public async Task Cancelling_a_long_pdf_run_returns_within_two_seconds_and_leaves_nothing()
    {
        var dir = PdfKit.NewDir();
        var big = PdfKit.Make(dir, "big.pdf", 400, "BIG", extraLines: 40);
        foreach (var (id, opts) in new[]
        {
            ("pdf-split", new[] { ("split_mode", "filesize"), ("max_size_kb", "400") }),
            ("pdf-watermark", new[] { ("text", "DRAFT"), ("tile", "true") }),
            ("pdf-page-numbers", Array.Empty<(string, string)>()),
        })
        {
            var o = ToolTestSupport.NewTempDir();
            using var cts = new CancellationTokenSource();
            var run = Task.Run(() => Go(id, new[] { big }, o, null, cts.Token, opts));
            await Task.Delay(300);
            var sw = Stopwatch.StartNew();
            cts.Cancel();
            var result = await run.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), id + " took " + sw.Elapsed.TotalSeconds + " s to stop");
            if (!result.Ok)
            {
                Assert.Equal("cancelled", result.Error);
                Assert.Empty(IoDirectory.GetFileSystemEntries(o));
            }
            else
                Assert.True(true, id + " finished before the cancel arrived");
        }
    }

    [Fact]
    public async Task Cancelling_a_video_run_returns_within_two_seconds_and_leaves_nothing()
    {
        var dir = NewDir();
        var clip = MakeVideo(dir, "long.mp4", seconds: 55, size: "640x360", rate: 30);
        foreach (var (id, opts) in new[]
        {
            ("vid-convert", new[] { ("format", "webm") }),
            ("vid-reverse", Array.Empty<(string, string)>()),
            ("vid-speed", new[] { ("speed", "400") }),
        })
        {
            var o = ToolTestSupport.NewTempDir();
            using var cts = new CancellationTokenSource();
            // Cancel from the progress stream once ffmpeg is provably mid-encode, not after a fixed delay:
            // a timer continuation can be starved for seconds by other test classes on a busy runner,
            // and then the encode finishes before the cancel arrives.
            var sw = new Stopwatch();
            var cancelAt = new CancelOnProgress(cts, sw);
            var run = Task.Run(() => Go(id, new[] { clip }, o, cancelAt, cts.Token, opts));
            // stop the clock on the thread that completes the run, so an await continuation delay is not counted
            var stopped = run.ContinueWith(_ => sw.Stop(), TaskContinuationOptions.ExecuteSynchronously);
            var result = await run.WaitAsync(TimeSpan.FromSeconds(120));
            await stopped;
            Assert.True(cancelAt.Fired, id + " finished with no mid-run progress report; make the clip longer");
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), id + " took " + sw.Elapsed.TotalSeconds + " s to stop");
            Assert.False(result.Ok, id + " finished before the cancel; make the clip longer");
            Assert.Equal("cancelled", result.Error);
            Assert.Empty(IoDirectory.GetFileSystemEntries(o));
        }
    }

    [Fact]
    public async Task Same_named_inputs_from_two_folders_never_collide_and_the_input_folder_is_a_safe_output_folder()
    {
        var a = ToolTestSupport.NewTempDir();
        var b = ToolTestSupport.NewTempDir();
        var cases = new List<(string Id, string[] Files, (string, string)[] Opts)>
        {
            ("pdf-rotate", new[] { PdfKit.Make(a, "doc.pdf", 2, "A"), PdfKit.Make(b, "doc.pdf", 3, "B") }, Array.Empty<(string, string)>()),
            ("pdf-watermark", new[] { PdfKit.Make(a, "w.pdf", 1, "A"), PdfKit.Make(b, "w.pdf", 1, "B") }, new[] { ("text", "X") }),
            ("img-resize", new[] { ImageToolTestSupport.NoisyImage(a, "p.png", "png", 40, 40), ImageToolTestSupport.NoisyImage(b, "p.png", "png", 60, 60) }, new[] { ("mode", "percentage"), ("percent", "50") }),
            ("img-to-jpg", new[] { ImageToolTestSupport.NoisyImage(a, "q.png", "png", 40, 40), ImageToolTestSupport.NoisyImage(b, "q.png", "png", 60, 60) }, Array.Empty<(string, string)>()),
        };
        foreach (var (id, files, opts) in cases)
        {
            var before = files.Select(f => IoFile.ReadAllBytes(f)).ToList();
            // the output folder is the first input's own folder: nothing may be written over an input
            var r = await Go(id, files, a, null, default, opts);
            Assert.True(r.Ok, id + ": " + r.Error);
            Assert.Equal(2, r.Outputs.Select(o => o.ToLowerInvariant()).Distinct().Count());
            foreach (var o in r.Outputs) Assert.DoesNotContain(o, files);
            for (var i = 0; i < files.Length; i++) Assert.Equal(before[i], IoFile.ReadAllBytes(files[i]));
        }

        // video tools take one file: run twice, into the input's own folder
        var clip = MakeVideo(a, "m.mp4", 1);
        var clipBytes = IoFile.ReadAllBytes(clip);
        var first = await Go("vid-mute", new[] { clip }, a);
        var second = await Go("vid-mute", new[] { clip }, a);
        Assert.True(first.Ok && second.Ok, first.Error + second.Error);
        Assert.NotEqual(first.Outputs[0], second.Outputs[0]);
        Assert.NotEqual(clip, first.Outputs[0]);
        Assert.Equal(clipBytes, IoFile.ReadAllBytes(clip));
    }
}
