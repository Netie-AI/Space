using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NetieSpace.Apps.Bridge;

/// <summary>Closed set of outcomes of one send. Cancellation is not an outcome: it throws OperationCanceledException.</summary>
public enum DropStatus
{
    /// <summary>AirGPT accepted the file (2xx).</summary>
    Sent,
    /// <summary>AirGPT already has this content (rejected by hash); nothing new was stored.</summary>
    Duplicate,
    /// <summary>Nothing is listening on the AirGPT port. Nothing was sent.</summary>
    NotRunning,
    /// <summary>Any other answer or failure: HTTP error, redirect (never followed), timeout, broken connection.</summary>
    Refused,
    /// <summary>The file is over the 100 MB AirGPT limit. Nothing was sent.</summary>
    TooLarge,
    /// <summary>The file does not exist or is not a file. Nothing was sent.</summary>
    FileMissing,
}

/// <summary>Outcome of <see cref="AirGptDrop.SendAsync"/>. HttpStatus is 0 when no HTTP answer was received.</summary>
public sealed record DropResult(DropStatus Status, string? MessageId, int HttpStatus, string? Message);

/// <summary>
/// Posts one file to AirGPT running on this PC (multipart POST /api/messages). Loopback only:
/// the base URL host must be the literal 127.0.0.1, ::1 or localhost, the request always goes to a
/// literal loopback address (never resolved), redirects are never followed and nothing is retried.
/// The file is streamed, never loaded into memory.
/// </summary>
public sealed class AirGptDrop : IDisposable
{
    public const long MaxBytes = 100L * 1024 * 1024;
    public const string DefaultUrl = "http://127.0.0.1:8765";
    public const string DeviceName = "Netie Space";
    internal const int MaxMessageChars = 200;
    private const int MaxResponseBytes = 64 * 1024;

    private static readonly ConcurrentDictionary<string, byte> InFlight = new(StringComparer.Ordinal);

    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly TimeSpan _timeout;

    /// <summary>Loopback base URL actually used, e.g. http://127.0.0.1:8765/.</summary>
    public Uri BaseUri { get; }
    public int Port => BaseUri.Port;

    /// <param name="baseUrl">null = <see cref="DefaultUrl"/>. Throws ArgumentException when the host is not loopback.</param>
    /// <param name="handler">Test seam. null = a SocketsHttpHandler pinned to the loopback address.</param>
    /// <param name="timeout">Overall limit for one send (default 5 minutes).</param>
    public AirGptDrop(string? baseUrl = null, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        if (!TryParseBaseUrl(baseUrl ?? DefaultUrl, out var uri, out var ip, out var error))
            throw new ArgumentException(error, nameof(baseUrl));
        BaseUri = uri;
        _endpoint = new Uri(uri, "/api/messages");
        _timeout = timeout ?? TimeSpan.FromMinutes(5);
        if (handler is null)
        {
            var port = uri.Port;
            _http = new HttpClient(new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.None,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                // Whatever host the request carries, the socket goes to the loopback literal only.
                ConnectCallback = async (_, ct) =>
                {
                    var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(ip, port), ct).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                },
            }, disposeHandler: true);
        }
        else
        {
            _http = new HttpClient(handler, disposeHandler: false);
        }
        _http.Timeout = Timeout.InfiniteTimeSpan; // the per-call token below owns the deadline
    }

    /// <summary>Base URL from AIRGPT_URL (port may change, host may not) or the default. Throws ArgumentException on a non-loopback host.</summary>
    public static string ResolveBaseUrl(string? airGptUrlEnv)
    {
        var v = airGptUrlEnv?.Trim();
        if (string.IsNullOrEmpty(v)) return DefaultUrl;
        if (!TryParseBaseUrl(v, out _, out _, out var error))
            throw new ArgumentException("AIRGPT_URL: " + error);
        return v;
    }

    /// <summary>
    /// Strict loopback check on the text as typed, not on a normalized form: http only, no credentials,
    /// no path/query, host exactly 127.0.0.1, [::1] or localhost (so 127.1, 2130706433, localhost. and
    /// DNS names are all refused). localhost maps to 127.0.0.1 without any lookup.
    /// </summary>
    public static bool TryParseBaseUrl(string? url, out Uri uri, out IPAddress address, out string error)
    {
        uri = null!;
        address = IPAddress.Loopback;
        error = "";
        var s = url?.Trim();
        if (string.IsNullOrEmpty(s)) { error = "empty URL"; return false; }
        foreach (var c in s)
            if (c <= ' ' || c == '\\' || c >= 0x7f) { error = "invalid character in URL"; return false; }
        const string scheme = "http://";
        if (!s.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) { error = "only http:// to loopback is allowed"; return false; }
        var rest = s.Substring(scheme.Length);
        var end = rest.IndexOfAny(new[] { '/', '?', '#' });
        var authority = end < 0 ? rest : rest.Substring(0, end);
        var tail = end < 0 ? "" : rest.Substring(end);
        if (tail.Length > 0 && tail != "/") { error = "URL must not carry a path, query or fragment"; return false; }
        if (authority.Contains('@')) { error = "credentials in the URL are not allowed"; return false; }

        string host, portText;
        if (authority.StartsWith('['))
        {
            var close = authority.IndexOf(']');
            if (close < 0) { error = "malformed host"; return false; }
            host = authority.Substring(0, close + 1);
            var after = authority.Substring(close + 1);
            if (after.Length > 0 && after[0] != ':') { error = "malformed host"; return false; }
            portText = after.Length > 0 ? after.Substring(1) : "";
        }
        else
        {
            var colon = authority.LastIndexOf(':');
            host = colon < 0 ? authority : authority.Substring(0, colon);
            portText = colon < 0 ? "" : authority.Substring(colon + 1);
        }

        if (host.Equals("127.0.0.1", StringComparison.Ordinal) || host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            address = IPAddress.Loopback;
        else if (host.Equals("[::1]", StringComparison.Ordinal))
            address = IPAddress.IPv6Loopback;
        else { error = "host must be 127.0.0.1, ::1 or localhost (AirGPT runs on this PC only)"; return false; }

        var port = 80;
        if (portText.Length > 0)
        {
            if (portText.Length > 5 || !portText.All(char.IsAsciiDigit) || !int.TryParse(portText, out port) || port is < 1 or > 65535)
            { error = "invalid port"; return false; }
        }
        else if (authority.EndsWith(':')) { error = "invalid port"; return false; }

        uri = new Uri(address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"http://[::1]:{port}/"
            : $"http://127.0.0.1:{port}/");
        return true;
    }

    /// <summary>
    /// Sends one file. At most one attempt: no retry, no redirect. A timeout or a connection that breaks
    /// after the body started returns Refused with HttpStatus 0 (the file may or may not have arrived).
    /// Throws OperationCanceledException when <paramref name="cancel"/> fires.
    /// </summary>
    public async Task<DropResult> SendAsync(string path, string? text = null, string? sessionId = null, CancellationToken cancel = default)
    {
        FileInfo info;
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return new(DropStatus.FileMissing, null, 0, "no file given");
            info = new FileInfo(Path.GetFullPath(path));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return new(DropStatus.FileMissing, null, 0, "invalid file path");
        }
        if (!info.Exists) return new(DropStatus.FileMissing, null, 0, "file not found");
        long length;
        try { length = info.Length; }
        catch (IOException) { return new(DropStatus.FileMissing, null, 0, "file not readable"); }
        if (length > MaxBytes)
            return new(DropStatus.TooLarge, null, 0, $"{length} bytes; limit is {MaxBytes}");

        string resolved;
        try { resolved = (info.ResolveLinkTarget(true) as FileInfo)?.FullName ?? info.FullName; }
        catch (IOException) { resolved = info.FullName; }
        var key = OperatingSystem.IsWindows() ? resolved.ToUpperInvariant() : resolved;
        if (!InFlight.TryAdd(key, 0))
            return new(DropStatus.Refused, null, 0, "this file is already being sent");
        try
        {
            FileStream stream;
            try
            {
                // Opened off-thread: a FIFO or device would block an open forever; the deadline must still apply.
                var opening = Task.Run(() => new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan));
                var done = await Task.WhenAny(opening, Task.Delay(TimeSpan.FromSeconds(5), cancel)).ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();
                if (done != opening)
                {
                    _ = opening.ContinueWith(t => { if (t.IsCompletedSuccessfully) t.Result.Dispose(); }, TaskScheduler.Default);
                    return new(DropStatus.FileMissing, null, 0, "file could not be opened (not a regular file?)");
                }
                stream = await opening.ConfigureAwait(false);
            }
            catch (FileNotFoundException) { return new(DropStatus.FileMissing, null, 0, "file not found"); }
            catch (DirectoryNotFoundException) { return new(DropStatus.FileMissing, null, 0, "file not found"); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { return new(DropStatus.FileMissing, null, 0, "file not readable"); }
            if (!stream.CanSeek) { stream.Dispose(); return new(DropStatus.FileMissing, null, 0, "not a regular file"); }
            length = stream.Length; // the opened file is the truth, not the earlier stat
            if (length > MaxBytes)
            {
                stream.Dispose();
                return new(DropStatus.TooLarge, null, 0, $"{length} bytes; limit is {MaxBytes}");
            }

            using var body = new MultipartBody(stream, length, MultipartBody.SafeFileName(info.Name), text ?? "", sessionId);
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = body, Version = HttpVersion.Version11 };
            request.Headers.ExpectContinue = false;

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            deadline.CancelAfter(_timeout);
            try
            {
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                return await Interpret(response, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
            {
                return new(DropStatus.Refused, null, 0, "timed out; the file may or may not have arrived");
            }
            catch (HttpRequestException e) when (!body.Started && IsConnectionRefused(e))
            {
                return new(DropStatus.NotRunning, null, 0, "connection refused");
            }
            catch (Exception e) when (e is HttpRequestException or IOException or SocketException or ObjectDisposedException or InvalidOperationException)
            {
                var msg = body.Started
                    ? "connection failed while sending; the file may or may not have arrived"
                    : "could not connect: " + Clean(e.GetBaseException().Message);
                return new(DropStatus.Refused, null, 0, msg);
            }
        }
        finally
        {
            InFlight.TryRemove(key, out _);
        }
    }

    private static bool IsConnectionRefused(Exception e)
    {
        for (var x = e; x is not null; x = x.InnerException!)
        {
            if (x is SocketException { SocketErrorCode: SocketError.ConnectionRefused }) return true;
            if (x.InnerException is null) break;
        }
        return false;
    }

    private static async Task<DropResult> Interpret(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        var raw = await ReadCapped(response, ct).ConfigureAwait(false);
        string? id = null, message = null;
        var duplicateFlag = false;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var root = doc.RootElement;
                id = FindId(root);
                message = FindText(root, "detail") ?? FindText(root, "error") ?? FindText(root, "message");
                if (root.TryGetProperty("duplicate", out var d) && d.ValueKind == JsonValueKind.True) duplicateFlag = true;
                if (root.TryGetProperty("deduplicated", out var d2) && d2.ValueKind == JsonValueKind.True) duplicateFlag = true;
            }
        }
        catch (JsonException)
        {
            message = Encoding.UTF8.GetString(raw);
        }
        message = Clean(message);

        var is2xx = status is >= 200 and <= 299;
        if (is2xx)
            return duplicateFlag
                ? new(DropStatus.Duplicate, id, status, message)
                : new(DropStatus.Sent, id, status, message);
        if (status == 409 || (message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) && status is >= 400 and <= 499))
            return new(DropStatus.Duplicate, id, status, message);
        if (status is >= 300 and <= 399)
            return new(DropStatus.Refused, null, status, "redirect not followed" + (message.Length > 0 ? ": " + message : ""));
        return new(DropStatus.Refused, null, status, message);
    }

    private static string? FindId(JsonElement root)
    {
        foreach (var name in new[] { "id", "message_id", "messageId" })
            if (root.TryGetProperty(name, out var v) && ScalarText(v) is { Length: > 0 } t) return Clean(t);
        if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object) return FindId(m);
        return null;
    }

    private static string? FindText(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? ScalarText(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        _ => null,
    };

    private static async Task<byte[]> ReadCapped(HttpResponseMessage response, CancellationToken ct)
    {
        await using var s = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buf = new byte[MaxResponseBytes];
        var n = 0;
        while (n < buf.Length)
        {
            var r = await s.ReadAsync(buf.AsMemory(n), ct).ConfigureAwait(false);
            if (r == 0) break;
            n += r;
        }
        return buf.AsSpan(0, n).ToArray();
    }

    /// <summary>Drops control characters (a server cannot paint the terminal), collapses whitespace, truncates.</summary>
    internal static string Clean(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(Math.Min(s.Length, MaxMessageChars + 3));
        var lastSpace = false;
        foreach (var r in s.EnumerateRunes())
        {
            if (sb.Length >= MaxMessageChars) { sb.Append("..."); break; }
            var cat = Rune.GetUnicodeCategory(r);
            if (cat is UnicodeCategory.Format or UnicodeCategory.OtherNotAssigned or UnicodeCategory.PrivateUse) continue;
            if (Rune.IsControl(r) || Rune.IsWhiteSpace(r) || cat is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                if (!lastSpace && sb.Length > 0) sb.Append(' ');
                lastSpace = true;
            }
            else { sb.Append(r.ToString()); lastSpace = false; }
        }
        return sb.ToString().TrimEnd();
    }

    public void Dispose() => _http.Dispose();

    /// <summary>
    /// Hand-built multipart/form-data so the file name header is exact (UTF-8, quotes and CR/LF
    /// percent-encoded as browsers do) and the length is known up front. Streams the file once;
    /// a second serialization attempt throws, so a replay can never double-send.
    /// </summary>
    internal sealed class MultipartBody : HttpContent
    {
        private readonly FileStream _file;
        private readonly long _length;
        private readonly byte[] _head;
        private readonly byte[] _tail;
        private int _used;

        public bool Started => Volatile.Read(ref _used) != 0;
        public string Boundary { get; }

        public MultipartBody(FileStream file, long length, string fileName, string text, string? sessionId)
        {
            _file = file;
            _length = length;
            Boundary = "NetieSpace" + Guid.NewGuid().ToString("N");
            var h = new StringBuilder();
            AddField(h, "text", text);
            AddField(h, "device_name", DeviceName);
            if (!string.IsNullOrEmpty(sessionId)) AddField(h, "session_id", sessionId);
            AddField(h, "no_ai", "true");
            h.Append("--").Append(Boundary).Append("\r\n")
             .Append("Content-Disposition: form-data; name=\"file\"; filename=\"").Append(fileName).Append("\"\r\n")
             .Append("Content-Type: application/octet-stream\r\n\r\n");
            _head = new UTF8Encoding(false).GetBytes(h.ToString());
            _tail = Encoding.ASCII.GetBytes("\r\n--" + Boundary + "--\r\n");
            Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data") { Parameters = { new NameValueHeaderValue("boundary", Boundary) } };
        }

        private void AddField(StringBuilder sb, string name, string value)
            => sb.Append("--").Append(Boundary).Append("\r\n")
                 .Append("Content-Disposition: form-data; name=\"").Append(name).Append("\"\r\n\r\n")
                 .Append(value).Append("\r\n");

        /// <summary>Last path segment only; CR, LF and double quote percent-encoded, other control chars replaced.</summary>
        internal static string SafeFileName(string name)
        {
            var cut = name.LastIndexOfAny(new[] { '/', '\\' });
            if (cut >= 0) name = name.Substring(cut + 1);
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                if (c == '\r') sb.Append("%0D");
                else if (c == '\n') sb.Append("%0A");
                else if (c == '"') sb.Append("%22");
                else if (char.IsControl(c)) sb.Append('_');
                else sb.Append(c);
                if (sb.Length > 400) break;
            }
            var result = sb.ToString().Trim();
            return result.Length == 0 ? "file" : result;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _head.Length + _length + _tail.Length;
            return true;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _used, 1) != 0)
                throw new InvalidOperationException("multipart body already consumed; refusing to send twice");
            await stream.WriteAsync(_head, cancellationToken).ConfigureAwait(false);
            var buf = new byte[81920];
            var remaining = _length;
            while (remaining > 0)
            {
                var r = await _file.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, remaining)), cancellationToken).ConfigureAwait(false);
                if (r == 0) throw new IOException("file shrank while sending");
                await stream.WriteAsync(buf.AsMemory(0, r), cancellationToken).ConfigureAwait(false);
                remaining -= r;
            }
            await stream.WriteAsync(_tail, cancellationToken).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _file.Dispose();
            base.Dispose(disposing);
        }
    }
}
