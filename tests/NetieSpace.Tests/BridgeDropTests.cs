using System.Net;
using System.Text;
using NetieSpace.Apps.Bridge;

namespace NetieSpace.Tests;

public sealed class BridgeDropTests : IDisposable
{
    private readonly BridgeTempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private static byte[] AllBytes(int n)
    {
        var b = new byte[n];
        for (var i = 0; i < n; i++) b[i] = (byte)(i * 31 + i / 256);
        return b;
    }

    [Fact]
    public async Task Multipart_shape_names_filename_and_identical_bytes()
    {
        // spaces, a quote, and non-ASCII (e-acute, CJK) in the name
        var name = "my \"report\" \u00e9\u4e2d.bin";
        var content = AllBytes(300_000);
        var path = _dir.File(name, content);
        var stub = StubHandler.Replying(HttpStatusCode.OK, "{\"id\":\"m-42\"}");
        using var drop = new AirGptDrop("http://127.0.0.1:8765", stub);

        var r = await drop.SendAsync(path);

        Assert.Equal(DropStatus.Sent, r.Status);
        Assert.Equal("m-42", r.MessageId);
        var seen = Assert.Single(stub.Seen);
        Assert.Equal("POST", seen.Method);
        Assert.Equal("http://127.0.0.1:8765/api/messages", seen.Uri.ToString());
        Assert.StartsWith("multipart/form-data", seen.ContentType);
        Assert.Equal(seen.Body.Length, seen.ContentLength);
        Assert.Equal(new[] { "text", "device_name", "no_ai", "file" }, seen.Parts.Select(p => p.Name).ToArray());
        Assert.Equal("Netie Space", seen.Parts[1].Text);
        Assert.Equal("true", seen.Parts[2].Text);
        var file = seen.Parts[3];
        Assert.Contains("filename=\"my %22report%22 \u00e9\u4e2d.bin\"", file.Headers);
        Assert.Equal(content, file.Body);
    }

    [Fact]
    public async Task Header_injection_through_file_name_is_neutralised()
    {
        var path = _dir.File("a\r\nX-Evil: 1\r\n\r\nGOTCHA.txt");
        var stub = StubHandler.Replying(HttpStatusCode.OK, "{\"id\":1}");
        using var drop = new AirGptDrop(handler: stub);

        await drop.SendAsync(path);

        var file = stub.Seen.Single().Parts.Single(p => p.Name == "file");
        var lines = file.Headers.Split("\r\n");
        Assert.Equal(2, lines.Length); // Content-Disposition + Content-Type only
        Assert.DoesNotContain(lines, l => l.StartsWith("X-Evil"));
        Assert.Contains("%0D%0A", lines[0]);
        Assert.Equal("hello phone", file.Text);
    }

    [Fact]
    public async Task Path_segments_never_reach_the_server()
    {
        var path = _dir.File("..%2f..\\evil.txt");
        var stub = StubHandler.Replying(HttpStatusCode.OK, "{}");
        using var drop = new AirGptDrop(handler: stub);
        await drop.SendAsync(path);
        var h = stub.Seen.Single().Parts.Single(p => p.Name == "file").Headers;
        Assert.Contains("filename=\"evil.txt\"", h);
    }

    [Fact]
    public async Task Optional_text_and_session_id_are_sent_only_when_given()
    {
        var stub = StubHandler.Replying(HttpStatusCode.OK, "{}");
        using var drop = new AirGptDrop(handler: stub);
        await drop.SendAsync(_dir.File("a.txt"), text: "from space", sessionId: "s-1");
        var parts = stub.Seen.Single().Parts;
        Assert.Equal("from space", parts.Single(p => p.Name == "text").Text);
        Assert.Equal("s-1", parts.Single(p => p.Name == "session_id").Text);
    }

    [Theory]
    [InlineData(200, "{\"id\":\"abc\"}", DropStatus.Sent, "abc")]
    [InlineData(201, "{\"id\":7}", DropStatus.Sent, "7")]
    [InlineData(200, "{\"message\":{\"id\":\"nested\"}}", DropStatus.Sent, "nested")]
    [InlineData(200, "{\"duplicate\":true,\"id\":\"old\"}", DropStatus.Duplicate, "old")]
    [InlineData(409, "{\"detail\":\"already stored\"}", DropStatus.Duplicate, null)]
    [InlineData(400, "{\"detail\":\"Duplicate file\"}", DropStatus.Duplicate, null)]
    [InlineData(500, "{\"detail\":\"boom\"}", DropStatus.Refused, null)]
    [InlineData(413, "too big", DropStatus.Refused, null)]
    [InlineData(404, "", DropStatus.Refused, null)]
    public async Task Outcome_mapping(int code, string body, DropStatus want, string? id)
    {
        var stub = StubHandler.Replying((HttpStatusCode)code, body);
        using var drop = new AirGptDrop(handler: stub);
        var r = await drop.SendAsync(_dir.File("o" + code + ".txt"));
        Assert.Equal(want, r.Status);
        Assert.Equal(id, r.MessageId);
        Assert.Equal(code, r.HttpStatus);
        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public async Task Refused_carries_status_and_a_truncated_clean_message()
    {
        var noisy = "bad\u001b[31m thing\r\n" + new string('x', 5000);
        var stub = StubHandler.Replying(HttpStatusCode.UnprocessableEntity, "{\"detail\":" + System.Text.Json.JsonSerializer.Serialize(noisy) + "}");
        using var drop = new AirGptDrop(handler: stub);
        var r = await drop.SendAsync(_dir.File("n.txt"));
        Assert.Equal(DropStatus.Refused, r.Status);
        Assert.Equal(422, r.HttpStatus);
        Assert.True(r.Message!.Length <= AirGptDrop.MaxMessageChars + 3);
        Assert.DoesNotContain(r.Message, c => char.IsControl(c));
        Assert.StartsWith("bad [31m thing xxx", r.Message);
        Assert.EndsWith("...", r.Message);
    }

    [Fact]
    public async Task File_missing_directory_and_too_large_send_nothing()
    {
        var stub = StubHandler.Replying(HttpStatusCode.OK, "{}");
        using var drop = new AirGptDrop(handler: stub);
        Assert.Equal(DropStatus.FileMissing, (await drop.SendAsync(Path.Combine(_dir.Path, "nope.txt"))).Status);
        Assert.Equal(DropStatus.FileMissing, (await drop.SendAsync(_dir.Path)).Status);
        Assert.Equal(DropStatus.FileMissing, (await drop.SendAsync("")).Status);

        var big = Path.Combine(_dir.Path, "big.bin");
        using (var fs = new FileStream(big, FileMode.Create)) fs.SetLength(AirGptDrop.MaxBytes + 1);
        var r = await drop.SendAsync(big);
        Assert.Equal(DropStatus.TooLarge, r.Status);

        var exact = Path.Combine(_dir.Path, "exact.bin");
        using (var fs = new FileStream(exact, FileMode.Create)) fs.SetLength(AirGptDrop.MaxBytes);
        Assert.Equal(DropStatus.Sent, (await drop.SendAsync(exact)).Status); // limit is inclusive

        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public async Task Not_running_is_connection_refused_on_a_real_closed_port()
    {
        using var drop = new AirGptDrop($"http://127.0.0.1:{FakeAirGpt.FreePort()}");
        var r = await drop.SendAsync(_dir.File("a.txt"));
        Assert.Equal(DropStatus.NotRunning, r.Status);
        Assert.Equal(0, r.HttpStatus);
    }

    [Theory]
    [InlineData("http://example.com:8765")]
    [InlineData("http://127.0.0.1.evil.com:8765")]
    [InlineData("http://localhost.evil.com:8765")]
    [InlineData("http://localhost.:8765")]
    [InlineData("http://127.1:8765")]
    [InlineData("http://2130706433:8765")]
    [InlineData("http://0x7f.0.0.1:8765")]
    [InlineData("http://127.0.0.2:8765")]
    [InlineData("http://0.0.0.0:8765")]
    [InlineData("http://192.168.1.5:8765")]
    [InlineData("http://[::2]:8765")]
    [InlineData("http://[::ffff:127.0.0.1]:8765")]
    [InlineData("http://127.0.0.1@evil.com:8765")]
    [InlineData("http://evil.com@127.0.0.1:8765")]
    [InlineData("http://127.0.0.1:8765/other")]
    [InlineData("http://127.0.0.1:8765/?x=1")]
    [InlineData("http://127.0.0.1:99999")]
    [InlineData("http://127.0.0.1:")]
    [InlineData("https://127.0.0.1:8765")]
    [InlineData("ftp://127.0.0.1")]
    [InlineData("//127.0.0.1")]
    [InlineData("127.0.0.1:8765")]
    [InlineData("")]
    public async Task Non_loopback_target_is_refused_and_sends_nothing(string url)
    {
        var stub = StubHandler.Replying(HttpStatusCode.OK, "{}");
        Assert.Throws<ArgumentException>(() => new AirGptDrop(url, stub));
        Assert.Throws<ArgumentException>(() => AirGptDrop.ResolveBaseUrl(url.Length == 0 ? " x" : url));
        Assert.Equal(0, stub.Calls);
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData("http://127.0.0.1:8765", "http://127.0.0.1:8765/")]
    [InlineData("http://127.0.0.1:9000/", "http://127.0.0.1:9000/")]
    [InlineData("HTTP://LOCALHOST:9001", "http://127.0.0.1:9001/")]
    [InlineData("http://[::1]:9002", "http://[::1]:9002/")]
    [InlineData("http://localhost", "http://127.0.0.1/")]
    public void Loopback_forms_are_accepted_and_pinned_to_a_literal_address(string url, string pinned)
    {
        Assert.True(AirGptDrop.TryParseBaseUrl(url, out var uri, out _, out _));
        Assert.Equal(pinned, uri.ToString());
    }

    [Fact]
    public async Task Localhost_is_pinned_to_127_0_0_1_so_a_hosts_file_cannot_redirect_it()
    {
        // Stub path: the request URI carries the literal address, never the name.
        var stub = StubHandler.Replying(HttpStatusCode.OK, "{}");
        using (var d = new AirGptDrop("http://localhost:8765", stub))
            await d.SendAsync(_dir.File("a.txt"));
        Assert.Equal("127.0.0.1", stub.Seen.Single().Uri.Host);

        // Real socket path: a listener on 127.0.0.1 is reached through "localhost".
        using var server = new FakeAirGpt((c, _) => FakeAirGpt.Json(c, 200, "{\"id\":\"live\"}"));
        using var drop = new AirGptDrop($"http://localhost:{server.Port}");
        var r = await drop.SendAsync(_dir.File("b.txt"));
        Assert.Equal(DropStatus.Sent, r.Status);
        Assert.Equal("live", r.MessageId);
        Assert.Equal(1, server.Hits);
    }

    [Fact]
    public async Task Redirects_are_never_followed_on_the_real_socket_path()
    {
        using var target = new FakeAirGpt((c, _) => FakeAirGpt.Json(c, 200, "{\"id\":\"stolen\"}"));
        using var first = new FakeAirGpt((c, _) =>
        {
            c.Response.StatusCode = 307; // 307 would replay the POST body if followed
            c.Response.RedirectLocation = $"http://127.0.0.1:{target.Port}/api/messages";
            return Task.CompletedTask;
        });
        using var drop = new AirGptDrop($"http://127.0.0.1:{first.Port}");
        var r = await drop.SendAsync(_dir.File("a.txt"));
        Assert.Equal(DropStatus.Refused, r.Status);
        Assert.Equal(307, r.HttpStatus);
        Assert.Contains("redirect not followed", r.Message);
        Assert.Equal(1, first.Hits);
        Assert.Equal(0, target.Hits);
    }

    [Fact]
    public async Task Redirect_status_through_a_stub_is_refused_not_success()
    {
        var stub = StubHandler.Replying(HttpStatusCode.Found, "", "http://evil.example/steal");
        using var drop = new AirGptDrop(handler: stub);
        var r = await drop.SendAsync(_dir.File("a.txt"));
        Assert.Equal(DropStatus.Refused, r.Status);
        Assert.Equal(302, r.HttpStatus);
        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public async Task A_failed_send_is_never_retried()
    {
        var stub = new StubHandler((_, _) => throw new HttpRequestException("connection reset"));
        using var drop = new AirGptDrop(handler: stub);
        var r = await drop.SendAsync(_dir.File("a.txt"));
        Assert.Equal(DropStatus.Refused, r.Status);
        Assert.Equal(0, r.HttpStatus);
        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public async Task The_body_can_only_be_serialized_once()
    {
        using var fs = new FileStream(_dir.File("a.txt"), FileMode.Open, FileAccess.Read);
        using var body = new AirGptDrop.MultipartBody(fs, fs.Length, "a.txt", "", null);
        await body.CopyToAsync(Stream.Null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => body.CopyToAsync(Stream.Null));
    }

    [Fact]
    public async Task Timeout_reports_refused_and_cancel_throws()
    {
        var hang = new StubHandler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); });
        using (var drop = new AirGptDrop(handler: hang, timeout: TimeSpan.FromMilliseconds(150)))
        {
            var r = await drop.SendAsync(_dir.File("a.txt"));
            Assert.Equal(DropStatus.Refused, r.Status);
            Assert.Contains("timed out", r.Message);
        }
        using var cts = new CancellationTokenSource(150);
        using var drop2 = new AirGptDrop(handler: hang);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => drop2.SendAsync(_dir.File("b.txt"), cancel: cts.Token));
    }

    [Fact]
    public async Task Same_file_cannot_be_sent_twice_at_once()
    {
        var gate = new TaskCompletionSource();
        var stub = new StubHandler(async (_, _) =>
        {
            await gate.Task;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"1\"}") };
        });
        using var drop = new AirGptDrop(handler: stub);
        var path = _dir.File("dbl.txt");
        var first = drop.SendAsync(path);
        while (stub.Calls == 0) await Task.Delay(5);

        var second = await drop.SendAsync(path); // double click
        Assert.Equal(DropStatus.Refused, second.Status);
        Assert.Contains("already being sent", second.Message);

        gate.SetResult();
        Assert.Equal(DropStatus.Sent, (await first).Status);
        Assert.Equal(1, stub.Calls);
        Assert.Equal(DropStatus.Sent, (await drop.SendAsync(path)).Status); // free again afterwards
    }

    [Fact]
    public async Task Large_file_is_streamed_not_buffered()
    {
        var path = Path.Combine(_dir.Path, "large.bin");
        using (var fs = new FileStream(path, FileMode.Create)) fs.SetLength(64L * 1024 * 1024);
        long sent = 0;
        var stub = new HttpMessageHandlerFn(async (req, ct) =>
        {
            await using var sink = new CountingSink();
            await req.Content!.CopyToAsync(sink, ct);
            sent = sink.Count;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });
        using var drop = new AirGptDrop(handler: stub);
        var before = GC.GetTotalAllocatedBytes(true);
        var r = await drop.SendAsync(path);
        var allocated = GC.GetTotalAllocatedBytes(true) - before;
        Assert.Equal(DropStatus.Sent, r.Status);
        Assert.True(sent > 64L * 1024 * 1024);
        Assert.True(allocated < 24L * 1024 * 1024, $"allocated {allocated} bytes for a 64 MB file");
    }

    [Fact]
    public async Task Symlink_alias_of_a_file_in_flight_is_also_refused()
    {
        if (OperatingSystem.IsWindows()) return;
        var gate = new TaskCompletionSource();
        var stub = new StubHandler(async (_, _) =>
        {
            await gate.Task;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });
        using var drop = new AirGptDrop(handler: stub);
        var real = _dir.File("real.txt");
        var link = Path.Combine(_dir.Path, "link.txt");
        File.CreateSymbolicLink(link, real);
        var first = drop.SendAsync(real);
        while (stub.Calls == 0) await Task.Delay(5);
        var second = await drop.SendAsync(link);
        Assert.Equal(DropStatus.Refused, second.Status);
        Assert.Contains("already being sent", second.Message);
        gate.SetResult();
        await first;
        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public async Task Fifo_does_not_hang_the_send()
    {
        if (!OperatingSystem.IsLinux()) return;
        var fifo = Path.Combine(_dir.Path, "pipe");
        using (var p = System.Diagnostics.Process.Start("mkfifo", fifo)!) await p.WaitForExitAsync();
        var stub = StubHandler.Replying(HttpStatusCode.OK, "{}");
        using var drop = new AirGptDrop(handler: stub);
        var t = drop.SendAsync(fifo);
        Assert.Same(t, await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(15))));
        Assert.Equal(DropStatus.FileMissing, (await t).Status);
        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public void Clean_removes_bidi_override_zero_width_and_keeps_emoji()
    {
        var s = AirGptDrop.Clean("a\u202Eb\u200Bc\u2028d \U0001F600");
        Assert.Equal("abc d \U0001F600", s);
    }

    private sealed class HttpMessageHandlerFn : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _f;
        public HttpMessageHandlerFn(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> f) => _f = f;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => _f(r, ct);
    }

    private sealed class CountingSink : Stream
    {
        public long Count;
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => Count; public override long Position { get => Count; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => Count += c;
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> b, CancellationToken ct = default) { Count += b.Length; return ValueTask.CompletedTask; }
    }
}
