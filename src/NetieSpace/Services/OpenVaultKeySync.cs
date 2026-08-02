using System.Net.Http;
using System.Text.Json;
using NetieSpace.Helpers;
using NetieSpace.Services.Licensing;

namespace NetieSpace.Services;

/// <summary>
/// Thin client of OpenVault (SoT for keys). Pulls provider secrets into Netie Space
/// user.env and optionally auto-signs into the Netie license using vaulted credentials.
/// </summary>
public static class OpenVaultKeySync
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string OpenVaultBaseUrl =>
        (Environment.GetEnvironmentVariable("OPENVAULT_URL")
         ?? "http://127.0.0.1:5000").TrimEnd('/');

    public sealed class SyncResult
    {
        public bool Reachable { get; init; }
        public bool Synced { get; init; }
        public int KeysPulled { get; init; }
        public string? Gemini { get; init; }
        public string? Groq { get; init; }
        public string? OpenRouter { get; init; }
        public string? SeaLion { get; init; }
        public string? AccountEmail { get; init; }
        public string Message { get; init; } = "";
        public IReadOnlyList<string> Providers { get; init; } = Array.Empty<string>();
    }

    public static async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            using var res = await Http.GetAsync($"{OpenVaultBaseUrl}/api/healthz", ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Pull Google / Groq / OpenRouter / SEA-LION secrets from OpenVault into Netie user.env.
    /// Soft-fails when OpenVault is offline.
    /// </summary>
    public static async Task<SyncResult> SyncApiKeysAsync(CancellationToken ct = default)
    {
        if (!await IsHealthyAsync(ct).ConfigureAwait(false))
        {
            return new SyncResult
            {
                Reachable = false,
                Message = $"OpenVault offline ({OpenVaultBaseUrl}) — using local keys only."
            };
        }

        try
        {
            using var listRes = await Http.GetAsync($"{OpenVaultBaseUrl}/api/keys", ct).ConfigureAwait(false);
            listRes.EnsureSuccessStatusCode();
            using var listDoc = JsonDocument.Parse(await listRes.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (!listDoc.RootElement.TryGetProperty("keys", out var keysEl) ||
                keysEl.ValueKind != JsonValueKind.Array)
            {
                return new SyncResult { Reachable = true, Message = "OpenVault returned no keys." };
            }

            string? gemini = null, groq = null, openRouter = null, seaLion = null;
            var pulled = new List<string>();

            foreach (var k in keysEl.EnumerateArray())
            {
                var enabled = !k.TryGetProperty("enabled", out var en) || en.GetBoolean();
                var life = k.TryGetProperty("lifecycle", out var lf) ? lf.GetString() : "active";
                if (!enabled || !string.Equals(life, "active", StringComparison.OrdinalIgnoreCase))
                    continue;

                var id = k.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var provider = k.TryGetProperty("provider", out var pEl) ? pEl.GetString() : null;
                var label = k.TryGetProperty("label", out var lEl) ? lEl.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(provider))
                    continue;

                var slot = MapProviderSlot(provider!, label);
                if (slot is null) continue;

                var secret = await RevealSecretAsync(id!, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(secret)) continue;

                switch (slot)
                {
                    case "gemini" when gemini is null:
                        gemini = secret; pulled.Add("gemini"); break;
                    case "groq" when groq is null:
                        groq = secret; pulled.Add("groq"); break;
                    case "openrouter" when openRouter is null:
                        openRouter = secret; pulled.Add("openrouter"); break;
                    case "sea-lion" when seaLion is null:
                        seaLion = secret; pulled.Add("sea-lion"); break;
                }
            }

            // Keep any local keys OpenVault did not provide
            var local = EnvLoader.ReadUserApiKeys();
            gemini ??= NullIfEmpty(local.Gemini);
            groq ??= NullIfEmpty(local.Groq);
            openRouter ??= NullIfEmpty(local.OpenRouter);
            seaLion ??= NullIfEmpty(local.SeaLion);

            if (pulled.Count > 0)
            {
                EnvLoader.SaveUserApiKeys(groq, openRouter, gemini, seaLion);
            }

            var email = await TryReadAccountEmailAsync(ct).ConfigureAwait(false);

            return new SyncResult
            {
                Reachable = true,
                Synced = pulled.Count > 0,
                KeysPulled = pulled.Count,
                Gemini = gemini,
                Groq = groq,
                OpenRouter = openRouter,
                SeaLion = seaLion,
                AccountEmail = email,
                Providers = pulled,
                Message = pulled.Count > 0
                    ? $"Connected OpenVault — synced {string.Join(", ", pulled)}."
                    : "OpenVault reachable — no matching AI keys to sync."
            };
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"openvault-sync: {ex.Message}");
            return new SyncResult
            {
                Reachable = true,
                Message = $"OpenVault sync failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Auto sign-in when OpenVault (or local password vault) holds Netie Space credentials.
    /// </summary>
    public static async Task<bool> TryAutoLoginAsync(
        string licenseApiBaseUrl,
        string devicePepper,
        CancellationToken ct = default)
    {
        if (LicenseStore.IsLicensedOffline())
            return true;

        var (email, password) = await ResolveLoginCredentialsAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || password.Length < 8)
            return false;

        try
        {
            var api = new LicenseApiClient(licenseApiBaseUrl);
            var auth = await api.LoginAsync(email, password).ConfigureAwait(false);
            if (!auth.Success || string.IsNullOrWhiteSpace(auth.Token))
            {
                AppLogger.Warn($"openvault auto-login auth: {auth.Error}");
                return false;
            }

            LicenseStore.SaveSession(auth.Token, auth.Email ?? email);
            var deviceHash = DeviceFingerprint.ComputeHash(devicePepper);
            var activate = await api.ActivateAsync(
                auth.Token,
                deviceHash,
                DeviceFingerprint.DeviceLabel(),
                auth.LicenseKey).ConfigureAwait(false);

            if (!activate.Success || string.IsNullOrWhiteSpace(activate.EntitlementToken))
            {
                AppLogger.Warn($"openvault auto-login activate: {activate.Error}");
                return false;
            }

            LicenseStore.SaveEntitlement(new LicenseEntitlement
            {
                Email = auth.Email ?? email,
                LicenseKey = auth.LicenseKey ?? "",
                LicenseId = activate.Entitlement is not null &&
                            activate.Entitlement.TryGetValue("license_id", out var lid)
                    ? lid.GetString() ?? ""
                    : "",
                DeviceHash = deviceHash,
                ActivatedAt = DateTime.UtcNow.ToString("O"),
                EntitlementToken = activate.EntitlementToken,
                PepperHint = devicePepper
            });

            AppLogger.Info($"openvault auto-login OK for {email}");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"openvault auto-login: {ex.Message}");
            return false;
        }
    }

    private static async Task<(string? Email, string? Password)> ResolveLoginCredentialsAsync(
        CancellationToken ct)
    {
        string? email = null;
        string? password = null;

        // 1) OpenVault custom secrets / labels
        if (await IsHealthyAsync(ct).ConfigureAwait(false))
        {
            try
            {
                using var listRes = await Http.GetAsync($"{OpenVaultBaseUrl}/api/keys", ct).ConfigureAwait(false);
                if (listRes.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await listRes.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                    if (doc.RootElement.TryGetProperty("keys", out var keys))
                    {
                        foreach (var k in keys.EnumerateArray())
                        {
                            var id = k.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                            var label = (k.TryGetProperty("label", out var lEl) ? lEl.GetString() : "") ?? "";
                            if (string.IsNullOrWhiteSpace(id)) continue;
                            var upper = label.ToUpperInvariant();

                            if (string.Equals(upper, "NETIE_SPACE_EMAIL", StringComparison.Ordinal) ||
                                string.Equals(upper, "NETIE_EMAIL", StringComparison.Ordinal) ||
                                upper.Contains("NETIE_SPACE_EMAIL", StringComparison.Ordinal))
                            {
                                email ??= await RevealSecretAsync(id, ct).ConfigureAwait(false);
                            }
                            else if (string.Equals(upper, "NETIE_SPACE_PASSWORD", StringComparison.Ordinal) ||
                                     string.Equals(upper, "NETIE_PASSWORD", StringComparison.Ordinal) ||
                                     upper.Contains("NETIE_SPACE_PASSWORD", StringComparison.Ordinal))
                            {
                                password ??= await RevealSecretAsync(id, ct).ConfigureAwait(false);
                            }
                        }
                    }
                }

                email ??= await TryReadAccountEmailAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"openvault login creds: {ex.Message}");
            }
        }

        // There used to be a second step here that scanned the user's local password
        // vault for ANY entry whose title, url or notes merely contained "netie" and
        // took its username and password. Removed 2026-08-03 (Space#3).
        //
        // Two reasons. It is a materially different security posture from failing
        // closed - the app was reading credentials the user stored for something else
        // and never consented to share with it - and it was documented nowhere. And it
        // is unnecessary: OpenVault owns credentials for this estate, so if the key is
        // not there, the answer is to put it there, not to go looking.
        //
        // Fail closed. A missing credential returns (null, null) and auto-login is
        // skipped, which the caller already handles.
        return (email?.Trim(), password);
    }

    private static async Task<string?> TryReadAccountEmailAsync(CancellationToken ct)
    {
        try
        {
            using var res = await Http.GetAsync($"{OpenVaultBaseUrl}/api/accounts", ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (!doc.RootElement.TryGetProperty("accounts", out var accounts)) return null;
            foreach (var a in accounts.EnumerateArray())
            {
                var email = a.TryGetProperty("email", out var e) ? e.GetString() : null;
                if (!string.IsNullOrWhiteSpace(email))
                    return email.Trim();
            }
        }
        catch { /* ignore */ }
        return null;
    }

    private static async Task<string?> RevealSecretAsync(string keyId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            $"{OpenVaultBaseUrl}/api/keys/{keyId}/secret");
        req.Headers.TryAddWithoutValidation("X-OpenVault-Reveal", "intentional");
        using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return doc.RootElement.TryGetProperty("secret", out var s) ? s.GetString() : null;
    }

    private static string? MapProviderSlot(string provider, string label)
    {
        var p = provider.Trim().ToLowerInvariant();
        var l = label.ToUpperInvariant();
        if (p is "google" || l.Contains("GOOGLE", StringComparison.Ordinal) ||
            l.Contains("GEMINI", StringComparison.Ordinal) ||
            l.Contains("AISTUDIO", StringComparison.Ordinal))
            return "gemini";
        if (p is "groq" || l.Contains("GROQ", StringComparison.Ordinal))
            return "groq";
        if (p is "openrouter" || l.Contains("OPENROUTER", StringComparison.Ordinal))
            return "openrouter";
        if (l.Contains("SEA_LION", StringComparison.Ordinal) ||
            l.Contains("SEA-LION", StringComparison.Ordinal) ||
            l.Contains("SEALION", StringComparison.Ordinal))
            return "sea-lion";
        return null;
    }

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
