namespace NetieSpace.Apps.Tools;

/// <summary>
/// The licence gate sits on the engine, not the tool: changing one engine flips every tool on it (R-0004).
/// AGPL engines are refused whether or not they are present on the machine.
/// </summary>
public static class LicenseGate
{
    public static bool IsCleared(EngineRef engine) => engine.License != EngineLicense.AGPL;
}

/// <summary>
/// Availability = verdict gate, then licence gate, then engine probe, then runner present.
/// Each leg yields its own reason from the closed set so the UI never has to say just "no".
/// </summary>
public static class ToolAvailabilityRules
{
    public const string TextNotBuilt = "not built in this version";
    public const string TextNeedsDisclose = "needs Space#3 disclose";
    public const string TextCloudOnly = "cloud only";
    public const string PrefixEngineMissing = "engine missing: ";
    public const string PrefixLicence = "licence not cleared for shipping: ";

    public static ToolAvailability Compute(ToolEntry entry, bool hasRunner, IEngineProbe probe)
    {
        if (entry.Verdict == ToolVerdict.CloudOnly)
            return new(false, UnavailableReason.CloudOnly, TextCloudOnly);
        if (entry.Verdict == ToolVerdict.AiEgress)
            return new(false, UnavailableReason.NeedsDisclose, TextNeedsDisclose);
        if (!LicenseGate.IsCleared(entry.Engine))
            return new(false, UnavailableReason.LicenceNotCleared, PrefixLicence + entry.Engine.Name);

        var (ok, detail) = probe.Probe(entry.Engine);
        if (!ok)
            return new(false, UnavailableReason.EngineMissing,
                PrefixEngineMissing + (string.IsNullOrWhiteSpace(detail) ? entry.Engine.Name : detail));

        if (!hasRunner)
            return new(false, UnavailableReason.NotBuilt, TextNotBuilt);

        return ToolAvailability.Yes;
    }
}
