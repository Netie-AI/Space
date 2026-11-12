// Proof that the TFM move to net8.0-windows10.0.19041.0 is real: WinRT is reachable
// from the test host. Windows only by construction: on Linux (scripts/ci-cursor.sh) the
// test project targets plain net8.0 and this file compiles to nothing, which that script
// states out loud. Windows CI (dotnet test NetieSpace.sln) runs it.
#if WINDOWS
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace NetieSpace.Tests;

public class WinRtSmokeTests
{
    [Fact]
    public async Task Windows_Data_Pdf_renders_page_one_of_sample_pdf()
    {
        var path = IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "sample.pdf");
        Assert.True(IoFile.Exists(path), $"fixture missing: {path}");

        var file = await StorageFile.GetFileFromPathAsync(path);
        var document = await PdfDocument.LoadFromFileAsync(file);
        Assert.True(document.PageCount >= 1, "sample.pdf has no pages");

        using var page = document.GetPage(0);
        using var stream = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(stream);
        Assert.True(stream.Size > 0, "page 1 rendered to zero bytes");
    }
}
#endif
