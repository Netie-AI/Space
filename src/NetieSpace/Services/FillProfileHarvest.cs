using System.Text.Json;

namespace NetieSpace.Services;

/// <summary>
/// Save-time Autofill harvest, held until the user confirms it.
///
/// The viewer posts a "profileHarvest" object on every PDF flush. That object used
/// to be merged straight into <see cref="FillProfileStore"/>, so six PII fields
/// (name, email, phone, address, DOB, nationality) were captured on every save with
/// no prompt and no disclosure. Nothing in here touches disk: Propose only says what
/// *would* change, and the caller writes only after an explicit confirm.
/// </summary>
public static class FillProfileHarvest
{
    /// <summary>One bounded field the harvest would overwrite. Shown to the user verbatim.</summary>
    public sealed record Change(string Label, string OldValue, string NewValue);

    /// <summary>Merged profile plus the exact list of what changed. Saving it is the caller's decision.</summary>
    public sealed record Proposal(FillProfileStore.Profile Merged, IReadOnlyList<Change> Changes)
    {
        /// <summary>Disclosure body for the confirm dialog - every field named, no silent extras.</summary>
        public string Describe() =>
            string.Join("\n", Changes.Select(c => $"- {c.Label}: {c.NewValue}"));
    }

    /// <summary>
    /// What this harvest would write over <paramref name="current"/>.
    /// Returns null when nothing bounded is new - no proposal means no prompt and no write.
    /// Keys outside the bounded set are ignored, so the viewer cannot grow fill-profile.bin.
    /// </summary>
    public static Proposal? Propose(JsonElement harvest, FillProfileStore.Profile current)
    {
        if (harvest.ValueKind != JsonValueKind.Object)
            return null;

        var changes = new List<Change>();
        string Take(string key, string label, string existing)
        {
            var value = Pick(harvest, key, existing);
            if (!string.Equals(value, existing, StringComparison.Ordinal))
                changes.Add(new Change(label, existing, value));
            return value;
        }

        var merged = new FillProfileStore.Profile(
            Take("name", "Name", current.FullName),
            Take("email", "Email", current.Email),
            Take("phone", "Phone", current.Phone),
            Take("address", "Address", current.Address),
            Take("dob", "Date of birth", current.DateOfBirth),
            Take("nationality", "Nationality", current.Nationality),
            Take("dateFormat", "Date format", current.DateFormat),
            DateTime.Now.ToString("dd/MM/yyyy"));

        return changes.Count == 0 ? null : new Proposal(merged, changes);
    }

    /// <summary>Keep the existing value when the harvest field is absent, blank or not a string.</summary>
    private static string Pick(JsonElement harvest, string key, string existing)
    {
        if (!harvest.TryGetProperty(key, out var p))
            return existing;
        var v = p.ValueKind == JsonValueKind.String ? p.GetString()?.Trim() ?? "" : "";
        return string.IsNullOrWhiteSpace(v) ? existing : v;
    }
}
