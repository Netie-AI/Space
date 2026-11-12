namespace NetieSpace.Apps.Shell;

public sealed record FolderCheck(bool Ok, string Folder, string? Message);

/// <summary>Where results go and whether that folder can take them. No WPF.</summary>
public static class OutputFolder
{
    /// <summary>"Netie PDF" beside the first input; empty when there is no input yet.</summary>
    public static string Default(AppIdentity app, IReadOnlyList<string> files)
    {
        if (files.Count == 0 || string.IsNullOrWhiteSpace(files[0]))
            return "";
        var dir = IoPath.GetDirectoryName(IoPath.GetFullPath(files[0]));
        return string.IsNullOrEmpty(dir) ? "" : IoPath.Combine(dir, app.DisplayName);
    }

    /// <summary>The chosen folder if there is one, else the default.</summary>
    public static string Resolve(AppIdentity app, IReadOnlyList<string> files, string? chosen) =>
        !string.IsNullOrWhiteSpace(chosen) ? chosen! : Default(app, files);

    /// <summary>
    /// Creates the folder when missing and proves it can take a file by writing and deleting a
    /// probe. The message names the folder and says why it was refused.
    /// </summary>
    public static FolderCheck Prepare(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return new FolderCheck(false, "", "No output folder");
        string full;
        try
        {
            full = IoPath.GetFullPath(folder.Trim());
        }
        catch (Exception)
        {
            return new FolderCheck(false, folder, "Not a valid folder: " + folder);
        }
        if (IoFile.Exists(full))
            return new FolderCheck(false, full, "Not a folder: " + full);

        string? probe = null;
        try
        {
            IoDirectory.CreateDirectory(full);
            probe = IoPath.Combine(full, ".netie-write-" + Guid.NewGuid().ToString("N") + ".tmp");
            IoFile.WriteAllText(probe, "x");
            return new FolderCheck(true, full, null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.IO.IOException or NotSupportedException or System.Security.SecurityException)
        {
            return new FolderCheck(false, full, "Cannot write to " + full);
        }
        finally
        {
            try
            {
                if (probe is not null && IoFile.Exists(probe))
                    IoFile.Delete(probe);
            }
            catch (Exception)
            {
                // A probe that cannot be removed is a stray empty file, not a failure.
            }
        }
    }
}
