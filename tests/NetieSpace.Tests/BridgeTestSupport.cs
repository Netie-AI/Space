using System.Net;
using System.Net.Sockets;
using System.Text;
using NetieSpace.Apps.Bridge;

namespace NetieSpace.Tests;

internal sealed record MpPart(string Headers, byte[] Body)
{
    public string Text => Encoding.UTF8.GetString(Body);
    public string Name => System.Text.RegularExpressions.Regex.Match(Headers, "name=\"([^\"]*)\"").Groups[1].Value;
}

internal sealed record Captured(Uri Uri, string Method, string ContentType, long? ContentLength, byte[] Body, List<MpPart> Parts);

internal static class Mp
{
    public static List<MpPart> Parse(string contentType, byte[] body)
    {
        var b = contentType.Substring(contentType.IndexOf("boundary=", StringComparison.Ordinal) + 9).Trim('"', ' ');
        var delim = Encoding.ASCII.GetBytes("--" + b);
        var parts = new List<MpPart>();
        var pos = IndexOf(body, delim, 0);
        while (pos >= 0)
        {
            var after = pos + delim.Length;
            if (after + 1 < body.Length && body[after] == '-' && body[after + 1] == '-') break;
            after += 2; // CRLF
            var next = IndexOf(body, delim, after);
            if (next < 0) throw new InvalidDataException("unterminated multipart body");
            var segment = body.AsSpan(after, next - after - 2).ToArray(); // minus CRLF before the delimiter
            var split = IndexOf(segment, Encoding.ASCII.GetBytes("\r\n\r\n"), 0);
            parts.Add(new MpPart(Encoding.UTF8.GetString(segment, 0, split), segment.AsSpan(split + 4).ToArray()));
            pos = next;
        }
        return parts;
    }

    private static int IndexOf(byte[] hay, byte[] needle, int from)
    {
        var i = hay.AsSpan(from).IndexOf(needle);
        return i < 0 ? -1 : i + from;
    }
}

internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _fn;
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);
    public List<Captured> Seen { get; } = new();

    public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn) => _fn = fn;

    public static StubHandler Replying(HttpStatusCode code, string body = "", string? location = null)
        => new(async (req, ct) =>
        {
            var res = new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (location is not null) res.Headers.Location = new Uri(location);
            await Task.CompletedTask;
            return res;
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        if (request.Content is not null)
        {
            using var ms = new MemoryStream();
            await request.Content.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            var ctype = request.Content.Headers.ContentType!.ToString();
            lock (Seen) Seen.Add(new Captured(request.RequestUri!, request.Method.Method, ctype,
                request.Content.Headers.ContentLength, bytes, Mp.Parse(ctype, bytes)));
        }
        return await _fn(request, ct);
    }
}

/// <summary>Real HTTP listener on 127.0.0.1 standing in for AirGPT.</summary>
internal sealed class FakeAirGpt : IDisposable
{
    private readonly HttpListener _l = new();
    public int Port { get; }
    public int Hits;
    public List<Captured> Seen { get; } = new();

    public FakeAirGpt(Func<HttpListenerContext, byte[], Task> respond)
    {
        Port = FreePort();
        _l.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _l.Start();
        _ = Task.Run(async () =>
        {
            while (_l.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _l.GetContextAsync(); } catch { return; }
                Interlocked.Increment(ref Hits);
                using var ms = new MemoryStream();
                await ctx.Request.InputStream.CopyToAsync(ms);
                var bytes = ms.ToArray();
                var ct = ctx.Request.ContentType ?? "";
                lock (Seen) Seen.Add(new Captured(ctx.Request.Url!, ctx.Request.HttpMethod, ct, ctx.Request.ContentLength64, bytes,
                    ct.Contains("boundary=") ? Mp.Parse(ct, bytes) : new()));
                await respond(ctx, bytes);
                ctx.Response.Close();
            }
        });
    }

    public static int FreePort()
    {
        var t = new TcpListener(IPAddress.Loopback, 0);
        t.Start();
        var p = ((IPEndPoint)t.LocalEndpoint).Port;
        t.Stop();
        return p;
    }

    public static async Task Json(HttpListenerContext c, int status, string json)
    {
        c.Response.StatusCode = status;
        c.Response.ContentType = "application/json";
        var b = Encoding.UTF8.GetBytes(json);
        await c.Response.OutputStream.WriteAsync(b);
    }

    public void Dispose() { try { _l.Stop(); _l.Close(); } catch { } }
}

internal sealed class BridgeTempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bridge-" + Guid.NewGuid().ToString("N"));
    public BridgeTempDir() => Directory.CreateDirectory(Path);
    public string File(string name, byte[]? content = null)
    {
        var p = System.IO.Path.Combine(Path, name);
        System.IO.File.WriteAllBytes(p, content ?? Encoding.UTF8.GetBytes("hello phone"));
        return p;
    }
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}
