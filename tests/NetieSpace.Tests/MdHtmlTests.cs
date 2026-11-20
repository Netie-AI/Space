using NetieSpace.Apps.Md;

namespace NetieSpace.Tests;

/// <summary>Netie MD: outline, picture and link rewriting, and the page shell with its policy.</summary>
public class MdHtmlTests : IDisposable
{
    private readonly string _dir = IoPath.Combine(IoPath.GetTempPath(), "netie-mdh-" + Guid.NewGuid().ToString("N"));

    public MdHtmlTests()
    {
        IoDirectory.CreateDirectory(_dir);
        IoFile.WriteAllBytes(IoPath.Combine(_dir, "a.png"), new byte[] { 1 });
    }

    public void Dispose() => IoDirectory.Delete(_dir, true);

    private MdBody Process(string html) => MdHtml.Process(html, _dir, IoFile.Exists);

    [Fact]
    public void Headings_get_ids_and_the_outline_nests_by_level()
    {
        var body = Process("<h1>Title</h1><h2>Setup</h2><h3>Install</h3><h2>Usage</h2><h1>Appendix</h1><h3>Skip</h3>");
        Assert.Equal(
            new[] { ("title", 1, 0), ("setup", 2, 1), ("install", 3, 2), ("usage", 2, 1), ("appendix", 1, 0), ("skip", 3, 1) },
            body.Outline.Select(h => (h.Id, h.Level, h.Depth)));
        Assert.Contains("<h2 id=\"setup\">Setup</h2>", body.Html);
        Assert.Contains("<h1 id=\"appendix\">Appendix</h1>", body.Html);
    }

    [Fact]
    public void Duplicate_titles_get_distinct_ids_that_match_the_html()
    {
        var body = Process("<h2>Notes</h2><h2>Notes</h2><h2>Notes</h2><h2>Notes 1</h2>");
        var ids = body.Outline.Select(h => h.Id).ToList();
        Assert.Equal(new[] { "notes", "notes-1", "notes-2", "notes-1-1" }, ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        foreach (var id in ids)
            Assert.Contains($"id=\"{id}\"", body.Html);
    }

    [Fact]
    public void Thousands_of_equal_headings_stay_fast_and_distinct()
    {
        var html = string.Concat(Enumerable.Repeat("<h1>a</h1>", 30000));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var body = Process(html);
        Assert.True(sw.ElapsedMilliseconds < 3000, sw.ElapsedMilliseconds + " ms");
        Assert.Equal(30000, body.Outline.Select(h => h.Id).Distinct().Count());
    }

    [Fact]
    public void An_engine_that_never_returns_becomes_a_notice()
    {
        var md = IoPath.Combine(_dir, "slow.md");
        IoFile.WriteAllText(md, "x");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var doc = MdDocument.Load(md, new Slow(), "NN");
        Assert.Equal("Document too complex to show", doc.Error);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20));
    }

    private sealed class Slow : IMdEngine
    {
        public string ToHtml(string markdown) { Thread.Sleep(TimeSpan.FromSeconds(30)); return ""; }
    }

    [Fact]
    public void Titles_are_plain_text_and_ids_are_safe_characters_only()
    {
        var body = Process("<h2>A <code>b</code> &amp; <em>c</em>: d</h2><h2>&lt;script&gt;alert(1)&lt;/script&gt;</h2><h2>!!!</h2><h2><img src=\"a.png\" alt=\"\"></h2>");
        Assert.Equal("A b & c: d", body.Outline[0].Title);
        Assert.Equal("a-b-c-d", body.Outline[0].Id);
        Assert.Equal("<script>alert(1)</script>", body.Outline[1].Title);
        Assert.All(body.Outline, h => Assert.Matches("^[a-z0-9-]+$", h.Id));
        Assert.Equal("section", body.Outline[2].Id);
        Assert.Equal("Untitled heading", body.Outline[3].Title);
    }

    [Fact]
    public void Attributes_an_engine_may_have_put_on_a_heading_are_dropped()
    {
        var body = Process("<h2 onclick=\"x()\" id=\"evil\">Hi</h2>");
        Assert.DoesNotContain("onclick", body.Html);
        Assert.DoesNotContain("evil", body.Html);
        Assert.Contains("<h2 id=\"hi\">Hi</h2>", body.Html);
    }

    [Fact]
    public void A_local_picture_is_served_by_the_picture_host_and_a_remote_one_is_not_requested()
    {
        var body = Process(
            "<p><img src=\"a.png\" alt=\"Local one\" /> <img src=\"https://evil.example/t.png?x=1\" alt=\"Remote one\" /> " +
            "<img src=\"gone.png\" alt=\"Gone\" /> <img src=\"../a.png\" alt=\"Out\" /></p>");
        Assert.Contains("<img src=\"https://netie-md-doc.local/a.png\" alt=\"Local one\">", body.Html);
        Assert.DoesNotContain("evil.example", body.Html);
        Assert.Contains("Remote one", body.Html);
        Assert.Contains("remote picture", body.Html);
        Assert.Contains("picture not found", body.Html);
        Assert.Contains("picture blocked", body.Html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(body.Html, "<img "));
    }

    [Fact]
    public void Alt_text_cannot_break_out_of_its_placeholder()
    {
        var body = Process("<img src=\"https://x.test/a.png\" alt=\"&quot;&gt;&lt;script&gt;alert(1)&lt;/script&gt;\" />");
        Assert.DoesNotContain("<script", body.Html);
        Assert.Contains("&lt;script&gt;", body.Html);
    }

    [Fact]
    public void Links_keep_only_what_a_click_may_do()
    {
        var body = Process(
            "<a href=\"#setup\">in page</a> <a href=\"https://example.com/x?a=1&amp;b=2\" onclick=\"evil()\">web</a> " +
            "<a href=\"other.md#p\">md</a> <a href=\"javascript:alert(1)\">js</a> <a href=\"file:///C:/Windows/x.exe\">file</a> " +
            "<a href=\"run.exe\">exe</a>");
        Assert.Contains("<a href=\"#setup\">", body.Html);
        Assert.Contains("<a href=\"https://example.com/x?a=1&amp;b=2\" data-md=\"ext\">", body.Html);
        Assert.Contains("<a href=\"other.md#p\" data-md=\"md\">", body.Html);
        Assert.DoesNotContain("javascript:", body.Html);
        Assert.DoesNotContain("file:", body.Html);
        Assert.DoesNotContain("run.exe", body.Html);
        Assert.DoesNotContain("onclick", body.Html);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(body.Html, "md-blocked-link").Count);
    }

    [Fact]
    public void Tags_named_like_pictures_or_links_inside_code_are_left_alone()
    {
        var body = Process("<pre><code>&lt;img src=&quot;https://x.test/a.png&quot;&gt; &lt;a href=&quot;javascript:1&quot;&gt;</code></pre>");
        Assert.Contains("&lt;img src=&quot;https://x.test/a.png&quot;&gt;", body.Html);
    }

    // ---- the page shell ----

    [Fact]
    public void The_title_is_escaped_and_never_becomes_markup()
    {
        var page = MdShell.Page("<script>alert(1)</script>.md", "<p>x</p>", "NONCE");
        Assert.DoesNotContain("<script>alert(1)</script>", page);
        Assert.Contains("<title>&lt;script&gt;alert(1)&lt;/script&gt;.md</title>", page);
        var quoted = MdShell.Page("a\"><img src=x onerror=alert(1)>.md", "", "N");
        Assert.DoesNotContain("<img src=x", quoted);
    }

    [Fact]
    public void The_policy_allows_no_network_and_one_nonce_script()
    {
        var page = MdShell.Page("t", "<p>x</p>", "ABC123");
        Assert.Contains("Content-Security-Policy", page);
        Assert.Contains("default-src 'none'", page);
        Assert.Contains("img-src https://netie-md-doc.local/ data:", page);
        Assert.Contains("script-src 'nonce-ABC123'", page);
        Assert.DoesNotContain("unsafe-eval", page);
        Assert.DoesNotContain("script-src 'unsafe-inline'", page);
        Assert.DoesNotContain("connect-src", page);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(page, "<script"));
        Assert.Contains("<script nonce=\"ABC123\">", page);
        Assert.Contains("border:1px solid #bdbdbd", page); // table borders
    }

    [Fact]
    public void Each_page_gets_its_own_nonce()
    {
        Assert.NotEqual(MdShell.NewNonce(), MdShell.NewNonce());
        Assert.Matches("^[0-9A-F]{32}$", MdShell.NewNonce());
    }

    [Fact]
    public void A_notice_escapes_its_message()
    {
        Assert.DoesNotContain("<b>", MdShell.Notice("t", "<b>boom</b>", "N"));
    }

    // ---- a whole document through a stand-in engine ----

    private sealed class Echo : IMdEngine
    {
        public string ToHtml(string markdown) => markdown;
    }

    [Fact]
    public void A_document_loads_into_a_page_with_an_outline_and_reports_a_missing_file()
    {
        var md = IoPath.Combine(_dir, "doc.md");
        IoFile.WriteAllText(md, "<h1>One</h1><p><img src=\"a.png\" alt=\"p\"></p><h2>Two</h2>");
        var doc = MdDocument.Load(md, new Echo(), "NN");
        Assert.Null(doc.Error);
        Assert.Equal("doc.md", doc.Title);
        Assert.Equal(new[] { "one", "two" }, doc.Outline.Select(h => h.Id));
        Assert.Contains("https://netie-md-doc.local/a.png", doc.Page);

        var missing = MdDocument.Load(IoPath.Combine(_dir, "no.md"), new Echo(), "NN");
        Assert.Equal("File not found", missing.Error);
        Assert.Contains("File not found", missing.Page);
        Assert.Empty(missing.Outline);
    }

    [Fact]
    public void A_picture_that_is_a_link_to_a_file_outside_the_folder_is_not_shown()
    {
        var sub = IoPath.Combine(_dir, "sub");
        IoDirectory.CreateDirectory(sub);
        var md = IoPath.Combine(sub, "doc.md");
        IoFile.WriteAllText(md, "<p><img src=\"../a.png\" alt=\"up\"></p>");
        var doc = MdDocument.Load(md, new Echo(), "NN");
        Assert.DoesNotContain("<img", doc.Page);
        Assert.Contains("picture blocked", doc.Page);
    }

    [Fact]
    public void An_engine_failure_becomes_a_notice_not_a_crash()
    {
        var md = IoPath.Combine(_dir, "doc.md");
        IoFile.WriteAllText(md, "x");
        var doc = MdDocument.Load(md, new Throwing(), "NN");
        Assert.StartsWith("Document could not be rendered", doc.Error);
    }

    private sealed class Throwing : IMdEngine
    {
        public string ToHtml(string markdown) => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void The_navigation_limit_counts_bytes_not_characters()
    {
        var md = IoPath.Combine(_dir, "cjk.md");
        IoFile.WriteAllText(md, "x");
        var doc = MdDocument.Load(md, new Wide(), "NN");
        Assert.Equal("Document too large to show", doc.Error);
    }

    private sealed class Wide : IMdEngine
    {
        public string ToHtml(string markdown) => "<p>" + new string('\u6f22', 700_000) + "</p>";
    }

    [Fact]
    public void A_page_over_the_navigation_limit_is_refused_not_cut()
    {
        var md = IoPath.Combine(_dir, "big.md");
        IoFile.WriteAllText(md, "x");
        var doc = MdDocument.Load(md, new Fat(), "NN");
        Assert.Equal("Document too large to show", doc.Error);
        Assert.True(doc.Page.Length < 20_000);
    }

    private sealed class Fat : IMdEngine
    {
        public string ToHtml(string markdown) => "<p>" + new string('x', MdDocument.MaxPageBytes) + "</p>";
    }
}
