namespace NetieSpace.Helpers;

/// <summary>Repo / publish root. WPF-free so Cursor CI can link FfmpegService.</summary>
public static class ProjectRoot
{
    public static string Find()
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
