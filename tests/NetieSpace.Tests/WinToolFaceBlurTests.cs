// img-blur-face uses Windows.Media.FaceAnalysis for detection, so these tests exist only in the Windows shape.
// The manual-rectangle path needs no detector; the pixel work itself is covered in WinToolCoreTests.
#if WINDOWS
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace NetieSpace.Tests;

public class WinToolFaceBlurTests
{
    private static string Checker(string dir, string name, int w, int h, bool jpeg = false)
    {
        using var img = new SixLabors.ImageSharp.Image<Rgba32>(w, h);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                img[x, y] = ((x / 2) + (y / 2)) % 2 == 0 ? new Rgba32(0, 0, 0, 255) : new Rgba32(255, 255, 255, 255);
        var path = IoPath.Combine(dir, name);
        if (jpeg) img.Save(path, new JpegEncoder { Quality = 100 }); else img.Save(path, new PngEncoder());
        return path;
    }

    [Fact]
    public void Blur_face_is_registered_and_available_on_windows()
    {
        var tool = NetieSpace.Apps.Tools.ToolRegistry.Default.Find("img-blur-face");
        Assert.NotNull(tool);
        Assert.NotNull(tool!.Runner);
        Assert.True(tool.Availability.Available, tool.Availability.Text);
    }

    [Fact]
    public void The_face_detector_is_supported_on_this_machine()
    {
        Assert.True(Windows.Media.FaceAnalysis.FaceDetector.IsSupported, "face detection is not supported on this PC");
    }

    [Fact]
    public void Manual_rectangle_changes_the_inside_and_leaves_the_outside_and_the_input_alone()
    {
        var dir = WinToolSupport.NewDir();
        var input = Checker(dir, "group photo.png", 120, 90);
        var inputBytes = IoFile.ReadAllBytes(input);
        var outDir = IoPath.Combine(dir, "out");

        var (code, stdout, stderr) = WinToolSupport.Cli(
            "--tool", "img-blur-face", "--in", input, "--out", outDir, "--opt", "regions=30,20,50,40");
        Assert.True(code == 0, stderr);

        var output = IoPath.Combine(outDir, "group photo-blurred.png");
        Assert.Contains(output, stdout);
        Assert.Equal(inputBytes, IoFile.ReadAllBytes(input));

        using var before = SixLabors.ImageSharp.Image.Load<Rgba32>(input);
        using var after = SixLabors.ImageSharp.Image.Load<Rgba32>(output);
        Assert.Equal((before.Width, before.Height), (after.Width, after.Height));
        var box = new SixLabors.ImageSharp.Rectangle(30, 20, 50, 40);
        for (var y = 0; y < before.Height; y++)
            for (var x = 0; x < before.Width; x++)
                if (!box.Contains(x, y))
                    Assert.True(before[x, y] == after[x, y], $"pixel {x},{y} outside the box was changed");
        for (var y = box.Top + 8; y < box.Bottom - 8; y++)
            for (var x = box.Left + 8; x < box.Right - 8; x++)
                Assert.InRange((int)after[x, y].R, 90, 165);
    }

    [Fact]
    public void Pixelate_mode_and_jpeg_input_are_saved_as_jpeg_with_the_box_changed()
    {
        var dir = WinToolSupport.NewDir();
        var input = Checker(dir, "shot.jpg", 160, 120, jpeg: true);
        var outDir = IoPath.Combine(dir, "out");

        var (code, _, stderr) = WinToolSupport.Cli(
            "--tool", "img-blur-face", "--in", input, "--out", outDir,
            "--opt", "regions=40,30,80,60;0,0,20,20", "--opt", "mode=pixelate", "--opt", "strength=40");
        Assert.True(code == 0, stderr);

        var output = IoPath.Combine(outDir, "shot-blurred.jpg");
        Assert.True(WinToolSupport.IsJpeg(IoFile.ReadAllBytes(output)));
        using var before = SixLabors.ImageSharp.Image.Load<Rgba32>(input);
        using var after = SixLabors.ImageSharp.Image.Load<Rgba32>(output);
        // A fine checkerboard has high local contrast; after pixelation a block is flat.
        int Contrast(SixLabors.ImageSharp.Image<Rgba32> i) =>
            Enumerable.Range(0, 7).Sum(k => Math.Abs(i[60 + k, 50].R - i[61 + k, 50].R));
        Assert.True(Contrast(before) > 200, "the fixture lost its pattern");
        Assert.True(Contrast(after) < Contrast(before) / 2, "the region was not pixelated");
    }

    [Theory]
    [InlineData("regions=1,2,3")]
    [InlineData("regions=500,500,10,10")]
    [InlineData("regions=1,1,10,10;x")]
    [InlineData("mode=smear")]
    [InlineData("strength=0")]
    [InlineData("strength=101")]
    public void A_bad_option_fails_naming_it_and_writes_nothing(string opt)
    {
        var dir = WinToolSupport.NewDir();
        var input = Checker(dir, "a.png", 60, 60);
        var outDir = IoPath.Combine(dir, "out");
        var (code, _, stderr) = WinToolSupport.Cli("--tool", "img-blur-face", "--in", input, "--out", outDir, "--opt", opt);
        Assert.NotEqual(0, code);
        Assert.Contains(opt.Split('=')[0], stderr);
        Assert.False(WinToolSupport.HasFiles(outDir));
    }

    [Fact]
    public void An_image_with_no_face_fails_with_no_faces_found_and_writes_no_copy()
    {
        Assert.True(Windows.Media.FaceAnalysis.FaceDetector.IsSupported, "face detection is not supported on this PC");
        var dir = WinToolSupport.NewDir();
        // A smooth gradient: nothing in it for a face detector to find.
        var path = IoPath.Combine(dir, "gradient.png");
        using (var img = new SixLabors.ImageSharp.Image<Rgba32>(256, 256))
        {
            for (var y = 0; y < 256; y++)
                for (var x = 0; x < 256; x++)
                    img[x, y] = new Rgba32((byte)x, (byte)y, 128, 255);
            img.Save(path, new PngEncoder());
        }
        var outDir = IoPath.Combine(dir, "out");
        var (code, _, stderr) = WinToolSupport.Cli("--tool", "img-blur-face", "--in", path, "--out", outDir);
        Assert.Equal(1, code);
        Assert.Contains("no faces found", stderr);
        Assert.False(WinToolSupport.HasFiles(outDir));
    }

    [Fact]
    public async Task Detection_on_the_gradient_returns_an_empty_list_not_an_error()
    {
        Assert.True(Windows.Media.FaceAnalysis.FaceDetector.IsSupported, "face detection is not supported on this PC");
        using var img = new SixLabors.ImageSharp.Image<Rgba32>(300, 200, new Rgba32(200, 200, 200, 255));
        var faces = await NetieSpace.Apps.Image.WinRt.FaceBlurToolProvider.DetectAsync(img, CancellationToken.None);
        Assert.Empty(faces);
    }
}
#endif
