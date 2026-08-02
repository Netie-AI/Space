namespace NetieSpace.Helpers;

public static class EnvLoader
{
    public static string UserDataDirectory =>
        IoPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetieSpace");

    public static string UserEnvPath => IoPath.Combine(UserDataDirectory, "user.env");

    /// <summary>DPAPI-sealed replacement for user.env (Space#3).</summary>
    public static string UserSecretsPath => IoPath.Combine(UserDataDirectory, "user.secrets");

    //: Entropy binds the blob to this app, so another process running as the same user
    //: cannot unprotect it by merely calling ProtectedData.Unprotect.
    private static readonly byte[] _entropy =
        System.Text.Encoding.UTF8.GetBytes("NetieSpace.user.secrets.v1");

    private static void WriteSealed(string text)
    {
        var blob = System.Security.Cryptography.ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(text),
            _entropy,
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
        IoFile.WriteAllBytes(UserSecretsPath, blob);
    }

    private static string? ReadSealed()
    {
        if (!IoFile.Exists(UserSecretsPath))
            return null;
        try
        {
            var clear = System.Security.Cryptography.ProtectedData.Unprotect(
                IoFile.ReadAllBytes(UserSecretsPath),
                _entropy,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(clear);
        }
        catch (Exception ex)
        {
            // A blob written by a different user or machine cannot be unprotected.
            // Say so rather than silently falling back to no keys.
            AppLogger.Warn($"user.secrets unreadable ({ex.GetType().Name}) - re-enter keys in settings");
            return null;
        }
    }

    /// <summary>
    /// Move any pre-existing plaintext user.env into the sealed store and delete it.
    ///
    /// Keys used to be written to user.env in cleartext, readable by any process running
    /// as this user and by anything that syncs the profile folder. Migrating on load
    /// means an existing install stops leaking on next launch without the user acting.
    /// </summary>
    private static void MigratePlaintextIfPresent()
    {
        if (!IoFile.Exists(UserEnvPath))
            return;
        try
        {
            var text = IoFile.ReadAllText(UserEnvPath);
            if (!IoFile.Exists(UserSecretsPath))
                WriteSealed(text);
            IoFile.Delete(UserEnvPath);
            AppLogger.Info("Migrated user.env to DPAPI-sealed user.secrets and removed the plaintext file");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"user.env migration failed: {ex.Message}");
        }
    }

    public static void LoadFromProjectRoot()
    {
        var root = FindProjectRoot();
        LoadEnvFile(IoPath.Combine(root, ".env"));
        MigratePlaintextIfPresent();
        LoadSealedUserKeys();
    }

    /// <summary>Load the DPAPI-sealed user keys into the process environment.</summary>
    public static void LoadSealedUserKeys()
    {
        var text = ReadSealed();
        if (text is null)
            return;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;
            var eq = trimmed.IndexOf('=');
            if (eq <= 0)
                continue;
            Environment.SetEnvironmentVariable(
                trimmed[..eq].Trim(),
                trimmed[(eq + 1)..].Trim().Trim('"').Trim('\''));
        }
    }

    public static void LoadEnvFile(string envPath)
    {
        if (!IoFile.Exists(envPath))
            return;

        AppLogger.Info($"Loading .env from {envPath}");
        foreach (var line in IoFile.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var eq = trimmed.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = trimmed[..eq].Trim();
            var value = trimmed[(eq + 1)..].Trim().Trim('"').Trim('\'');
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    public static void SaveUserApiKeys(
        string? groqKey,
        string? openRouterKey,
        string? geminiKey = null,
        string? seaLionKey = null)
    {
        IoDirectory.CreateDirectory(UserDataDirectory);

        var lines = new List<string>
        {
            "# Netie Space user API keys (local only — not shipped in release zip)",
            $"GOOGLE_AISTUDIO_FREE={geminiKey?.Trim() ?? string.Empty}",
            $"GROQ_API_KEY={groqKey?.Trim() ?? string.Empty}",
            $"OPENROUTER_API_KEY={openRouterKey?.Trim() ?? string.Empty}",
            $"SEA_LION_API_KEY={seaLionKey?.Trim() ?? string.Empty}"
        };

        // Sealed with DPAPI (CurrentUser + app entropy) rather than written as plaintext.
        // PasswordVaultService in this same repo already did it this way; the pattern
        // existed and was simply not applied here.
        WriteSealed(string.Join("\n", lines));
        if (IoFile.Exists(UserEnvPath))
            IoFile.Delete(UserEnvPath);
        Environment.SetEnvironmentVariable("GOOGLE_AISTUDIO_FREE", geminiKey?.Trim() ?? string.Empty);
        Environment.SetEnvironmentVariable("GROQ_API_KEY", groqKey?.Trim() ?? string.Empty);
        Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", openRouterKey?.Trim() ?? string.Empty);
        Environment.SetEnvironmentVariable("SEA_LION_API_KEY", seaLionKey?.Trim() ?? string.Empty);
        AppLogger.Info($"Saved user API keys (DPAPI-sealed) to {UserSecretsPath}");
    }

    public static (string Gemini, string Groq, string OpenRouter, string SeaLion) ReadUserApiKeys()
    {
        string gemini =
            Environment.GetEnvironmentVariable("GOOGLE_AISTUDIO_FREE") ??
            Environment.GetEnvironmentVariable("GOOGLE_API_KEY") ??
            Environment.GetEnvironmentVariable("GEMINI_API_KEY") ??
            string.Empty;
        string groq = Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? string.Empty;
        string openRouter = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? string.Empty;
        string seaLion = Environment.GetEnvironmentVariable("SEA_LION_API_KEY") ?? string.Empty;

        if (!IoFile.Exists(UserEnvPath))
            return (gemini, groq, openRouter, seaLion);

        foreach (var line in IoFile.ReadAllLines(UserEnvPath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("GOOGLE_AISTUDIO_FREE=", StringComparison.OrdinalIgnoreCase))
                gemini = trimmed["GOOGLE_AISTUDIO_FREE=".Length..].Trim().Trim('"');
            else if (trimmed.StartsWith("GROQ_API_KEY=", StringComparison.OrdinalIgnoreCase))
                groq = trimmed["GROQ_API_KEY=".Length..].Trim().Trim('"');
            else if (trimmed.StartsWith("OPENROUTER_API_KEY=", StringComparison.OrdinalIgnoreCase))
                openRouter = trimmed["OPENROUTER_API_KEY=".Length..].Trim().Trim('"');
            else if (trimmed.StartsWith("SEA_LION_API_KEY=", StringComparison.OrdinalIgnoreCase))
                seaLion = trimmed["SEA_LION_API_KEY=".Length..].Trim().Trim('"');
        }

        return (gemini, groq, openRouter, seaLion);
    }

    /// <summary>Display form: first 4 + … + last 4. Never log the return as a secret.</summary>
    public static string MaskSecret(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            return "";
        var s = secret.Trim();
        if (s.Length <= 8)
            return new string('•', Math.Min(s.Length, 8));
        return $"{s[..4]}…{s[^4..]}";
    }

    public static string FindProjectRoot()
    {
        var baseDir = AppContext.BaseDirectory;

        if (IoDirectory.Exists(IoPath.Combine(baseDir, "tools")) ||
            IoFile.Exists(IoPath.Combine(baseDir, ".env")))
            return baseDir;

        var dir = new System.IO.DirectoryInfo(baseDir);
        while (dir is not null)
        {
            if (IoFile.Exists(IoPath.Combine(dir.FullName, ".env")) ||
                IoFile.Exists(IoPath.Combine(dir.FullName, "convert_all.py")) ||
                IoFile.Exists(IoPath.Combine(dir.FullName, "NetieSpace.sln")))
                return dir.FullName;

            dir = dir.Parent;
        }

        return baseDir;
    }
}
