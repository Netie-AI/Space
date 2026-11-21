using System.Net;
using System.Text;
using NetieSpace.Apps.Bridge;

namespace NetieSpace.Tests;

public sealed class BridgeCliTests : IDisposable
{
    private readonly BridgeTempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private static (int code, string o, string e) Run(string[] args, HttpMessageHandler? h = null, string? airGptUrl = null)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = BridgeCli.Run(args, o, e, h, k => k == "AIRGPT_URL" ? airGptUrl : null);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void Without_yes_prints_one_disclosure_line_and_sends_nothing()
    {
        var stub = StubHandler.Replying(HttpStatusCode.OK, "{\"id\":\"x\"}");
        var (code, o, e) = Run(new[] { "--send-to-phone", _dir.File("tax.pdf") }, stub);
        Assert.Equal(2, code);
        Assert.Equal(0, stub.Calls);
        Assert.Equal("", e);
        var line = Assert.Single(o.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("tax.pdf", line);
        Assert.Contains("copied to AirGPT on this PC at port 8765", line);
        Assert.Contains("devices paired with AirGPT can then fetch it", line);
        Assert.Contains("--yes", line);
    }

    [Fact]
    public void Without_yes_nothing_goes_over_a_real_socket_either()
    {
        using var server = new FakeAirGpt((c, _) => FakeAirGpt.Json(c, 200, "{}"));
        var (code, o, _) = Run(new[] { "--send-to-phone", _dir.File("a.txt") }, null, $"http://127.0.0.1:{server.Port}");
        Assert.Equal(2, code);
        Assert.Contains($"port {server.Port}", o);
        Assert.Equal(0, server.Hits);
    }

    [Fact]
    public void With_yes_sends_once_and_reports_the_message_id()
    {
        var stub = StubHandler.Replying(HttpStatusCode.OK, "{\"id\":\"m-9\"}");
        var (code, o, e) = Run(new[] { "--send-to-phone", _dir.File("a.txt"), "--yes" }, stub);
        Assert.Equal(0, code);
        Assert.Equal(1, stub.Calls);
        Assert.Contains("Sending:", o);
        Assert.Contains("at port 8765", o);
        Assert.Contains("message id m-9", o);
        Assert.Equal("", e);
    }

    [Fact]
    public void Yes_may_come_before_the_file_flag()
    {
        var stub = StubHandler.Replying(HttpStatusCode.OK, "{\"id\":\"1\"}");
        var (code, _, _) = Run(new[] { "--yes", "--send-to-phone", _dir.File("a.txt") }, stub);
        Assert.Equal(0, code);
        Assert.Equal(1, stub.Calls);
    }

    [Fact]
    public void Duplicate_exits_zero_and_says_nothing_new_was_stored()
    {
        var stub = StubHandler.Replying(HttpStatusCode.Conflict, "{\"detail\":\"duplicate\"}");
        var (code, o, _) = Run(new[] { "--send-to-phone", _dir.File("a.txt"), "--yes" }, stub);
        Assert.Equal(0, code);
        Assert.Contains("already has this file", o);
    }

    [Fact]
    public void Not_running_exits_one_and_names_the_port()
    {
        var port = FakeAirGpt.FreePort();
        var (code, _, e) = Run(new[] { "--send-to-phone", _dir.File("a.txt"), "--yes" }, null, $"http://127.0.0.1:{port}");
        Assert.Equal(1, code);
        Assert.Contains($"not running on this PC at port {port}", e);
    }

    [Fact]
    public void Refused_shows_status_and_server_message_on_stderr()
    {
        var stub = StubHandler.Replying(HttpStatusCode.RequestEntityTooLarge, "{\"detail\":\"file too big\"}");
        var (code, o, e) = Run(new[] { "--send-to-phone", _dir.File("a.txt"), "--yes" }, stub);
        Assert.Equal(1, code);
        Assert.Contains("HTTP 413", e);
        Assert.Contains("file too big", e);
        Assert.DoesNotContain("Sent to", o);
    }

    [Fact]
    public void Missing_file_exits_one_without_a_disclosure_and_sends_nothing()
    {
        var stub = StubHandler.Replying(HttpStatusCode.OK, "{}");
        var (code, o, e) = Run(new[] { "--send-to-phone", Path.Combine(_dir.Path, "nope.txt"), "--yes" }, stub);
        Assert.Equal(1, code);
        Assert.Equal(0, stub.Calls);
        Assert.Equal("", o);
        Assert.Contains("not found", e);
    }

    [Fact]
    public void Too_large_exits_one_and_sends_nothing()
    {
        var big = Path.Combine(_dir.Path, "big.bin");
        using (var fs = new FileStream(big, FileMode.Create)) fs.SetLength(AirGptDrop.MaxBytes + 1);
        var stub = StubHandler.Replying(HttpStatusCode.OK, "{}");
        var (code, _, e) = Run(new[] { "--send-to-phone", big, "--yes" }, stub);
        Assert.Equal(1, code);
        Assert.Equal(0, stub.Calls);
        Assert.Contains("too large", e);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--yes")]
    [InlineData("--send-to-phone")]
    [InlineData("--send-to-phone --yes")]
    [InlineData("--send-to-phone a.txt --bogus")]
    [InlineData("--send-to-phone a.txt b.txt")]
    [InlineData("--send-to-phone a.txt --json")]
    [InlineData("--send-to-phone a.txt --yes --yes")]
    public void Bad_arguments_exit_two_and_send_nothing(string joined)
    {
        var stub = StubHandler.Replying(HttpStatusCode.OK, "{}");
        var args = joined.Length == 0 ? Array.Empty<string>() : joined.Split(' ');
        var (code, _, e) = Run(args, stub);
        Assert.Equal(2, code);
        Assert.Equal(0, stub.Calls);
        Assert.Contains("usage:", e);
    }

    [Theory]
    [InlineData("http://example.com:8765")]
    [InlineData("http://192.168.1.9:8765")]
    [InlineData("http://127.0.0.1.evil.com:8765")]
    [InlineData("https://127.0.0.1:8765")]
    public void Env_url_with_another_host_is_refused_even_with_yes(string env)
    {
        var stub = StubHandler.Replying(HttpStatusCode.OK, "{}");
        var (code, _, e) = Run(new[] { "--send-to-phone", _dir.File("a.txt"), "--yes" }, stub, env);
        Assert.Equal(2, code);
        Assert.Equal(0, stub.Calls);
        Assert.Contains("AIRGPT_URL", e);
    }

    [Fact]
    public void Env_url_may_change_the_port_and_the_disclosure_shows_it()
    {
        var stub = StubHandler.Replying(HttpStatusCode.OK, "{\"id\":\"p\"}");
        var (code, o, _) = Run(new[] { "--send-to-phone", _dir.File("a.txt"), "--yes" }, stub, "http://localhost:9123");
        Assert.Equal(0, code);
        Assert.Contains("port 9123", o);
        Assert.Equal("http://127.0.0.1:9123/api/messages", stub.Seen.Single().Uri.ToString());
    }

    [Fact]
    public void Json_file_is_written_for_sent_and_for_not_confirmed()
    {
        var json = Path.Combine(_dir.Path, "out.json");
        var stub = StubHandler.Replying(HttpStatusCode.OK, "{\"id\":\"j1\"}");
        var file = _dir.File("a.txt");

        Run(new[] { "--send-to-phone", file, "--json", json }, stub);
        var unconfirmed = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json)).RootElement;
        Assert.Equal("not_confirmed", unconfirmed.GetProperty("status").GetString());
        Assert.Equal(0, stub.Calls);

        Run(new[] { "--send-to-phone", file, "--yes", "--json", json }, stub);
        var sent = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json)).RootElement;
        Assert.Equal("sent", sent.GetProperty("status").GetString());
        Assert.Equal("j1", sent.GetProperty("messageId").GetString());
        Assert.Equal("a.txt", sent.GetProperty("file").GetString());
        Assert.Equal(8765, sent.GetProperty("port").GetInt32());
    }

    [Fact]
    public void Real_socket_round_trip_with_the_default_handler()
    {
        var content = Encoding.UTF8.GetBytes(new string('z', 100_000));
        using var server = new FakeAirGpt((c, _) => FakeAirGpt.Json(c, 200, "{\"id\":\"real-1\"}"));
        var (code, o, _) = Run(new[] { "--send-to-phone", _dir.File("big name \u00e9.txt", content), "--yes" }, null, $"http://127.0.0.1:{server.Port}");
        Assert.Equal(0, code);
        Assert.Contains("real-1", o);
        var part = server.Seen.Single().Parts.Single(p => p.Name == "file");
        Assert.Equal(content, part.Body);
        Assert.Contains("filename=\"big name \u00e9.txt\"", part.Headers);
    }

    [Fact]
    public void Json_path_equal_to_the_file_is_refused_and_the_file_is_untouched()
    {
        var f = _dir.File("secret.txt", Encoding.UTF8.GetBytes("DATA"));
        var stub = StubHandler.Replying(HttpStatusCode.OK, "{}");
        var (code, _, e) = Run(new[] { "--send-to-phone", f, "--json", f }, stub);
        Assert.Equal(2, code);
        Assert.Equal("DATA", File.ReadAllText(f));
        Assert.Equal(0, stub.Calls);
        Assert.Contains("--json", e);
    }

    [Fact]
    public void Handles_recognises_the_flag_only_exactly()
    {
        Assert.True(BridgeCli.Handles(new[] { "--yes", "--send-to-phone", "x" }));
        Assert.False(BridgeCli.Handles(new[] { "--app", "pdf" }));
        Assert.False(BridgeCli.Handles(new[] { "--send-to-phones" }));
    }
}
