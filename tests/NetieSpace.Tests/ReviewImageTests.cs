using NetieSpace.Apps.Image;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SImage = SixLabors.ImageSharp.Image;
using static NetieSpace.Tests.ImageToolTestSupport;

namespace NetieSpace.Tests;

/// <summary>Review fixes for the image tools (S50 wave 4). Each test was written red first.</summary>
public class ReviewImageTests
{
    private static bool Reddish(Rgba32 p) => p.R - Math.Max(p.G, p.B) > 100;

    private static string PlainGif(string dir, string name, int frames)
    {
        var path = IoPath.Combine(dir, name);
        using var img = new Image<Rgba32>(160, 80, new Rgba32(255, 255, 255, 255));
        for (var i = 1; i < frames; i++)
        {
            using var f = new Image<Rgba32>(160, 80, new Rgba32(255, 255, (byte)(255 - i * 40), 255));
            img.Frames.AddFrame(f.Frames.RootFrame);
        }
        img.Save(path, new GifEncoder());
        return path;
    }

    [Fact]
    public async Task Text_watermark_marks_every_gif_frame()
    {
        var gif = PlainGif(ToolTestSupport.NewTempDir(), "anim.gif", 3);

        var r = await Run("img-watermark", new[] { gif }, ToolTestSupport.NewTempDir(),
            ("text", "MARK"), ("opacity", "100"), ("scale", "60"), ("color", "#FF0000"));

        Assert.True(r.Ok, r.Error);
        using var outImg = SImage.Load<Rgba32>(r.Outputs[0]);
        Assert.Equal(3, outImg.Frames.Count);
        for (var f = 0; f < outImg.Frames.Count; f++)
        {
            var red = 0;
            for (var y = 0; y < outImg.Height; y++)
                for (var x = 0; x < outImg.Width; x++)
                    if (Reddish(outImg.Frames[f][x, y])) red++;
            Assert.True(red > 40, $"frame {f} carries only {red} watermark pixels");
        }
    }

    /// <summary>
    /// A hand-written animated GIF: a w x h canvas and <paramref name="frames"/> frames of one pixel each. The header
    /// declares the size; the pixel data is a few bytes a frame, so the file is tiny but the decoded canvases are not.
    /// </summary>
    private static string TinyFramesGif(string dir, int w, int h, int frames)
    {
        var b = new List<byte>();
        b.AddRange("GIF89a"u8.ToArray());
        b.AddRange(new[] { (byte)(w & 255), (byte)(w >> 8), (byte)(h & 255), (byte)(h >> 8), (byte)0x80, (byte)0, (byte)0 });
        b.AddRange(new byte[] { 0, 0, 0, 255, 255, 255 });
        for (var i = 0; i < frames; i++)
        {
            b.AddRange(new byte[] { 0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00 });
            b.AddRange(new byte[] { 0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0 });
            b.AddRange(new byte[] { 0x02, 0x02, 0x44, 0x01, 0x00 });
        }
        b.Add(0x3B);
        var path = IoPath.Combine(dir, "frames.gif");
        IoFile.WriteAllBytes(path, b.ToArray());
        return path;
    }

    [Fact]
    public void The_hand_written_gif_is_a_real_animated_gif()
    {
        var path = TinyFramesGif(ToolTestSupport.NewTempDir(), 40, 30, 3);
        var info = SImage.Identify(path);
        Assert.Equal((40, 30), (info.Width, info.Height));
        using var img = SImage.Load<Rgba32>(path);
        Assert.Equal(3, img.Frames.Count);
    }

    [Fact]
    public async Task Many_frame_gif_is_refused_by_the_pixel_budget()
    {
        // 3000 x 3000 is 9 megapixels, well inside the limit for one frame; eight of them are 72 megapixels in memory.
        var gif = TinyFramesGif(ToolTestSupport.NewTempDir(), 3000, 3000, 8);
        var outDir = ToolTestSupport.NewTempDir();

        var r = await Run("img-rotate", new[] { gif }, outDir, ("direction", "right"));

        Assert.False(r.Ok);
        Assert.Contains("megapixels", r.Error);
        Assert.Contains("frames", r.Error);
        Assert.Empty(IoDirectory.GetFileSystemEntries(outDir));
    }

    [Fact]
    public async Task A_gif_with_few_frames_is_still_fine()
    {
        var gif = TinyFramesGif(ToolTestSupport.NewTempDir(), 3000, 3000, 2);
        var r = await Run("img-rotate", new[] { gif }, ToolTestSupport.NewTempDir(), ("direction", "right"));
        Assert.True(r.Ok, r.Error);
    }

    [Fact]
    public async Task Watermark_image_is_auto_oriented()
    {
        var dir = ToolTestSupport.NewTempDir();
        var basePng = SolidImage(dir, "base.png", 200, 200, new Rgba32(255, 255, 255, 255));
        // Stored 40 x 20: left half red, right half blue. EXIF orientation 6 means "turn 90 degrees clockwise to
        // display", so the viewer sees a 20 x 40 mark with the red half on top and the blue half underneath.
        var mark = IoPath.Combine(dir, "mark.jpg");
        using (var m = new Image<Rgba32>(40, 20))
        {
            for (var y = 0; y < 20; y++)
                for (var x = 0; x < 40; x++)
                    m[x, y] = x < 20 ? new Rgba32(255, 0, 0, 255) : new Rgba32(0, 0, 255, 255);
            m.Metadata.ExifProfile = new ExifProfile();
            m.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
            m.Save(mark, new JpegEncoder { Quality = 100 });
        }

        var r = await Run("img-watermark", new[] { basePng }, ToolTestSupport.NewTempDir(),
            ("image", mark), ("opacity", "100"), ("scale", "30"), ("position", "center"));

        Assert.True(r.Ok, r.Error);
        using var o = SImage.Load<Rgba32>(r.Outputs[0]);
        Assert.Equal((200, 200), (o.Width, o.Height));
        // Upright mark: 60 wide, 120 tall, centred. Top part red, bottom part blue.
        AssertClose(new Rgba32(255, 0, 0, 255), o[100, 60], 70, "upper half of the oriented mark");
        AssertClose(new Rgba32(0, 0, 255, 255), o[100, 140], 70, "lower half of the oriented mark");
    }
}
