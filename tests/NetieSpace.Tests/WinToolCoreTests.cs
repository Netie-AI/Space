// The pure halves of the Windows-engine tools: rectangle blurring and the .pptx writer. They need no
// WinRT, but their sources live under Apps\*\Windows, so they are linked only in the Windows shape.
#if WINDOWS
using NetieSpace.Apps.Image.WinRt;
using NetieSpace.Apps.Pdf.WinRt;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SRect = SixLabors.ImageSharp.Rectangle;

namespace NetieSpace.Tests;

public class WinToolCoreTests
{
    /// <summary>Black and white 2-pixel checkerboard: any blur or pixelation must change it to mid grey.</summary>
    private static SixLabors.ImageSharp.Image<Rgba32> Checker(int w, int h)
    {
        var img = new SixLabors.ImageSharp.Image<Rgba32>(w, h);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                img[x, y] = ((x / 2) + (y / 2)) % 2 == 0 ? new Rgba32(0, 0, 0, 255) : new Rgba32(255, 255, 255, 255);
        return img;
    }

    private static Rgba32[,] Snapshot(SixLabors.ImageSharp.Image<Rgba32> img)
    {
        var s = new Rgba32[img.Width, img.Height];
        for (var y = 0; y < img.Height; y++)
            for (var x = 0; x < img.Width; x++)
                s[x, y] = img[x, y];
        return s;
    }

    [Fact]
    public void Regions_parse_clip_and_name_the_bad_rectangle()
    {
        var ok = FaceBlurCore.ParseRegions("10,10,50,40; -5,60,30,100", 100, 80);
        Assert.Equal(new SRect(10, 10, 50, 40), ok[0]);
        Assert.Equal(new SRect(0, 60, 25, 20), ok[1]);   // clipped to the image on the left and bottom

        foreach (var bad in new[] { "1,2,3", "a,b,c,d", "10,10,0,5", "10,10,-4,5", "200,10,5,5", ";", "" })
        {
            var ex = Assert.Throws<ArgumentException>(() => FaceBlurCore.ParseRegions(bad, 100, 80));
            Assert.Contains("regions", ex.Message);
        }
        var second = Assert.Throws<ArgumentException>(() => FaceBlurCore.ParseRegions("1,1,5,5;1,1,5", 100, 80));
        Assert.Contains("rectangle 2", second.Message);
    }

    [Fact]
    public void Blur_changes_the_inside_of_the_box_and_nothing_outside_it()
    {
        using var img = Checker(120, 90);
        var before = Snapshot(img);
        var box = new SRect(30, 20, 50, 40);

        FaceBlurCore.Apply(img, new[] { box }, "blur", 60);

        for (var y = 0; y < img.Height; y++)
            for (var x = 0; x < img.Width; x++)
            {
                var inside = box.Contains(x, y);
                if (!inside)
                    Assert.True(before[x, y] == img[x, y], $"pixel {x},{y} outside the box was changed");
            }
        // Well inside the box the black and white squares have become grey.
        for (var y = box.Top + 8; y < box.Bottom - 8; y++)
            for (var x = box.Left + 8; x < box.Right - 8; x++)
            {
                var v = img[x, y].R;
                Assert.InRange((int)v, 90, 165);
            }
    }

    [Fact]
    public void Pixelate_makes_flat_blocks_inside_the_box_and_nothing_outside_it()
    {
        using var img = Checker(120, 90);
        var before = Snapshot(img);
        var box = new SRect(20, 10, 80, 64);

        FaceBlurCore.Apply(img, new[] { box }, "pixelate", 50);

        var block = FaceBlurCore.BlockFor(box, 50);
        Assert.True(block >= 4);
        for (var y = 0; y < img.Height; y++)
            for (var x = 0; x < img.Width; x++)
                if (!box.Contains(x, y))
                    Assert.True(before[x, y] == img[x, y], $"pixel {x},{y} outside the box was changed");

        // Pixels in one block share one colour, which a 2-pixel checkerboard never does.
        var distinct = new HashSet<byte>();
        for (var y = box.Top; y < box.Top + block; y++)
            for (var x = box.Left; x < box.Left + block; x++)
                distinct.Add(img[x, y].R);
        Assert.Single(distinct);
    }

    [Fact]
    public void Apply_refuses_a_bad_mode_or_strength_by_option_name()
    {
        using var img = Checker(20, 20);
        var box = new[] { new SRect(0, 0, 10, 10) };
        Assert.Contains("mode", Assert.Throws<ArgumentException>(() => FaceBlurCore.Apply(img, box, "smear", 50)).Message);
        Assert.Contains("strength", Assert.Throws<ArgumentException>(() => FaceBlurCore.Apply(img, box, "blur", 0)).Message);
        Assert.Contains("strength", Assert.Throws<ArgumentException>(() => FaceBlurCore.Apply(img, box, "blur", 101)).Message);
    }

    [Fact]
    public void Detector_boxes_grow_by_a_quarter_and_stay_inside_the_image()
    {
        var padded = FaceBlurCore.Pad(new SRect(40, 40, 20, 20), 0.25, 100, 100);
        Assert.Equal(new SRect(35, 35, 30, 30), padded);
        var edge = FaceBlurCore.Pad(new SRect(0, 0, 20, 20), 0.25, 100, 100);
        Assert.Equal(new SRect(0, 0, 25, 25), edge);
    }

    // ---- the .pptx writer ----

    private static byte[] Encoded(int w, int h, bool png)
    {
        using var img = new SixLabors.ImageSharp.Image<Rgba32>(w, h, new Rgba32(30, 90, 200, 255));
        using var ms = new MemoryStream();
        if (png) img.Save(ms, new PngEncoder()); else img.Save(ms, new JpegEncoder());
        return ms.ToArray();
    }

    [Fact]
    public void Deck_has_one_slide_and_one_picture_per_page_and_validates()
    {
        var path = IoPath.Combine(WinToolSupport.NewDir(), "deck.pptx");
        var (cx, cy) = PptxDeck.SlideSizeFor(816, 1056);   // US Letter in 1/96 inch
        using (var deck = new PptxDeck(path, cx, cy))
        {
            deck.AddPicture(new MemoryStream(Encoded(204, 264, false)), false, 204, 264, "Page 1 & <one>");
            deck.AddPicture(new MemoryStream(Encoded(204, 264, true)), true, 204, 264, "Page 2");
            deck.AddPicture(new MemoryStream(Encoded(400, 100, false)), false, 400, 100, "Page 3");
            Assert.Equal(3, deck.SlideCount);
        }

        var info = WinToolSupport.ReadDeck(path);
        Assert.Equal(cx, info.Width);
        Assert.Equal(cy, info.Height);
        Assert.Equal(3, info.Slides.Count);
        Assert.All(info.Slides, s => Assert.Equal(1, s.PictureCount));
        Assert.True(WinToolSupport.IsJpeg(info.Slides[0].ImageBytes));
        Assert.True(WinToolSupport.IsPng(info.Slides[1].ImageBytes));
        Assert.True(WinToolSupport.IsJpeg(info.Slides[2].ImageBytes));
        Assert.Equal("", WinToolSupport.DeckErrors(path));
    }

    [Fact]
    public void Deck_refuses_bytes_that_are_not_the_declared_picture_format()
    {
        var path = IoPath.Combine(WinToolSupport.NewDir(), "deck.pptx");
        using var deck = new PptxDeck(path, 9144000, 6858000);
        Assert.Throws<InvalidDataException>(() =>
            deck.AddPicture(new MemoryStream(Encoded(10, 10, true)), false, 10, 10, "wrong"));
    }

    [Fact]
    public void Slide_size_keeps_the_page_ratio_and_stays_in_the_range_powerpoint_accepts()
    {
        var (w, h) = PptxDeck.SlideSizeFor(793.7, 1122.5);   // A4
        Assert.InRange(w / (double)h, 793.7 / 1122.5 - 0.001, 793.7 / 1122.5 + 0.001);

        var (bw, bh) = PptxDeck.SlideSizeFor(96 * 200, 96 * 100);   // 200 x 100 inches
        Assert.Equal(56 * PptxDeck.EmuPerInch, bw);
        Assert.Equal(28 * PptxDeck.EmuPerInch, bh);

        var (sw, sh) = PptxDeck.SlideSizeFor(48, 96);   // half an inch wide
        Assert.Equal(PptxDeck.EmuPerInch, sw);
        Assert.Equal(2 * PptxDeck.EmuPerInch, sh);

        Assert.Throws<ArgumentOutOfRangeException>(() => PptxDeck.SlideSizeFor(0, 100));
    }
}
#endif
