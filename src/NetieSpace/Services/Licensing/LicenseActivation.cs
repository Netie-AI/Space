namespace NetieSpace.Services.Licensing;

/// <summary>
/// Bind the current session token to this laptop. Used by Login and Setup
/// after Stripe pay. No WPF types.
/// </summary>
public static class LicenseActivation
{
    public const string DefaultApiBase = "https://netie-space-license.workers.dev";
    public const string DefaultPepper = "netie-dev-pepper";
    public const string ApiEnvName = "NETIE_LICENSE_API";
    public const string PepperEnvName = "NETIE_DEVICE_PEPPER";

    private static string? _configuredApiBase;
    private static string? _configuredPepper;

    /// <summary>
    /// Appsettings fallback after env. Env always wins so a local Worker
    /// (`NETIE_LICENSE_API=http://127.0.0.1:8787`) cannot split from Login.
    /// </summary>
    public static void Configure(string? apiBase, string? pepper)
    {
        _configuredApiBase = string.IsNullOrWhiteSpace(apiBase) ? null : apiBase.Trim().TrimEnd('/');
        _configuredPepper = string.IsNullOrWhiteSpace(pepper) ? null : pepper.Trim();
    }

    public static string ResolveApiBase()
    {
        var raw = Environment.GetEnvironmentVariable(ApiEnvName);
        if (!string.IsNullOrWhiteSpace(raw))
            return raw.Trim().TrimEnd('/');
        return _configuredApiBase ?? DefaultApiBase;
    }

    public static string ResolvePepper()
    {
        var raw = Environment.GetEnvironmentVariable(PepperEnvName);
        if (!string.IsNullOrWhiteSpace(raw))
            return raw.Trim();
        return _configuredPepper ?? DefaultPepper;
    }

    public static async Task<(bool Ok, string Message)> ActivateCurrentSessionAsync(
        string? licenseKey = null,
        CancellationToken ct = default)
    {
        var session = LicenseStore.LoadSession();
        if (string.IsNullOrWhiteSpace(session?.Token))
            return (false, "Sign in first (tray -> Account), then Activate this laptop.");

        try
        {
            var api = new LicenseApiClient(ResolveApiBase());
            var pepper = ResolvePepper();
            var deviceHash = DeviceFingerprint.ComputeHash(pepper);
            var activate = await api.ActivateAsync(
                session.Token,
                deviceHash,
                DeviceFingerprint.DeviceLabel(),
                licenseKey,
                ct).ConfigureAwait(false);

            if (!activate.Success || string.IsNullOrWhiteSpace(activate.EntitlementToken))
                return (false, activate.Error ?? "Activate failed. Pay first, then retry.");

            var licenseId = "";
            if (activate.Entitlement is not null &&
                activate.Entitlement.TryGetValue("license_id", out var lid))
            {
                licenseId = lid.GetString() ?? "";
            }

            LicenseStore.SaveEntitlement(new LicenseEntitlement
            {
                Email = session.Email ?? "",
                LicenseKey = licenseKey ?? "",
                LicenseId = licenseId,
                DeviceHash = deviceHash,
                ActivatedAt = DateTime.UtcNow.ToString("O"),
                EntitlementToken = activate.EntitlementToken,
                PepperHint = pepper
            });

            return (true, "Activated — you can use Netie offline (except AI / re-login).");
        }
        catch (Exception ex)
        {
            return (false, $"Activate failed: {ex.Message}");
        }
    }
}
