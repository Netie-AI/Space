using NetieSpace.Apps.Image;
using SixLabors.ImageSharp.PixelFormats;
using static NetieSpace.Tests.ImageToolTestSupport;

namespace NetieSpace.Tests;

/// <summary>
/// Decoder hardening: a tiny file whose header claims a huge canvas must be refused before any pixel
/// memory is allocated, by every tool that opens images.
/// </summary>
[Collection(IsolatedMeasurementCollection.Name)]
public class ImageTool2HardeningTests
{
    private static string Dir() => ToolTestSupport.NewTempDir();

    private static uint Crc(byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            c ^= b;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320u : c >> 1;
        }
        return ~c;
    }

    private static byte[] Be32(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

    private static byte[] PngChunk(string type, byte[] data)
    {
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        var body = t.Concat(data).ToArray();
        return Be32((uint)data.Length).Concat(body).Concat(Be32(Crc(body))).ToArray();
    }

    /// <summary>Under 100 bytes, claims a 100000 x 100000 canvas (10 gigapixels).</summary>
    public static string CraftedPng(string dir, string name = "bomb.png")
    {
        var ihdr = Be32(100000).Concat(Be32(100000)).Concat(new byte[] { 8, 6, 0, 0, 0 }).ToArray();
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
            .Concat(PngChunk("IHDR", ihdr)).Concat(PngChunk("IEND", Array.Empty<byte>())).ToArray();
        var p = IoPath.Combine(dir, name);
        IoFile.WriteAllBytes(p, bytes);
        return p;
    }

    public static string CraftedBmp(string dir, string name = "bomb.bmp")
    {
        var b = new List<byte> { (byte)'B', (byte)'M' };
        b.AddRange(new byte[] { 0x36, 0, 0, 0, 0, 0, 0, 0, 0x36, 0, 0, 0 });
        b.AddRange(BitConverter.GetBytes(40));
        b.AddRange(BitConverter.GetBytes(100000));
        b.AddRange(BitConverter.GetBytes(100000));
        b.AddRange(new byte[] { 1, 0, 24, 0 });
        b.AddRange(new byte[24]);
        var p = IoPath.Combine(dir, name);
        IoFile.WriteAllBytes(p, b.ToArray());
        return p;
    }

    public static string CraftedGif(string dir, string name = "bomb.gif")
    {
        var b = new List<byte>();
        b.AddRange(System.Text.Encoding.ASCII.GetBytes("GIF89a"));
        b.AddRange(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0 });
        b.Add((byte)';');
        var p = IoPath.Combine(dir, name);
        IoFile.WriteAllBytes(p, b.ToArray());
        return p;
    }

    public static string CraftedJpg(string dir, string name = "bomb.jpg")
    {
        var b = new List<byte> { 0xFF, 0xD8 };
        // DQT with one flat 8-bit table, then SOF0 claiming 65535 x 65535, one component.
        b.AddRange(new byte[] { 0xFF, 0xDB, 0x00, 0x43, 0x00 });
        b.AddRange(Enumerable.Repeat((byte)1, 64));
        b.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0xFF, 0xFF, 0xFF, 0xFF, 0x01, 0x01, 0x11, 0x00 });
        b.AddRange(new byte[] { 0xFF, 0xD9 });
        var p = IoPath.Combine(dir, name);
        IoFile.WriteAllBytes(p, b.ToArray());
        return p;
    }

    public static IEnumerable<object[]> Bombs()
    {
        foreach (var kind in new[] { "png", "bmp", "gif", "jpg" })
            foreach (var tool in new[]
                     {
                         "img-resize", "img-crop", "img-rotate", "img-compress", "img-to-jpg", "img-from-jpg",
                         "img-convert-any", "img-watermark", "img-dpi", "img-meme",
                     })
            {
                if (tool == "img-from-jpg" && kind != "jpg") continue; // that tool only accepts .jpg inputs
                yield return new object[] { tool, kind };
            }
    }

    private static string Craft(string kind, string dir) => kind switch
    {
        "png" => CraftedPng(dir),
        "bmp" => CraftedBmp(dir),
        "gif" => CraftedGif(dir),
        _ => CraftedJpg(dir),
    };

    [Theory]
    [MemberData(nameof(Bombs))]
    public async Task Every_tool_refuses_a_header_that_claims_a_huge_canvas_and_writes_nothing(string tool, string kind)
    {
        var d = Dir();
        var bomb = Craft(kind, d);
        Assert.True(new FileInfo(bomb).Length < 200, "the crafted file is tiny");
        var outDir = IoPath.Combine(d, "o");
        var opts = tool switch
        {
            "img-resize" => new[] { ("width", "10") },
            "img-crop" => new[] { ("width", "10"), ("height", "10") },
            "img-watermark" => new[] { ("text", "x") },
            "img-meme" => new[] { ("top", "x") },
            "img-convert-any" => new[] { ("to", kind == "png" ? "bmp" : "png") },
            _ => Array.Empty<(string, string)>(),
        };
        var r = await Run(tool, new[] { bomb }, outDir, opts);
        Assert.False(r.Ok, tool + " accepted a bomb");
        Assert.Contains("megapixels", r.Error);
        Assert.False(IoDirectory.Exists(outDir) && IoDirectory.GetFiles(outDir).Length > 0, "no output may be left");
    }

    [Theory]
    [InlineData("png")]
    [InlineData("bmp")]
    [InlineData("gif")]
    [InlineData("jpg")]
    public void The_refusal_happens_before_pixel_memory_is_allocated(string kind)
    {
        var bomb = Craft(kind, Dir());
        var before = GC.GetTotalAllocatedBytes(true);
        var ex = Assert.Throws<InvalidOperationException>(() => ImageIo.Load(bomb));
        var after = GC.GetTotalAllocatedBytes(true);
        Assert.Contains("megapixels", ex.Message);
        // A 10 gigapixel decode would need tens of gigabytes; the refusal costs next to nothing.
        Assert.True(after - before < 20_000_000, "allocated " + (after - before) + " bytes before refusing");
    }

    [Fact]
    public void The_budget_is_one_constant_with_an_exact_edge()
    {
        Assert.Equal(64_000_000L, ImageIo.MaxPixels);
        ImageIo.CheckPixelBudget(8000, 8000, "x");
        ImageIo.CheckPixelBudget(64_000_000, 1, "x");
        Assert.Throws<InvalidOperationException>(() => ImageIo.CheckPixelBudget(8001, 8000, "x"));
        Assert.Throws<InvalidOperationException>(() => ImageIo.CheckPixelBudget(0, 5, "x"));
        // Multiplied in 64 bits: two 40000 sides do not wrap around to a small number.
        Assert.Throws<InvalidOperationException>(() => ImageIo.CheckPixelBudget(100000, 100000, "x"));
    }

    [Fact]
    public async Task Resize_refuses_a_target_above_the_budget_but_not_a_normal_one()
    {
        var d = Dir();
        var src = SolidImage(d, "s.png", 10, 10, new Rgba32(1, 2, 3, 255));
        var big = await Run("img-resize", new[] { src }, IoPath.Combine(d, "o"),
            ("width", "30000"), ("height", "30000"), ("keep_ratio", "false"));
        Assert.False(big.Ok);
        Assert.Contains("megapixels", big.Error);
        var ok = await Run("img-resize", new[] { src }, IoPath.Combine(d, "o2"), ("width", "100"));
        Assert.True(ok.Ok, ok.Error);
    }

    [Fact]
    public void Cli_refuses_a_bomb_with_a_message_and_a_failing_exit_code()
    {
        var d = Dir();
        var r = Cli("--tool", "img-rotate", "--in", CraftedPng(d), "--out", IoPath.Combine(d, "o"));
        Assert.NotEqual(0, r.Exit);
        Assert.Contains("megapixels", r.Err);
    }
}
