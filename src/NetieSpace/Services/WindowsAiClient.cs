using System.Text.Json;

namespace NetieSpace.Services;

/// <summary>
/// Fail-closed Windows AI / WinML probe. No WPF types. No WinAppSDK package.
/// Missing runtime -> false. Never throws to the UI -- rembg / Copy stay.
/// Live enhance / GPU is still human.
/// </summary>
public sealed class WindowsAiClient
{
    public const string SidecarSuffix = ".winai-input.json";

    public static readonly string[] ProbeFiles =
    [
        @"C:\Windows\System32\Windows.AI.MachineLearning.dll",
        @"C:\Windows\System32\WinML.dll"
    ];

    public static string OfflineMessage() =>
        "Windows AI is not available on this PC. rembg / Copy still work.";

    /// <summary>
    /// False on non-Windows, missing WinML type, missing probe files, or any error.
    /// </summary>
    public static bool IsAvailable()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return false;
            if (ProbeWinMlType())
                return true;
            foreach (var path in ProbeFiles)
            {
                if (IoFile.Exists(path))
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool ProbeWinMlType()
    {
        try
        {
            return Type.GetType(
                       "Windows.AI.MachineLearning.LearningModel, Windows.AI.MachineLearning, ContentType=WindowsRuntime")
                   is not null
                   || Type.GetType("Windows.AI.MachineLearning.LearningModel") is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Write {file}.winai-input.json next to the image (sidecar fallback).</summary>
    public static string WriteInputSidecar(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new ArgumentException("Image path is required.", nameof(imagePath));

        var sidecar = imagePath + SidecarSuffix;
        var payload = new Dictionary<string, string>
        {
            ["image_path"] = imagePath,
            ["note"] = "Windows AI / WinML sidecar. rembg / Copy still work if Windows AI is down."
        };
        IoFile.WriteAllText(
            sidecar,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        return sidecar;
    }
}
