using NetieSpace.Apps.Shell;

namespace NetieSpace.Tests;

/// <summary>Peek to app hand-off: the argv peek builds is the argv the app entry understands.</summary>
public class PeekBridgeTests
{
    [Theory]
    [InlineData("Pdf", "pdf", "Open in Netie PDF")]
    [InlineData("Image", "image", "Open in Netie Image")]
    [InlineData("Video", "video", "Open in Netie Video")]
    public void Each_previewable_kind_has_its_app(string kind, string key, string label)
    {
        var app = PeekBridge.ForKind(kind);
        Assert.NotNull(app);
        Assert.Equal(key, app!.Key);
        Assert.Equal(label, PeekBridge.Label(app));
    }

    [Theory]
    [InlineData("Text")]
    [InlineData("Markdown")]
    [InlineData("Office")]
    [InlineData("ShellInfo")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_kinds_have_no_app(string? kind)
    {
        Assert.Null(PeekBridge.ForKind(kind));
    }

    [Theory]
    [InlineData(@"C:\Users\me\My Files\Q3 report (final).pdf")]
    [InlineData(@"D:\Mom's clips\trip 100%.mp4")]
    [InlineData("C:\\a\\quote\"in\"name.png")]
    [InlineData(@"\\server\share\x.pdf")]
    public void The_arguments_peek_builds_are_read_back_by_the_app_entry(string path)
    {
        foreach (var app in AppIdentity.All)
        {
            var launch = AppLaunch.Parse(PeekBridge.Arguments(app, path));
            Assert.Equal(AppLaunchKind.App, launch.Kind);
            Assert.Equal(app.Key, launch.Identity!.Key);
            // The entry trims surrounding quotes only; everything inside the path survives.
            Assert.Equal(path.Trim().Trim('"'), launch.OpenPath);
        }
    }

    [Fact]
    public void Arguments_are_separate_elements_not_a_command_line()
    {
        var args = PeekBridge.Arguments(AppIdentity.Pdf, @"C:\a b\c.pdf");
        Assert.Equal(new[] { "--app", "pdf", "--open", @"C:\a b\c.pdf" }, args);
    }
}
