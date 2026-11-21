using NetieSpace.Apps.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SImage = SixLabors.ImageSharp.Image;

namespace NetieSpace.Tests;

internal static class ImageToolTestSupport
{
    public static readonly Rgba32 Red = new(255, 0, 0, 255);
    public static readonly Rgba32 Green = new(0, 255, 0, 255);
    public static readonly Rgba32 Blue = new(0, 0, 255, 255);
    public static readonly Rgba32 Yellow = new(255, 255, 0, 255);

    public static ToolRegistry Registry() => ToolRegistry.Load(probe: new EngineProbes());

    /// <summary>A 40x20 image: red top-left, green top-right, blue bottom-left, yellow bottom-right, white elsewhere.</summary>
    public static string CornerImage(string dir, string name, int w = 40, int h = 20, string format = "png")
    {
        IoDirectory.CreateDirectory(dir);
        var path = IoPath.Combine(dir, name);
        using var img = new Image<Rgba32>(w, h, new Rgba32(255, 255, 255, 255));
        img[0, 0] = Red;
        img[w - 1, 0] = Green;
        img[0, h - 1] = Blue;
        img[w - 1, h - 1] = Yellow;
        Save(img, path, format);
        return path;
    }

    /// <summary>Photograph-like content: smooth gradient plus deterministic noise, so compression has work to do.</summary>
    public static string NoisyImage(string dir, string name, string format, int w = 200, int h = 200)
    {
        IoDirectory.CreateDirectory(dir);
        var path = IoPath.Combine(dir, name);
        var rnd = new Random(42);
        using var img = new Image<Rgba32>(w, h);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                byte C(int basev) => (byte)Math.Clamp(basev + rnd.Next(-25, 26), 0, 255);
                img[x, y] = new Rgba32(C(x * 255 / w), C(y * 255 / h), C((x + y) * 255 / (w + h)), 255);
            }
        if (format == "jpg") img.Save(path, new JpegEncoder { Quality = 100 });
        else Save(img, path, format);
        return path;
    }

    public static string SolidImage(string dir, string name, int w, int h, Rgba32 color, string format = "png")
    {
        IoDirectory.CreateDirectory(dir);
        var path = IoPath.Combine(dir, name);
        using var img = new Image<Rgba32>(w, h, color);
        Save(img, path, format);
        return path;
    }

    private static void Save(Image<Rgba32> img, string path, string format)
    {
        switch (format)
        {
            case "png": img.Save(path, new PngEncoder()); break;
            case "jpg": img.Save(path, new JpegEncoder { Quality = 95 }); break;
            case "gif": img.Save(path, new GifEncoder()); break;
            case "bmp": img.Save(path, new BmpEncoder()); break;
            default: throw new ArgumentException(format);
        }
    }

    /// <summary>The density a file states, in dots per inch whatever unit its container stores.</summary>
    public static (double H, double V) Dpi(SixLabors.ImageSharp.IImageInfo info)
    {
        var m = info.Metadata;
        double K(double v) => m.ResolutionUnits switch
        {
            SixLabors.ImageSharp.Metadata.PixelResolutionUnit.PixelsPerMeter => v * 0.0254,
            SixLabors.ImageSharp.Metadata.PixelResolutionUnit.PixelsPerCentimeter => v * 2.54,
            _ => v,
        };
        return (K(m.HorizontalResolution), K(m.VerticalResolution));
    }

    public static Image<Rgba32> Open(string path) => SImage.Load<Rgba32>(path);

    public static byte[] Head(string path, int n = 8)
    {
        using var s = IoFile.OpenRead(path);
        var buf = new byte[n];
        var read = s.Read(buf, 0, n);
        return buf.Take(read).ToArray();
    }

    public static bool IsPng(string p) => Head(p).Take(8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
    public static bool IsJpg(string p) => Head(p, 3).SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF });
    public static bool IsGif(string p) => System.Text.Encoding.ASCII.GetString(Head(p, 3)) == "GIF";
    public static bool IsBmp(string p) => System.Text.Encoding.ASCII.GetString(Head(p, 2)) == "BM";

    public static void AssertClose(Rgba32 expected, Rgba32 actual, int tolerance, string what = "")
    {
        Assert.True(
            Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance &&
            Math.Abs(expected.B - actual.B) <= tolerance,
            $"{what} expected about {expected} but was {actual}");
    }

    public static async Task<ToolResult> Run(string id, IEnumerable<string> files, string outDir,
        params (string Key, string Value)[] options)
    {
        var reg = Registry();
        var tool = reg.Find(id) ?? throw new InvalidOperationException("no such tool " + id);
        var opts = options.ToDictionary(o => o.Key, o => o.Value);
        return await ToolRunner.RunAsync(tool, new ToolRequest(files.ToList(), outDir, opts), null, CancellationToken.None);
    }

    public static (int Exit, string Out, string Err) Cli(params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = ToolCli.RunWith(args, o, e, Registry());
        return (code, o.ToString(), e.ToString());
    }

    public static string Sha(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(IoFile.ReadAllBytes(path)));
}
