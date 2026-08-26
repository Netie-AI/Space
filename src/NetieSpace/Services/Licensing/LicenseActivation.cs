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

    public static string ResolveApiBase()
    {
        var raw = Environment.GetEnvironmentVariable(ApiEnvName);
        return string.IsNullOrWhiteSpace(raw) ? DefaultApiBase : raw.Trim().TrimEnd('/');
    }

    public static string ResolvePepper()
    {
        var raw = Environment.GetEnvironmentVariable(PepperEnvName);
        return string.IsNullOrWhiteSpace(raw) ? DefaultPepper : raw.Trim();
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
