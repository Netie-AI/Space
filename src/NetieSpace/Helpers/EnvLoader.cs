namespace NetieSpace.Helpers;

public static class EnvLoader
{
    public static string UserDataDirectory =>
        IoPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetieSpace");

    public static string UserEnvPath => IoPath.Combine(UserDataDirectory, "user.env");

    public static void LoadFromProjectRoot()
    {
        var root = FindProjectRoot();
        LoadEnvFile(IoPath.Combine(root, ".env"));
        LoadEnvFile(UserEnvPath);
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

        IoFile.WriteAllLines(UserEnvPath, lines);
        Environment.SetEnvironmentVariable("GOOGLE_AISTUDIO_FREE", geminiKey?.Trim() ?? string.Empty);
        Environment.SetEnvironmentVariable("GROQ_API_KEY", groqKey?.Trim() ?? string.Empty);
        Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", openRouterKey?.Trim() ?? string.Empty);
        Environment.SetEnvironmentVariable("SEA_LION_API_KEY", seaLionKey?.Trim() ?? string.Empty);
        AppLogger.Info($"Saved user API keys to {UserEnvPath}");
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
