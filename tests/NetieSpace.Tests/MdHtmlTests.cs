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
        Assert.Contains("<h2 id=\"setup\">Setup<a class=\"md-anchor\" href=\"#setup\"", body.Html);
        Assert.Contains("<h1 id=\"appendix\">Appendix<a class=\"md-anchor\" href=\"#appendix\"", body.Html);
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
        Assert.Equal("a-b--c-d", body.Outline[0].Id); // GitHub: punctuation dropped, every space a hyphen
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
        Assert.Contains("<h2 id=\"hi\">Hi<a class=\"md-anchor\"", body.Html);
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
        Assert.Contains("border:1px solid var(--border)", page); // table borders
        Assert.Contains("--border:#bdbdbd", page);
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

    // ---- GitHub-style anchors, code blocks, mermaid ----

    [Theory]
    [InlineData("Getting started", "getting-started")]
    [InlineData("What's new?", "whats-new")]
    [InlineData("API & Usage", "api--usage")]
    [InlineData("C# 12 / .NET 8", "c-12--net-8")]
    [InlineData("snake_case and kebab-case", "snake_case-and-kebab-case")]
    [InlineData("Uber Caf\u00e9", "uber-caf\u00e9")]
    [InlineData("!!!", "section")]
    public void Heading_anchors_match_what_GitHub_writes_in_a_table_of_contents(string title, string id) =>
        Assert.Equal(id, MdHtml.Slug(title));

    [Fact]
    public void Every_heading_gets_a_link_to_itself_that_the_outline_title_does_not_include()
    {
        var body = Process("<h2>Setup</h2>");
        Assert.Contains("<a class=\"md-anchor\" href=\"#setup\" aria-label=\"Link to this section\">#</a>", body.Html);
        Assert.Equal("Setup", body.Outline[0].Title);
    }

    [Fact]
    public void A_code_block_gets_its_language_label_and_a_copy_button_and_keeps_its_escaped_text()
    {
        var body = Process("<pre><code class=\"language-csharp\">var a = 1 &lt; 2;\n</code></pre>");
        Assert.Contains("<span class=\"md-lang\">csharp</span>", body.Html);
        Assert.Contains("class=\"md-copy\"", body.Html);
        Assert.Contains("<pre><code class=\"language-csharp\">var a = 1 &lt; 2;\n</code></pre>", body.Html);
        Assert.DoesNotContain("not rendered", body.Html);
    }

    [Fact]
    public void A_code_block_without_a_language_still_gets_a_copy_button()
    {
        var body = Process("<pre><code>plain\n</code></pre>");
        Assert.Contains("class=\"md-copy\"", body.Html);
        Assert.DoesNotContain("md-lang", body.Html);
    }

    [Fact]
    public void A_language_name_that_could_carry_markup_is_dropped()
    {
        var body = Process("<pre><code class=\"language-x&quot; onclick=&quot;evil()\">a</code></pre>");
        Assert.DoesNotContain("onclick", body.Html);
        Assert.DoesNotContain("language-x", body.Html);
    }

    [Fact]
    public void A_mermaid_block_shows_its_source_and_says_it_was_not_drawn()
    {
        var body = Process("<pre><code class=\"language-mermaid\">graph TD; A--&gt;B\n</code></pre>");
        Assert.Contains(MdHtml.MermaidNote, body.Html);
        Assert.Contains("md-mermaid", body.Html);
        Assert.Contains("graph TD; A--&gt;B", body.Html);
        Assert.Contains("nohighlight", body.Html);
    }

    [Fact]
    public void Alerts_keep_their_classes_and_the_picture_and_link_rules_still_apply_inside_them()
    {
        var body = Process("<div class=\"markdown-alert markdown-alert-note\"><p class=\"markdown-alert-title\">Note</p><p><a href=\"javascript:1\">x</a></p></div>");
        Assert.Contains("markdown-alert markdown-alert-note", body.Html);
        Assert.DoesNotContain("javascript:", body.Html);
    }

    // ---- the page: theme, highlighter, find ----

    [Fact]
    public void A_fixed_colour_mode_is_written_on_the_page_and_system_writes_nothing()
    {
        Assert.Contains("<html lang=\"en\" data-theme=\"dark\">", MdShell.Page("t", "", "N", new MdPageOptions(MdColorMode.Dark)));
        Assert.Contains("<html lang=\"en\" data-theme=\"light\">", MdShell.Page("t", "", "N", new MdPageOptions(MdColorMode.Light)));
        Assert.DoesNotContain("data-theme=\"", MdShell.Page("t", "", "N").Split("<style>")[0]);
    }

    [Fact]
    public void Dark_colours_exist_for_the_system_setting_and_for_the_toggle_and_print_is_always_light()
    {
        Assert.Contains("@media (prefers-color-scheme:dark){:root:not([data-theme=light])", MdShell.Css);
        Assert.Contains(":root[data-theme=dark]{", MdShell.Css);
        var print = MdShell.Css[MdShell.Css.IndexOf("@media print", StringComparison.Ordinal)..];
        Assert.Contains("--bg:#fff", print);
        Assert.Contains(".md-copy", print);
        Assert.DoesNotContain("--bg:#16181c", print);
    }

    [Fact]
    public void The_highlighter_is_inlined_once_under_the_same_nonce_and_a_page_without_it_still_works()
    {
        var with = MdShell.Page("t", "<p>x</p>", "ABC", new MdPageOptions(MdColorMode.System, "var hljs={};"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(with, "<script"));
        Assert.Contains("var hljs={};", with);
        Assert.Contains("window.hljs", with);
        Assert.Contains("default-src 'none'", with);
        Assert.DoesNotContain("var hljs", MdShell.Page("t", "", "A"));
    }

    [Fact]
    public void The_page_has_a_find_bar_and_the_script_reports_what_the_host_listens_for()
    {
        var page = MdShell.Page("t", "", "N");
        Assert.Contains("id=\"md-find\"", page);
        Assert.Contains("id=\"md-find-q\"", page);
        foreach (var message in new[] { "'link:'", "'copy:'", "'section:'", "'nav:back'", "'nav:forward'", "'zoom:in'", "'ui:open'", "'ui:print'" })
            Assert.Contains(message, MdShell.Script);
        Assert.DoesNotContain("fetch(", MdShell.Script);
        Assert.DoesNotContain("XMLHttpRequest", MdShell.Script);
        Assert.DoesNotContain("eval(", MdShell.Script);
    }

    [Fact]
    public void The_page_text_is_ascii_only()
    {
        Assert.All(MdShell.Css + MdShell.Script + MdShell.FindBar, c => Assert.True(c < 128, "non-ASCII char " + (int)c));
    }
}
