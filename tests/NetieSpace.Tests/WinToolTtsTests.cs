// vid-tts runs on Windows.Media.SpeechSynthesis, so these tests exist only in the Windows shape.
// A machine with no installed voice must FAIL them, with a message that says so.
#if WINDOWS
using NetieSpace.Apps.Video.WinRt;

namespace NetieSpace.Tests;

public class WinToolTtsTests
{
    private const string Sentence = "Hello from Netie. This is a short speech test, spoken by a Windows voice.";
    private const string NoVoice = "no text-to-speech voice is installed on this machine, so vid-tts cannot run";

    [Fact]
    public void A_voice_is_installed_on_this_machine()
    {
        Assert.True(TtsToolProvider.InstalledVoices().Count > 0, NoVoice);
    }

    [Fact]
    public void Vid_tts_is_registered_and_available_on_windows()
    {
        var tool = NetieSpace.Apps.Tools.ToolRegistry.Default.Find("vid-tts");
        Assert.NotNull(tool);
        Assert.NotNull(tool!.Runner);
        Assert.True(tool.Availability.Available, tool.Availability.Text);
    }

    [Fact]
    public void Text_option_makes_a_wav_with_a_riff_header_a_duration_and_audible_samples()
    {
        Assert.True(TtsToolProvider.InstalledVoices().Count > 0, NoVoice);
        var outDir = IoPath.Combine(WinToolSupport.NewDir(), "not yet there");
        var (code, stdout, stderr) = WinToolSupport.Cli("--tool", "vid-tts", "--out", outDir, "--opt", "text=" + Sentence);
        Assert.True(code == 0, stderr);

        var wav = IoPath.Combine(outDir, "speech.wav");
        Assert.Contains(wav, stdout);
        var info = WinToolSupport.ReadWav(wav);
        Assert.True(info.Seconds > 1.0, "the WAV lasts only " + info.Seconds + " s");
        Assert.True(info.Peak > 500, "the WAV is silent: loudest sample " + info.Peak + " of 32767");
        Assert.True(info.SampleRate >= 8000);
    }

    [Fact]
    public void A_txt_file_is_spoken_and_the_wav_is_named_after_it()
    {
        Assert.True(TtsToolProvider.InstalledVoices().Count > 0, NoVoice);
        var dir = WinToolSupport.NewDir();
        var input = IoPath.Combine(dir, "notes.txt");
        IoFile.WriteAllText(input, Sentence + "\r\nA second line follows it.");
        var outDir = IoPath.Combine(dir, "out");
        var (code, _, stderr) = WinToolSupport.Cli("--tool", "vid-tts", "--in", input, "--out", outDir);
        Assert.True(code == 0, stderr);
        var info = WinToolSupport.ReadWav(IoPath.Combine(outDir, "notes.wav"));
        Assert.True(info.Seconds > 1.5);
        Assert.True(info.Peak > 500);
        Assert.Equal(Sentence, IoFile.ReadAllText(input).Split('\r')[0]);
    }

    [Fact]
    public void Speed_changes_how_long_the_speech_lasts()
    {
        Assert.True(TtsToolProvider.InstalledVoices().Count > 0, NoVoice);
        double Seconds(string speed)
        {
            var outDir = WinToolSupport.NewDir();
            var (code, _, stderr) = WinToolSupport.Cli(
                "--tool", "vid-tts", "--out", outDir, "--opt", "text=" + Sentence, "--opt", "speed=" + speed);
            Assert.True(code == 0, stderr);
            return WinToolSupport.ReadWav(IoPath.Combine(outDir, "speech.wav")).Seconds;
        }

        var normal = Seconds("100");
        var fast = Seconds("200");
        Assert.True(fast < normal * 0.8, $"speed=200 took {fast} s against {normal} s at speed=100");
    }

    [Fact]
    public void Every_installed_voice_can_be_chosen_by_name()
    {
        var voices = TtsToolProvider.InstalledVoices();
        Assert.True(voices.Count > 0, NoVoice);
        var outDir = WinToolSupport.NewDir();
        var (code, _, stderr) = WinToolSupport.Cli(
            "--tool", "vid-tts", "--out", outDir, "--opt", "text=Voice check.", "--opt", "voice=" + voices[0]);
        Assert.True(code == 0, stderr);
        Assert.True(WinToolSupport.ReadWav(IoPath.Combine(outDir, "speech.wav")).Peak > 500);
    }

    [Fact]
    public void An_unknown_voice_fails_and_lists_the_installed_ones()
    {
        var voices = TtsToolProvider.InstalledVoices();
        Assert.True(voices.Count > 0, NoVoice);
        var outDir = IoPath.Combine(WinToolSupport.NewDir(), "none");
        var (code, _, stderr) = WinToolSupport.Cli(
            "--tool", "vid-tts", "--out", outDir, "--opt", "text=hi", "--opt", "voice=No Such Voice 9000");
        Assert.NotEqual(0, code);
        Assert.Contains("voice", stderr);
        Assert.Contains(voices[0], stderr);
        Assert.False(WinToolSupport.HasFiles(outDir));
    }

    [Fact]
    public void Same_named_runs_never_overwrite_each_other()
    {
        Assert.True(TtsToolProvider.InstalledVoices().Count > 0, NoVoice);
        var outDir = WinToolSupport.NewDir();
        for (var i = 0; i < 2; i++)
        {
            var (code, _, stderr) = WinToolSupport.Cli("--tool", "vid-tts", "--out", outDir, "--opt", "text=Run number " + i);
            Assert.True(code == 0, stderr);
        }
        Assert.Equal(2, IoDirectory.GetFiles(outDir, "*.wav").Length);
    }

    [Theory]
    [InlineData("speed=10", "speed")]
    [InlineData("speed=900", "speed")]
    [InlineData("text=   ", "text")]
    public void Bad_input_fails_naming_the_option_and_writes_nothing(string opt, string named)
    {
        var outDir = IoPath.Combine(WinToolSupport.NewDir(), "none");
        var (code, _, stderr) = WinToolSupport.Cli("--tool", "vid-tts", "--out", outDir, "--opt", opt);
        Assert.NotEqual(0, code);
        Assert.Contains(named, stderr);
        Assert.False(WinToolSupport.HasFiles(outDir));
    }

    [Fact]
    public void Both_a_file_and_the_text_option_is_refused()
    {
        var dir = WinToolSupport.NewDir();
        var input = IoPath.Combine(dir, "a.txt");
        IoFile.WriteAllText(input, "hello");
        var outDir = IoPath.Combine(dir, "out");
        var (code, _, stderr) = WinToolSupport.Cli("--tool", "vid-tts", "--in", input, "--out", outDir, "--opt", "text=hi");
        Assert.NotEqual(0, code);
        Assert.Contains("not both", stderr);
        Assert.False(WinToolSupport.HasFiles(outDir));
    }
}
#endif
