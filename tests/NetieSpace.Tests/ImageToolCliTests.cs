using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using static NetieSpace.Tests.ImageToolTestSupport;
using SImage = SixLabors.ImageSharp.Image;

namespace NetieSpace.Tests;

/// <summary>Each built image tool driven through the --tool path: registry, availability and runner together.</summary>
public class ImageToolCliTests
{
    private static string Dir() => ToolTestSupport.NewTempDir();

    private static string OneOutput((int Exit, string Out, string Err) r)
    {
        Assert.True(r.Exit == 0, r.Err);
        var lines = r.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
        var path = Assert.Single(lines);
        Assert.True(IoFile.Exists(path));
        return path;
    }

    [Fact]
    public void Cli_resize()
    {
        var src = CornerImage(Dir(), "a.png");
        var o = OneOutput(Cli("--tool", "img-resize", "--in", src, "--out", Dir(), "--opt", "width=10"));
        Assert.Equal((10, 5), (SImage.Identify(o).Width, SImage.Identify(o).Height));
    }

    [Fact]
    public void Cli_crop()
    {
        var src = CornerImage(Dir(), "a.png");
        var o = OneOutput(Cli("--tool", "img-crop", "--in", src, "--out", Dir(),
            "--opt", "x=0", "--opt", "y=0", "--opt", "width=10", "--opt", "height=10"));
        using var img = Open(o);
        Assert.Equal((10, 10), (img.Width, img.Height));
        Assert.Equal(Red, img[0, 0]);
    }

    [Fact]
    public void Cli_rotate()
    {
        var src = CornerImage(Dir(), "a.png");
        var o = OneOutput(Cli("--tool", "img-rotate", "--in", src, "--out", Dir(), "--opt", "direction=right"));
        using var img = Open(o);
        Assert.Equal((20, 40), (img.Width, img.Height));
        Assert.Equal(Red, img[19, 0]);
    }

    [Fact]
    public void Cli_compress()
    {
        var src = NoisyImage(Dir(), "a.jpg", "jpg");
        var o = OneOutput(Cli("--tool", "img-compress", "--in", src, "--out", Dir(), "--opt", "level=extreme"));
        Assert.True(IsJpg(o));
        Assert.True(new FileInfo(o).Length < new FileInfo(src).Length);
    }

    [Fact]
    public void Cli_to_jpg()
    {
        var src = CornerImage(Dir(), "a.png");
        var o = OneOutput(Cli("--tool", "img-to-jpg", "--in", src, "--out", Dir()));
        Assert.True(IsJpg(o));
        Assert.Equal((40, 20), (SImage.Identify(o).Width, SImage.Identify(o).Height));
    }

    [Fact]
    public void Cli_from_jpg()
    {
        var src = NoisyImage(Dir(), "a.jpg", "jpg", 30, 30);
        var o = OneOutput(Cli("--tool", "img-from-jpg", "--in", src, "--out", Dir(), "--opt", "format=bmp"));
        Assert.True(IsBmp(o));
    }

    [Fact]
    public void Cli_convert_any()
    {
        var src = CornerImage(Dir(), "a.png");
        var o = OneOutput(Cli("--tool", "img-convert-any", "--in", src, "--out", Dir(), "--opt", "to=gif"));
        Assert.True(IsGif(o));
    }

    [Fact]
    public void Cli_watermark()
    {
        var dir = Dir();
        var b = SolidImage(dir, "base.png", 100, 100, new Rgba32(255, 255, 255, 255));
        var m = SolidImage(dir, "mark.png", 10, 10, new Rgba32(0, 0, 0, 255));
        var o = OneOutput(Cli("--tool", "img-watermark", "--in", b, "--out", Dir(),
            "--opt", "image=" + m, "--opt", "position=center", "--opt", "opacity=100"));
        using var img = Open(o);
        AssertClose(new Rgba32(0, 0, 0, 255), img[50, 50], 5, "centre");
    }

    [Fact]
    public void Cli_dpi()
    {
        var src = CornerImage(Dir(), "a.png");
        var o = OneOutput(Cli("--tool", "img-dpi", "--in", src, "--out", Dir(), "--opt", "dpi=200"));
        Assert.InRange(Dpi(SImage.Identify(o)).H, 199.9, 200.1);
    }

    [Fact]
    public void Cli_reports_failure_with_exit_1_and_the_reason_on_stderr()
    {
        var src = SolidImage(Dir(), "tiny.png", 2, 2, Red);
        var r = Cli("--tool", "img-compress", "--in", src, "--out", Dir(), "--opt", "level=low");
        Assert.Equal(1, r.Exit);
        Assert.Contains("smaller", r.Err);
    }

    [Fact]
    public void Cli_unbuilt_image_tool_exits_2_with_not_built_or_engine_missing()
    {
        var src = CornerImage(Dir(), "a.png");
        var r = Cli("--tool", "img-remove-bg", "--in", src, "--out", Dir());
        Assert.Equal(2, r.Exit);
        Assert.Matches("unavailable", r.Err);
    }
}
