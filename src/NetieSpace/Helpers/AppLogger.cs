namespace NetieSpace.Helpers;

public static class AppLogger
{
    private static readonly string LogDir = IoPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NetieSpace", "logs");

    private static readonly string LogFile = IoPath.Combine(LogDir, "app.log");
    private static readonly object Lock = new();

    public static string LogPath => LogFile;

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);
    public static void Error(Exception ex, string context) =>
        Write("ERROR", $"{context}: {ex.Message}\n{ex.StackTrace}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
            {
                IoDirectory.CreateDirectory(LogDir);
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                IoFile.AppendAllText(LogFile, line + Environment.NewLine);
            }
        }
        catch
        {
            // ignore logging failures
        }
    }
}
