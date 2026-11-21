using System.IO;

namespace NetieSpace.Apps.Md;

/// <summary>The one remembered choice of Netie MD: Auto, Light or Dark. A plain text file; a bad or missing file means Auto.</summary>
public static class MdSettings
{
    public static string DefaultPath() => IoPath.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "NetieSpace", "apps", "md", "theme.txt");

    public static MdColorMode LoadColorMode(string path)
    {
        try
        {
            return IoFile.Exists(path) && MdColorModes.TryParse(IoFile.ReadAllText(path), out var mode) ? mode : MdColorMode.System;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return MdColorMode.System;
        }
    }

    /// <returns>False when the choice could not be written (it still applies until the window closes).</returns>
    public static bool SaveColorMode(string path, MdColorMode mode)
    {
        try
        {
            IoDirectory.CreateDirectory(IoPath.GetDirectoryName(path)!);
            IoFile.WriteAllText(path, MdColorModes.Label(mode).ToLowerInvariant());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
