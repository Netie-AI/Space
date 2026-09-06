namespace NetieSpace.Helpers;

/// <summary>Lightweight UI prefs under %LocalAppData%\NetieSpace\prefs.env</summary>
public static class UserPrefs
{
    private static readonly object Gate = new();
    private static string PrefsPath => IoPath.Combine(EnvLoader.UserDataDirectory, "prefs.env");

    public static bool SummaryBarEnabled
    {
        get => ReadBool("SummaryBarEnabled", defaultValue: true);
        set => Write("SummaryBarEnabled", value ? "1" : "0");
    }

    /// <summary>Summary strip hidden by the collapse handle — purely visual, AI calls continue.</summary>
    public static bool SummaryCollapsed
    {
        get => ReadBool("SummaryCollapsed", defaultValue: false);
        set => Write("SummaryCollapsed", value ? "1" : "0");
    }

    /// <summary>Summary pulled into the left rail instead of the top strip.</summary>
    public static bool SummaryDocked
    {
        get => ReadBool("SummaryDocked", defaultValue: false);
        set => Write("SummaryDocked", value ? "1" : "0");
    }

    public static bool StartupHudSeen
    {
        get => ReadBool("StartupHudSeen", defaultValue: false);
        set => Write("StartupHudSeen", value ? "1" : "0");
    }

    /// <summary>Last PDF viewer zoom percent. 0 = Fit mode.</summary>
    public static int PdfZoomPercent
    {
        get
        {
            var raw = Read("PdfZoomPercent");
            if (string.IsNullOrWhiteSpace(raw) || !int.TryParse(raw, out var n))
                return 0;
            return Math.Clamp(n, 0, 400);
        }
        set => Write("PdfZoomPercent", Math.Clamp(value, 0, 400).ToString());
    }

    /// <summary>User opted in to proprietary local dual-brain embeddings (never uploaded).</summary>
    public static bool EmbeddingConsentAccepted
    {
        get => ReadBool("EmbeddingConsentAccepted", defaultValue: false);
        set => Write("EmbeddingConsentAccepted", value ? "1" : "0");
    }

    /// <summary>Consent dialog already shown once (accepted or declined).</summary>
    public static bool EmbeddingConsentPrompted
    {
        get => ReadBool("EmbeddingConsentPrompted", defaultValue: false);
        set => Write("EmbeddingConsentPrompted", value ? "1" : "0");
    }

    public static bool ReadBool(string key, bool defaultValue)
    {
        var raw = Read(key);
        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;
        return raw is "1" or "true" or "yes" or "on";
    }

    public static string? Read(string key)
    {
        lock (Gate)
        {
            if (!IoFile.Exists(PrefsPath))
                return null;
            foreach (var line in IoFile.ReadAllLines(PrefsPath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    continue;
                var eq = trimmed.IndexOf('=');
                if (eq <= 0)
                    continue;
                if (!trimmed[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    continue;
                return trimmed[(eq + 1)..].Trim().Trim('"').Trim('\'');
            }
        }

        return null;
    }

    public static void Write(string key, string value)
    {
        lock (Gate)
        {
            IoDirectory.CreateDirectory(EnvLoader.UserDataDirectory);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (IoFile.Exists(PrefsPath))
            {
                foreach (var line in IoFile.ReadAllLines(PrefsPath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                        continue;
                    var eq = trimmed.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    map[trimmed[..eq].Trim()] = trimmed[(eq + 1)..].Trim();
                }
            }

            map[key] = value;
            var lines = new List<string> { "# Netie Space UI prefs" };
            foreach (var kv in map.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                lines.Add($"{kv.Key}={kv.Value}");
            IoFile.WriteAllLines(PrefsPath, lines);
        }
    }
}
