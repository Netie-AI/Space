// Shared helpers for the Windows-engine tool tests (WinTool*.cs). Compiles to nothing on the Linux gate.
#if WINDOWS
using DocumentFormat.OpenXml.Packaging;
using P = DocumentFormat.OpenXml.Presentation;

namespace NetieSpace.Tests;

internal static class WinToolSupport
{
    public static string Fixture(string name) => IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string NewDir()
    {
        var dir = IoPath.Combine(IoPath.GetTempPath(), "netie-wintool-" + Guid.NewGuid().ToString("N"));
        IoDirectory.CreateDirectory(dir);
        return dir;
    }

    public static (int Code, string Out, string Err) Cli(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = NetieSpace.Apps.Tools.ToolCli.RunWith(args, stdout, stderr, NetieSpace.Apps.Tools.ToolRegistry.Default);
        return (code, stdout.ToString(), stderr.ToString());
    }

    public static bool HasFiles(string dir) => IoDirectory.Exists(dir) && IoDirectory.GetFiles(dir).Length > 0;

    public sealed record SlideInfo(int PictureCount, byte[] ImageBytes, string ImageContentType);

    public sealed record DeckInfo(long Width, long Height, IReadOnlyList<SlideInfo> Slides);

    /// <summary>Opens a .pptx with Open XML and reads back what a person would get: size, slides, pictures.</summary>
    public static DeckInfo ReadDeck(string path)
    {
        using var doc = PresentationDocument.Open(path, false);
        var presentation = doc.PresentationPart!.Presentation;
        var size = presentation.SlideSize!;
        var slides = new List<SlideInfo>();
        foreach (var id in presentation.SlideIdList!.Elements<P.SlideId>())
        {
            var slidePart = (SlidePart)doc.PresentationPart.GetPartById(id.RelationshipId!);
            var pictures = slidePart.Slide.Descendants<P.Picture>().ToList();
            byte[] bytes = Array.Empty<byte>();
            var type = "";
            if (pictures.Count == 1)
            {
                var embed = pictures[0].BlipFill!.Blip!.Embed!.Value!;
                var imagePart = (ImagePart)slidePart.GetPartById(embed);
                type = imagePart.ContentType;
                using var s = imagePart.GetStream();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                bytes = ms.ToArray();
            }
            slides.Add(new SlideInfo(pictures.Count, bytes, type));
        }
        return new DeckInfo(size.Cx!.Value, size.Cy!.Value, slides);
    }

    public static bool IsJpeg(byte[] b) => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    public static bool IsPng(byte[] b) =>
        b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
        && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A;

    public static (int Width, int Height) PngSize(byte[] b) =>
        ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);

    public sealed record WavInfo(int FormatTag, int Channels, int SampleRate, int Bits, int DataBytes, double Seconds, int Peak);

    /// <summary>Reads the RIFF header and the samples of a 16-bit PCM WAV file; throws on anything else.</summary>
    public static WavInfo ReadWav(string path)
    {
        var b = IoFile.ReadAllBytes(path);
        Assert.True(b.Length > 44, "WAV is only " + b.Length + " bytes: " + path);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(b, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(b, 8, 4));

        int format = 0, channels = 0, rate = 0, bits = 0, byteRate = 0;
        int dataStart = -1, dataLen = 0;
        for (var i = 12; i + 8 <= b.Length;)
        {
            var id = System.Text.Encoding.ASCII.GetString(b, i, 4);
            var len = BitConverter.ToInt32(b, i + 4);
            if (id == "fmt ")
            {
                format = BitConverter.ToInt16(b, i + 8);
                channels = BitConverter.ToInt16(b, i + 10);
                rate = BitConverter.ToInt32(b, i + 12);
                byteRate = BitConverter.ToInt32(b, i + 16);
                bits = BitConverter.ToInt16(b, i + 22);
            }
            else if (id == "data")
            {
                dataStart = i + 8;
                dataLen = (int)Math.Min((long)len < 0 ? long.MaxValue : len, b.Length - dataStart);
                break;
            }
            i += 8 + len + (len & 1);
        }
        Assert.True(dataStart >= 0, "WAV has no data chunk: " + path);
        Assert.Equal(1, format);
        Assert.Equal(16, bits);
        Assert.True(byteRate > 0, "WAV byte rate is zero");

        var peak = 0;
        for (var i = dataStart; i + 1 < dataStart + dataLen; i += 2)
            peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(b, i)));
        return new WavInfo(format, channels, rate, bits, dataLen, dataLen / (double)byteRate, peak);
    }

    /// <summary>Open XML schema errors of a deck, as text; empty when it validates.</summary>
    public static string DeckErrors(string path)
    {
        using var doc = PresentationDocument.Open(path, false);
        var validator = new DocumentFormat.OpenXml.Validation.OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2019);
        return string.Join("; ", validator.Validate(doc).Select(e => e.Description + " @ " + e.Path?.XPath));
    }
}
#endif
