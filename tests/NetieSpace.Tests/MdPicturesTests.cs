using NetieSpace.Apps.Md;

namespace NetieSpace.Tests;

/// <summary>Netie MD: the local-only picture rule and the link rule. Pure; the folder is a real temp folder.</summary>
public class MdPicturesTests : IDisposable
{
    private readonly string _dir = IoPath.Combine(IoPath.GetTempPath(), "netie-md-" + Guid.NewGuid().ToString("N"));

    public MdPicturesTests()
    {
        IoDirectory.CreateDirectory(IoPath.Combine(_dir, "img", "deep"));
        foreach (var f in new[] { "a.png", "img/b.jpg", "img/deep/c b.png", "notes.txt", "img/x.PNG" })
            IoFile.WriteAllBytes(IoPath.Combine(_dir, f), new byte[] { 1 });
    }

    public void Dispose() => IoDirectory.Delete(_dir, true);

    private MdPicture Resolve(string src) => MdPictures.Resolve(src, _dir, IoFile.Exists);

    [Theory]
    [InlineData("a.png", "https://netie-md-doc.local/a.png")]
    [InlineData("./a.png", "https://netie-md-doc.local/a.png")]
    [InlineData("img/b.jpg", "https://netie-md-doc.local/img/b.jpg")]
    [InlineData("img\\b.jpg", "https://netie-md-doc.local/img/b.jpg")]
    [InlineData("img/deep/c%20b.png", "https://netie-md-doc.local/img/deep/c%20b.png")]
    [InlineData("img/../a.png", "https://netie-md-doc.local/a.png")]
    [InlineData("a.png?raw=true#frag", "https://netie-md-doc.local/a.png")]
    public void A_file_inside_the_folder_is_shown_through_the_picture_host(string src, string url)
    {
        var picture = Resolve(src);
        Assert.Equal(MdPictureKind.Local, picture.Kind);
        Assert.Equal(url, picture.Src);
    }

    [Theory]
    [InlineData("../a.png")]
    [InlineData("img/../../a.png")]
    [InlineData("%2e%2e/a.png")]
    [InlineData("img/%2E%2E/%2E%2E/a.png")]
    [InlineData("..%5Ca.png")]
    [InlineData("/etc/a.png")]
    [InlineData("C:\\Windows\\a.png")]
    [InlineData("C:/a.png")]
    [InlineData("file:///C:/a.png")]
    [InlineData("a.png:stream")]
    [InlineData("img/CON.png")]
    [InlineData("img/a.png.")]
    [InlineData("javascript:alert(1)")]
    [InlineData("notes.txt")]
    [InlineData("")]
    public void Anything_that_leaves_the_folder_or_is_not_a_picture_is_blocked(string src)
    {
        Assert.Equal(MdPictureKind.Blocked, Resolve(src).Kind);
    }

    [Theory]
    [InlineData("http://example.com/a.png")]
    [InlineData("HTTPS://example.com/a.png")]
    [InlineData("//example.com/a.png")]
    [InlineData("\\\\server\\share\\a.png")]
    public void Remote_pictures_are_never_resolved_to_a_source(string src)
    {
        var picture = Resolve(src);
        Assert.Equal(MdPictureKind.Remote, picture.Kind);
        Assert.Equal("", picture.Src);
    }

    [Fact]
    public void A_missing_file_is_reported_not_requested()
    {
        var picture = Resolve("nope.png");
        Assert.Equal(MdPictureKind.Missing, picture.Kind);
        Assert.Equal("", picture.Src);
    }

    [Fact]
    public void Inline_data_pictures_are_allowed_only_for_bitmap_types()
    {
        Assert.Equal(MdPictureKind.Inline, Resolve("data:image/png;base64,iVBORw0KGgo=").Kind);
        Assert.Equal(MdPictureKind.Blocked, Resolve("data:image/svg+xml;base64,PHN2Zz4=").Kind);
        Assert.Equal(MdPictureKind.Blocked, Resolve("data:text/html;base64,PHNjcmlwdD4=").Kind);
    }

    [Fact]
    public void Url_segments_are_escaped_once_so_the_host_reads_the_file_that_was_checked()
    {
        IoFile.WriteAllBytes(IoPath.Combine(_dir, "50%.png"), new byte[] { 1 });
        var picture = Resolve("50%25.png");
        Assert.Equal(MdPictureKind.Local, picture.Kind);
        Assert.Equal("https://netie-md-doc.local/50%25.png", picture.Src);
    }

    [Fact]
    public void Request_filter_allows_the_picture_host_and_data_only()
    {
        Assert.True(MdShell.IsAllowedRequest("https://netie-md-doc.local/a.png"));
        Assert.True(MdShell.IsAllowedRequest("data:text/html;charset=utf-8,x"));
        Assert.False(MdShell.IsAllowedRequest("https://example.com/a.png"));
        Assert.False(MdShell.IsAllowedRequest("http://netie-md-doc.local/a.png"));
        Assert.False(MdShell.IsAllowedRequest("https://netie-md-doc.local.evil.com/a.png"));
        Assert.False(MdShell.IsAllowedRequest("file:///C:/a.png"));
    }

    // ---- links ----

    [Theory]
    [InlineData("#intro", MdLinkKind.Anchor)]
    [InlineData("https://example.com/x", MdLinkKind.External)]
    [InlineData("mailto:a@b.co", MdLinkKind.External)]
    [InlineData("other.md", MdLinkKind.Markdown)]
    [InlineData("sub/other.markdown#part", MdLinkKind.Markdown)]
    [InlineData("../up.md", MdLinkKind.Markdown)]
    [InlineData("other.exe", MdLinkKind.Blocked)]
    [InlineData("a.png", MdLinkKind.Blocked)]
    [InlineData("javascript:alert(1)", MdLinkKind.Blocked)]
    [InlineData("vbscript:x", MdLinkKind.Blocked)]
    [InlineData("file:///C:/x.md", MdLinkKind.Blocked)]
    [InlineData("C:\\x.md", MdLinkKind.Blocked)]
    [InlineData("/abs/x.md", MdLinkKind.Blocked)]
    [InlineData("\\\\server\\share\\x.md", MdLinkKind.Blocked)]
    [InlineData("//server/x.md", MdLinkKind.Blocked)]
    [InlineData("data:text/html,x", MdLinkKind.Blocked)]
    [InlineData("", MdLinkKind.Blocked)]
    public void A_click_may_scroll_open_a_page_open_a_markdown_file_or_nothing(string href, MdLinkKind kind)
    {
        Assert.Equal(kind, MdLinks.Classify(href, _dir).Kind);
    }

    [Fact]
    public void A_markdown_link_resolves_beside_the_document_and_keeps_its_fragment()
    {
        var link = MdLinks.Classify("sub/other.md#part-2", _dir);
        Assert.Equal(IoPath.GetFullPath(IoPath.Combine(_dir, "sub", "other.md")), link.Target);
        Assert.Equal("part-2", link.Fragment);
        var up = MdLinks.Classify("../up.md", _dir);
        Assert.Equal(IoPath.GetFullPath(IoPath.Combine(_dir, "..", "up.md")), up.Target);
    }

    [Fact]
    public void A_link_inside_the_folder_is_not_a_link_out_of_it()
    {
        Assert.True(MdPictures.HasNoLinkBelow(_dir, IoPath.Combine(_dir, "a.png")));
        Assert.False(MdPictures.HasNoLinkBelow(_dir, IoPath.Combine(_dir, "..", "a.png")));
        Assert.False(MdPictures.HasNoLinkBelow(_dir, IoPath.Combine(IoPath.GetTempPath(), "a.png")));
        if (!OperatingSystem.IsWindows())
        {
            var outside = IoPath.Combine(IoPath.GetTempPath(), "netie-md-out-" + Guid.NewGuid().ToString("N") + ".png");
            IoFile.WriteAllBytes(outside, new byte[] { 1 });
            try
            {
                IoFile.CreateSymbolicLink(IoPath.Combine(_dir, "evil.png"), outside);
                Assert.False(MdPictures.HasNoLinkBelow(_dir, IoPath.Combine(_dir, "evil.png")));
            }
            finally { IoFile.Delete(outside); }
        }
    }
}
