using System.Management;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace NetieSpace.Services.Licensing;

public sealed class LicenseApiClient
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public LicenseApiClient(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<LicenseAuthResult> RegisterAsync(string email, string password, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync($"{_baseUrl}/v1/register", new { email, password }, ct);
        return await ReadAuthAsync(res, ct);
    }

    public async Task<LicenseAuthResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync($"{_baseUrl}/v1/login", new { email, password }, ct);
        return await ReadAuthAsync(res, ct);
    }

    public async Task<CheckoutResult> CreateCheckoutAsync(
        string? token,
        string? email,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/checkout");
        if (!string.IsNullOrWhiteSpace(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new { email });
        var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            return CheckoutResult.Fail(ParseError(body), ParseErrorCode(body));

        var dto = JsonSerializer.Deserialize<CheckoutDto>(body, JsonOpts);
        if (string.IsNullOrWhiteSpace(dto?.CheckoutUrl))
            return CheckoutResult.Fail("No checkout URL returned");

        return CheckoutResult.Ok(dto.CheckoutUrl, dto.SessionId);
    }

    public async Task<StripeStatusResult> GetStripeStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var res = await _http.GetAsync($"{_baseUrl}/v1/stripe/status", ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                return StripeStatusResult.Unknown();
            var dto = JsonSerializer.Deserialize<StripeStatusDto>(body, JsonOpts);
            return new StripeStatusResult
            {
                Known = true,
                StripeConfigured = dto?.StripeConfigured == true,
                WebhookConfigured = dto?.WebhookConfigured == true,
                PriceIdSet = dto?.PriceIdSet == true
            };
        }
        catch
        {
            return StripeStatusResult.Unknown();
        }
    }

    public async Task<PortalResult> CreatePortalAsync(string token, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/billing-portal");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            return PortalResult.Fail(ParseError(body));

        var dto = JsonSerializer.Deserialize<PortalDto>(body, JsonOpts);
        if (string.IsNullOrWhiteSpace(dto?.PortalUrl))
            return PortalResult.Fail("No portal URL returned");

        return PortalResult.Ok(dto.PortalUrl);
    }

    public async Task<ActivateResult> ActivateAsync(
        string token,
        string deviceHash,
        string deviceLabel,
        string? licenseKey = null,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/activate");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new
        {
            device_hash = deviceHash,
            device_label = deviceLabel,
            license_key = licenseKey
        });
        var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            return ActivateResult.Fail(ParseError(body), ParseErrorCode(body));

        var doc = JsonSerializer.Deserialize<ActivateDto>(body, JsonOpts);
        if (string.IsNullOrWhiteSpace(doc?.EntitlementToken))
            return ActivateResult.Fail("No entitlement returned");

        return ActivateResult.Ok(doc.EntitlementToken, doc.Entitlement);
    }

    private static async Task<LicenseAuthResult> ReadAuthAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            return LicenseAuthResult.Fail(ParseError(body));

        var dto = JsonSerializer.Deserialize<AuthDto>(body, JsonOpts);
        if (string.IsNullOrWhiteSpace(dto?.Token))
            return LicenseAuthResult.Fail("Invalid server response");

        return LicenseAuthResult.Ok(dto.Token, dto.Email, dto.LicenseKey);
    }

    private static string ParseError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var m) && m.GetString() is { Length: > 0 } msg)
                return msg;
            if (doc.RootElement.TryGetProperty("error", out var e) && e.GetString() is { Length: > 0 } err)
                return err;
        }
        catch { /* ignore */ }

        return string.IsNullOrWhiteSpace(body) ? "Request failed" : body;
    }

    private static string? ParseErrorCode(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.GetString() is { Length: > 0 } err)
                return err;
        }
        catch { /* ignore */ }

        return null;
    }

    private sealed class AuthDto
    {
        [JsonPropertyName("token")] public string? Token { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }
        [JsonPropertyName("license_key")] public string? LicenseKey { get; set; }
    }

    private sealed class ActivateDto
    {
        [JsonPropertyName("entitlement_token")] public string? EntitlementToken { get; set; }
        [JsonPropertyName("entitlement")] public Dictionary<string, JsonElement>? Entitlement { get; set; }
    }

    private sealed class CheckoutDto
    {
        [JsonPropertyName("checkout_url")] public string? CheckoutUrl { get; set; }
        [JsonPropertyName("session_id")] public string? SessionId { get; set; }
    }

    private sealed class PortalDto
    {
        [JsonPropertyName("portal_url")] public string? PortalUrl { get; set; }
    }

    private sealed class StripeStatusDto
    {
        [JsonPropertyName("stripe_configured")] public bool StripeConfigured { get; set; }
        [JsonPropertyName("webhook_configured")] public bool WebhookConfigured { get; set; }
        [JsonPropertyName("price_id_set")] public bool PriceIdSet { get; set; }
    }
}

public sealed class StripeStatusResult
{
    public bool Known { get; init; }
    public bool StripeConfigured { get; init; }
    public bool WebhookConfigured { get; init; }
    public bool PriceIdSet { get; init; }

    public static StripeStatusResult Unknown() => new() { Known = false };
}

public sealed class LicenseAuthResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? Token { get; init; }
    public string? Email { get; init; }
    public string? LicenseKey { get; init; }

    public static LicenseAuthResult Ok(string token, string? email, string? licenseKey) => new()
    {
        Success = true,
        Token = token,
        Email = email,
        LicenseKey = licenseKey
    };

    public static LicenseAuthResult Fail(string error) => new() { Success = false, Error = error };
}

public sealed class ActivateResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
    public string? EntitlementToken { get; init; }
    public Dictionary<string, JsonElement>? Entitlement { get; init; }

    public bool PaymentRequired =>
        string.Equals(ErrorCode, "payment_required", StringComparison.OrdinalIgnoreCase)
        || (Error?.Contains("Pay first", StringComparison.OrdinalIgnoreCase) == true);

    public static ActivateResult Ok(string token, Dictionary<string, JsonElement>? entitlement) => new()
    {
        Success = true,
        EntitlementToken = token,
        Entitlement = entitlement
    };

    public static ActivateResult Fail(string error, string? errorCode = null) =>
        new() { Success = false, Error = error, ErrorCode = errorCode };
}

public sealed class CheckoutResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
    public string? CheckoutUrl { get; init; }
    public string? SessionId { get; init; }

    public bool AlreadyPaid =>
        string.Equals(ErrorCode, "already_paid", StringComparison.OrdinalIgnoreCase)
        || (Error?.Contains("Already paid", StringComparison.OrdinalIgnoreCase) == true);

    public static CheckoutResult Ok(string url, string? sessionId) => new()
    {
        Success = true,
        CheckoutUrl = url,
        SessionId = sessionId
    };

    public static CheckoutResult Fail(string error, string? errorCode = null) =>
        new() { Success = false, Error = error, ErrorCode = errorCode };
}

public sealed class PortalResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? PortalUrl { get; init; }

    public static PortalResult Ok(string url) => new() { Success = true, PortalUrl = url };
    public static PortalResult Fail(string error) => new() { Success = false, Error = error };
}

public static class DeviceFingerprint
{
    public static string ComputeHash(string pepper)
    {
        var raw = $"{GetMachineGuid()}|{GetBoardUuid()}|{GetSystemVolumeSerial()}";
        var sha = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(
            string.IsNullOrWhiteSpace(pepper) ? "netie-dev-pepper" : pepper));
        return Convert.ToHexString(hmac.ComputeHash(sha)).ToLowerInvariant();
    }

    public static string DeviceLabel()
    {
        try { return Environment.MachineName; }
        catch { return "Windows PC"; }
    }

    private static string GetMachineGuid()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid")?.ToString() ?? "unknown-guid";
        }
        catch { return "unknown-guid"; }
    }

    private static string GetBoardUuid()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct");
            foreach (ManagementObject mo in searcher.Get())
            {
                var uuid = mo["UUID"]?.ToString();
                if (!string.IsNullOrWhiteSpace(uuid))
                    return uuid;
            }
        }
        catch { /* ignore */ }
        return "unknown-board";
    }

    private static string GetSystemVolumeSerial()
    {
        try
        {
            return LicenseOffline.StableVolumeToken(Environment.MachineName);
        }
        catch { /* ignore */ }
        return "00000000";
    }
}
