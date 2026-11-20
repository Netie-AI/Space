using System.Text;
using NetieSpace.Apps.Md;

namespace NetieSpace.Tests;

/// <summary>Netie MD: reading a file that is large, binary or changing, the reload debounce, zoom and the headless arguments.</summary>
public class MdFileTests : IDisposable
{
    private readonly string _dir = IoPath.Combine(IoPath.GetTempPath(), "netie-mdf-" + Guid.NewGuid().ToString("N"));

    public MdFileTests() => IoDirectory.CreateDirectory(_dir);

    public void Dispose() => IoDirectory.Delete(_dir, true);

    private string Write(string name, byte[] bytes)
    {
        var p = IoPath.Combine(_dir, name);
        IoFile.WriteAllBytes(p, bytes);
        return p;
    }

    [Fact]
    public void A_plain_file_is_read_with_its_stamp()
    {
        var read = MdFile.Read(Write("a.md", Encoding.UTF8.GetBytes("# Hi\nbody")));
        Assert.Equal("# Hi\nbody", read.Text);
        Assert.Null(read.Error);
        Assert.False(read.Unstable);
        Assert.Equal(9, read.Stamp!.Value.Length);
    }

    [Fact]
    public void A_byte_order_mark_is_honoured_and_not_shown()
    {
        var utf8 = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("caf\u00e9")).ToArray();
        Assert.Equal("caf\u00e9", MdFile.Read(Write("u8.md", utf8)).Text);
        var utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("caf\u00e9")).ToArray();
        Assert.Equal("caf\u00e9", MdFile.Read(Write("u16.md", utf16)).Text);
    }

    [Fact]
    public void A_file_over_the_limit_is_refused_without_reading_it_all()
    {
        var path = Write("big.md", new byte[MdFile.MaxBytes + 1]);
        var read = MdFile.Read(path);
        Assert.Null(read.Text);
        Assert.Equal("File too large to show", read.Error);
        var edge = MdFile.Read(Write("edge.md", Enumerable.Repeat((byte)'a', (int)MdFile.MaxBytes).ToArray()));
        Assert.NotNull(edge.Text);
    }

    [Fact]
    public void A_file_that_grows_past_the_limit_while_it_is_read_is_refused()
    {
        var path = Write("grow.md", Encoding.UTF8.GetBytes("small"));
        // The stamp said "small"; the file is then replaced by a huge one before the bytes are read.
        var read = MdFile.Read(path, attempt =>
        {
            if (attempt == 1)
                IoFile.WriteAllBytes(path, new byte[MdFile.MaxBytes + 10]);
        });
        Assert.Null(read.Text);
        Assert.Equal("File too large to show", read.Error);
    }

    [Fact]
    public void A_binary_file_is_not_shown_as_text()
    {
        var read = MdFile.Read(Write("b.md", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0, 1, 2 }));
        Assert.Null(read.Text);
        Assert.Equal("Not a text file", read.Error);
    }

    [Fact]
    public void A_missing_file_is_reported()
    {
        Assert.Equal("File not found", MdFile.Read(IoPath.Combine(_dir, "none.md")).Error);
    }

    [Fact]
    public void A_file_that_changes_while_it_is_read_is_read_again_and_the_new_text_wins()
    {
        var path = Write("live.md", Encoding.UTF8.GetBytes("old"));
        var calls = 0;
        var read = MdFile.Read(path, attempt =>
        {
            calls++;
            if (attempt == 1)
                IoFile.WriteAllText(path, "brand new text");
        });
        Assert.Equal("brand new text", read.Text);
        Assert.False(read.Unstable);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void A_file_that_never_settles_is_shown_and_flagged_for_another_read()
    {
        var path = Write("busy.md", Encoding.UTF8.GetBytes("v0"));
        var n = 0;
        var read = MdFile.Read(path, _ => IoFile.WriteAllText(path, "v" + (++n) + new string('x', n)));
        Assert.NotNull(read.Text);
        Assert.True(read.Unstable);
        Assert.Equal(MdFile.Attempts, n);
    }

    [Fact]
    public void A_file_open_for_writing_by_an_editor_can_still_be_read()
    {
        var path = Write("held.md", Encoding.UTF8.GetBytes("held"));
        using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.Equal("held", MdFile.Read(path).Text);
    }

    // ---- reload debounce ----

    [Fact]
    public void A_burst_of_events_is_one_reload_after_it_goes_quiet()
    {
        var gate = new MdChangeGate(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(2));
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.False(gate.TryTake(t));
        gate.Note(t);
        gate.Note(t.AddMilliseconds(100));
        gate.Note(t.AddMilliseconds(250));
        Assert.True(gate.Pending);
        Assert.False(gate.TryTake(t.AddMilliseconds(400)));
        Assert.True(gate.TryTake(t.AddMilliseconds(600)));
        Assert.False(gate.TryTake(t.AddMilliseconds(900)));
        Assert.False(gate.Pending);
    }

    [Fact]
    public void A_burst_that_never_stops_still_reloads_after_the_max_wait()
    {
        var gate = new MdChangeGate(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(2));
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var ms = 0; ms < 1900; ms += 100)
        {
            gate.Note(t.AddMilliseconds(ms));
            Assert.False(gate.TryTake(t.AddMilliseconds(ms + 50)));
        }
        gate.Note(t.AddMilliseconds(2000));
        Assert.True(gate.TryTake(t.AddMilliseconds(2050)));
    }

    [Fact]
    public void A_stamp_tells_a_real_change_from_a_touch_of_nothing()
    {
        var path = Write("s.md", Encoding.UTF8.GetBytes("one"));
        var first = MdFileStamp.Of(path);
        Assert.Equal(first, MdFileStamp.Of(path));
        IoFile.WriteAllText(path, "one two");
        Assert.NotEqual(first, MdFileStamp.Of(path));
        Assert.Null(MdFileStamp.Of(IoPath.Combine(_dir, "none")));
    }

    // ---- zoom ----

    [Fact]
    public void Zoom_steps_through_the_levels_and_stops_at_the_ends()
    {
        Assert.Equal(1.1, MdZoom.Step(1.0, 1));
        Assert.Equal(0.9, MdZoom.Step(1.0, -1));
        Assert.Equal(1.25, MdZoom.Step(1.12, 1));
        Assert.Equal(3.0, MdZoom.Step(3.0, 1));
        Assert.Equal(0.5, MdZoom.Step(0.5, -1));
        Assert.Equal(3.0, MdZoom.Clamp(9));
        Assert.Equal(0.5, MdZoom.Clamp(0.01));
        Assert.Equal(1.0, MdZoom.Clamp(double.NaN));
        Assert.Equal("125%", MdZoom.Label(1.25));
    }

    // ---- headless arguments ----

    [Fact]
    public void Headless_arguments_need_a_file_and_say_what_is_missing()
    {
        var ok = MdArgs.Parse(new[] { "--app", "md", "--open", "C:\\a b\\x.md", "--snapshot", "out.png", "--pdf", "out.pdf" });
        Assert.True(ok.Headless);
        Assert.Null(ok.Error);
        Assert.Equal(("C:\\a b\\x.md", "out.png", "out.pdf"), (ok.OpenPath, ok.PngPath, ok.PdfPath));

        Assert.False(MdArgs.Parse(new[] { "--app", "md", "--open", "x.md" }).Headless);
        Assert.False(MdArgs.Parse(new[] { "--app", "md" }).Headless);
        Assert.Contains("--open", MdArgs.Parse(new[] { "--app", "md", "--snapshot", "o.png" }).Error);
        Assert.Contains("output path", MdArgs.Parse(new[] { "--app", "md", "--open", "x.md", "--snapshot" }).Error);
        Assert.Contains("output path", MdArgs.Parse(new[] { "--app", "md", "--open", "x.md", "--snapshot", "--pdf" }).Error);
    }
}
