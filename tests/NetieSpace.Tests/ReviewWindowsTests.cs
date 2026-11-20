using System.IO.Compression;
using System.Xml;
using NetieSpace.Apps.Pdf.WinRt;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;

namespace NetieSpace.Tests;

/// <summary>
/// Review fixes in the Windows-folder code (S50 wave 4). The slide-name test runs in both shapes (PptxDeck is
/// pure Open XML); the shortcut test needs COM and the Windows shape.
/// </summary>
public class ReviewWindowsTests
{
    private static byte[] TinyPng()
    {
        using var img = new Image<Rgba32>(4, 4, new Rgba32(10, 20, 30, 255));
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    // Built at run time: an attribute argument cannot carry a lone surrogate (metadata is UTF-8 and turns it into U+FFFD).
    public static IEnumerable<object[]> UnsafeNames() => new[]
    {
        new object[] { "lone" + '\uD800' + "high", "lonehigh" },
        new object[] { "lone" + '\uDC00' + "low", "lonelow" },
        new object[] { "bad" + '\uFFFE' + "end" + '\uFFFF', "badend" },
        new object[] { "tab\there" + '\u0001', "tabhere" },
        new object[] { "fine \uD83D\uDE00 pair", "fine \uD83D\uDE00 pair" },
    };

    [Theory]
    [MemberData(nameof(UnsafeNames))]
    public void Slide_picture_name_is_made_safe_for_xml(string name, string expectedLabel)
    {
        var dir = ToolTestSupport.NewTempDir();
        var pptx = IoPath.Combine(dir, "deck.pptx");
        using (var deck = new PptxDeck(pptx, PptxDeck.EmuPerInch * 10, PptxDeck.EmuPerInch * 6))
        {
            using var png = new MemoryStream(TinyPng());
            deck.AddPicture(png, true, 4, 4, name);
        }

        using var zip = ZipFile.OpenRead(pptx);
        var entry = zip.GetEntry("ppt/slides/slide1.xml");
        Assert.NotNull(entry);
        using var s = entry!.Open();
        // Strict reader: CheckCharacters is on by default, so any XML-invalid character in the part throws here.
        var doc = new XmlDocument();
        using (var reader = XmlReader.Create(s, new XmlReaderSettings { CheckCharacters = true }))
            doc.Load(reader);
        var cNvPr = doc.GetElementsByTagName("p:cNvPr").Cast<XmlElement>().Single(e => e.GetAttribute("id") == "2");
        Assert.Equal(expectedLabel, cNvPr.GetAttribute("name"));
        Assert.Equal(expectedLabel, cNvPr.GetAttribute("descr"));
    }

#if WINDOWS
    [Fact]
    public void Register_checks_every_icon_before_it_writes_the_first_shortcut()
    {
        var root = ToolTestSupport.NewTempDir();
        var exe = IoPath.Combine(root, "NetieSpace.exe");
        IoFile.WriteAllText(exe, "not a real exe; only its folder and existence matter here");
        var ids = NetieSpace.Apps.Shell.AppIdentity.All.ToList();
        // Every icon but the last one exists, so the old order wrote shortcuts for the first apps and then failed.
        foreach (var id in ids.Take(ids.Count - 1))
        {
            var icon = NetieSpace.Apps.Shell.AppRegistration.IconPath(root, id);
            IoDirectory.CreateDirectory(IoPath.GetDirectoryName(icon)!);
            IoFile.WriteAllBytes(icon, new byte[] { 0, 0, 1, 0 });
        }
        var programs = IoPath.Combine(root, "Programs");
        var logged = new List<string>();

        var ex = Assert.Throws<FileNotFoundException>(() => NetieSpace.Apps.Shell.AppShortcuts.Register(exe, programs, logged.Add));

        Assert.Contains(NetieSpace.Apps.Shell.AppRegistration.IconPath(root, ids[^1]), ex.Message);
        Assert.Empty(logged);
        Assert.True(!IoDirectory.Exists(programs) || !IoDirectory.GetFileSystemEntries(programs).Any(),
            "shortcuts were written before the missing icon was found");
    }
#endif
}
