using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetieSpace.Apps.Tools;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Streams;

namespace NetieSpace.Apps.Video.WinRt;

/// <summary>
/// vid-tts: text from a .txt file or from option 'text' is spoken by an installed Windows voice into a
/// WAV file. Compiled only for the Windows TFM.
/// </summary>
public sealed class TtsToolProvider : IToolProvider
{
    public const int MaxChars = 100_000;

    public ToolApp App => ToolApp.Video;

    public IEnumerable<(string ToolId, ToolRunnerDelegate Run)> Runners()
    {
        yield return ("vid-tts", RunAsync);
    }

    /// <summary>Display names of the installed voices, default voice first.</summary>
    public static IReadOnlyList<string> InstalledVoices()
    {
        var all = SpeechSynthesizer.AllVoices;
        var def = SpeechSynthesizer.DefaultVoice;
        return all.OrderBy(v => v.Id == def?.Id ? 0 : 1).ThenBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(v => v.DisplayName).ToList();
    }

    /// <summary>Finds a voice by display name, id, or language tag; null/empty means the default voice.</summary>
    public static VoiceInformation PickVoice(string? wanted)
    {
        var all = SpeechSynthesizer.AllVoices;
        if (all.Count == 0)
            throw new InvalidOperationException("no text-to-speech voice is installed on this PC");
        if (string.IsNullOrWhiteSpace(wanted))
            return SpeechSynthesizer.DefaultVoice ?? all[0];

        var w = wanted.Trim();
        var hit = all.FirstOrDefault(v => string.Equals(v.DisplayName, w, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(v => string.Equals(v.Id, w, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(v => v.DisplayName.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)
            ?? all.FirstOrDefault(v => string.Equals(v.Language, w, StringComparison.OrdinalIgnoreCase));
        if (hit is null)
            throw new ArgumentException(
                "option 'voice': no installed voice matches '" + w + "'; installed voices: "
                + string.Join(", ", InstalledVoices()));
        return hit;
    }

    private static async Task<IReadOnlyList<string>> RunAsync(
        ToolRequest request, IProgress<ToolProgress>? progress, CancellationToken ct)
    {
        var speed = 100;
        if (request.Options.TryGetValue("speed", out var speedText) && !int.TryParse(speedText, out speed))
            throw new ArgumentException("option 'speed': '" + speedText + "' is not a number");
        if (speed < 50 || speed > 300)
            throw new ArgumentException("option 'speed': " + speed + " is outside 50..300");

        request.Options.TryGetValue("text", out var optionText);
        var hasText = !string.IsNullOrWhiteSpace(optionText);
        string text, stem;
        if (request.Files.Count > 0)
        {
            if (hasText)
                throw new ArgumentException("give either an input file or option 'text', not both");
            var input = IoPath.GetFullPath(request.Files[0]);
            text = IoFile.ReadAllText(input);
            stem = IoPath.GetFileNameWithoutExtension(input);
        }
        else
        {
            if (!hasText)
                throw new ArgumentException("give a .txt input file or option 'text'");
            text = optionText!;
            stem = "speech";
        }

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("there is no text to speak");
        if (text.Length > MaxChars)
            throw new InvalidOperationException("the text is " + text.Length + " characters; the limit is " + MaxChars);

        var voice = PickVoice(request.Options.TryGetValue("voice", out var voiceText) ? voiceText : null);

        var taken = new List<string>();
        var outPath = FreePath(request.OutDir, stem, ".wav");
        try
        {
            using var synth = new SpeechSynthesizer();
            synth.Voice = voice;
            synth.Options.SpeakingRate = speed / 100.0;

            progress?.Report(new ToolProgress(10, "speaking with " + voice.DisplayName));
            using var stream = await synth.SynthesizeTextToStreamAsync(text).AsTask(ct);
            if (stream.Size < 64)
                throw new InvalidOperationException("the voice '" + voice.DisplayName + "' produced no audio");

            using var source = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream);
            using (var target = IoFile.Create(outPath))
                await source.CopyToAsync(target, ct);
            taken.Add(outPath);
        }
        catch
        {
            try { IoFile.Delete(outPath); } catch { /* ignore */ }
            throw;
        }
        return taken;
    }

    private static string FreePath(string dir, string stem, string ext)
    {
        IoDirectory.CreateDirectory(dir);
        for (var n = 1; ; n++)
        {
            var path = IoPath.Combine(dir, n == 1 ? stem + ext : $"{stem}-{n}{ext}");
            if (!IoFile.Exists(path)) return path;
        }
    }
}
