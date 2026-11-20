using System.Text.RegularExpressions;
using System.Xml.Linq;
using NetieSpace.Apps.Pdf;
using NetieSpace.Helpers;
using NetieSpace.Models;
using NetieSpace.Services;
using UglyToad.PdfPig;

namespace NetieSpace.Tests;

/// <summary>
/// S50 peek defects seen in the desktop (windows) screenshots: glyph boxes, unreadable Word text,
/// "Open the preview" on documents that have text, words glued across PDF lines.
/// </summary>
public class PeekDefectTests
{
    private static string Repo(params string[] parts) =>
        Path.Combine(new[] { ProjectRoot.Find() }.Concat(parts).ToArray());

    private static PreviewContent Doc(PreviewKind kind, string? raw, string? html = null) => new()
    {
        FilePath = "x", FileName = "memo.docx", Kind = kind, RawText = raw, RenderedHtml = html, FileSizeBytes = 5_120,
    };

    // ---- 3. summary uses the document's words, not the "DOCX preview" label ----

    private const string MemoHtml =
        "<!DOCTYPE html><html><head><title>memo.docx</title><style>body{color:#222}</style></head>" +
        "<body><article class=\"office-body\"><p>Lease summary</p>" +
        "<p>Tenant pays rent on the first of each month.</p></article></body></html>";

    [Theory]
    [InlineData("DOCX preview")]
    [InlineData("PPTX preview")]
    public void Office_label_in_RawText_summarizes_the_rendered_page(string label)
    {
        var summary = LocalSummaryService.Build(Doc(PreviewKind.Office, label, MemoHtml));

        Assert.Contains("Tenant pays rent on the first of each month.", summary);
        Assert.DoesNotContain("Open the preview to read the full document.", summary);
        Assert.DoesNotContain("memo.docx", summary.Split('\n').Skip(1).FirstOrDefault() ?? "");
    }

    [Fact]
    public void ReadableText_ignores_style_script_and_keeps_one_line_per_block()
    {
        var html = "<html><head><style>p{color:red}</style><script>var a='nope';</script></head>" +
                   "<body><h1>Title &amp; more</h1><p>one</p><ul><li>two</li><li>three</li></ul>" +
                   "<table><tr><td>a</td><td>b</td></tr></table></body></html>";

        var text = LocalSummaryService.ReadableText(Doc(PreviewKind.Office, "DOCX preview", html));

        Assert.Equal("Title & more\none\ntwo\nthree\na b", text);
    }

    [Fact]
    public void Real_RawText_is_kept_over_the_rendered_page()
    {
        var raw = "This is the real extracted text of a native Open XML document, long enough.";
        Assert.Equal(raw, LocalSummaryService.ReadableText(Doc(PreviewKind.Office, raw, MemoHtml)));
    }

    [Fact]
    public void Office_label_with_no_rendered_page_still_reports_metadata_and_the_open_hint()
    {
        var summary = LocalSummaryService.Build(Doc(PreviewKind.Office, "DOCX preview"));
        Assert.Contains("Open the preview to read the full document.", summary);
    }

    [Theory]
    [InlineData(PreviewKind.Image, "Image: photo.png (640x480)")]
    [InlineData(PreviewKind.Video, "Video: clip.mp4")]
    [InlineData(PreviewKind.ShellInfo, "")]
    public void Pictures_and_clips_do_not_offer_to_open_a_document(PreviewKind kind, string raw)
    {
        var summary = LocalSummaryService.Build(Doc(kind, raw));

        Assert.DoesNotContain("full document", summary);
        Assert.Contains($"Type: {kind}", summary);
        Assert.Contains("Size: 5 KB", summary);
    }

    [Fact]
    public void Office_summary_is_not_sent_to_AI_as_a_label()
    {
        // Under 40 chars of label used to count as "sufficient without AI"; the real page is long.
        var longHtml = "<body>" + string.Concat(Enumerable.Repeat("<p>" + new string('w', 90) + ".</p>", 40)) + "</body>";
        var content = Doc(PreviewKind.Office, "DOCX preview", longHtml);

        Assert.True(LocalSummaryService.ReadableText(content).Length > LocalSummaryService.SmallDocChars);
        Assert.False(LocalSummaryService.IsSufficientWithoutAi(content, "• a\n• b"));
    }

    // ---- 4. PDF text keeps its line breaks ----

    [Fact]
    public void Pdf_page_text_is_one_line_per_visual_line()
    {
        using var doc = PdfDocument.Open(Repo("tests", "fixtures", "preview", "Q3_Report.pdf"));
        var text = PdfText.PageText(doc.GetPage(1));

        Assert.StartsWith("Q3 Report\nExecutive Summary\nRevenue grew 18% QoQ.", text.Replace("\r", ""));
        Assert.DoesNotContain("ReportExecutive", text);
        Assert.DoesNotContain("SummaryRevenue", text);
        // Control: the PdfPig property the peek used to read really is glued.
        Assert.Contains("ReportExecutive", doc.GetPage(1).Text);
    }

    [Fact]
    public void Pdf_summary_has_no_words_glued_across_lines()
    {
        using var doc = PdfDocument.Open(Repo("tests", "fixtures", "preview", "Q3_Report.pdf"));
        var content = new PreviewContent
        {
            FilePath = "Q3_Report.pdf", FileName = "Q3_Report.pdf", Kind = PreviewKind.Pdf, FileSizeBytes = 1_680,
            RawText = PdfText.PageText(doc.GetPage(1)),
        };

        var summary = LocalSummaryService.Build(content);

        Assert.Contains("Revenue grew 18% QoQ.", summary);
        Assert.DoesNotMatch(@"[a-z][A-Z][a-z]+ [A-Z][a-z]+[A-Z]", summary);
        Assert.DoesNotContain("SummaryRevenue", summary);
    }

    // ---- 1. icon glyph buttons ----

    [Fact]
    public void Every_icon_font_button_style_shields_its_glyph_from_the_app_TextBlock_style()
    {
        // App.xaml and every view: a Style that sets an icon FontFamily on a Button must carry its own
        // implicit TextBlock style, or the app-wide TextBlock style (BodyFont) wins on the TextBlock the
        // Button generates for a string glyph and the glyph draws as an empty box.
        var files = new[] { Repo("src", "NetieSpace", "App.xaml") }
            .Concat(Directory.GetFiles(Repo("src", "NetieSpace", "Views"), "*.xaml"));
        var checkedStyles = 0;
        foreach (var file in files)
        {
            var doc = XDocument.Load(file);
            foreach (var style in doc.Descendants().Where(e => e.Name.LocalName == "Style"))
            {
                if (style.Attribute("TargetType")?.Value != "Button")
                    continue;
                var setsIconFont = style.Elements().Any(e =>
                    e.Name.LocalName == "Setter" &&
                    e.Attribute("Property")?.Value == "FontFamily" &&
                    (e.Attribute("Value")?.Value ?? "").Contains("Segoe Fluent Icons"));
                if (!setsIconFont)
                    continue;

                checkedStyles++;
                var shield = style.Elements().Where(e => e.Name.LocalName == "Style.Resources")
                    .SelectMany(r => r.Elements())
                    .Any(e => e.Name.LocalName == "Style" &&
                              e.Attribute("TargetType")?.Value == "TextBlock" &&
                              e.Elements().Any(s => s.Attribute("Property")?.Value == "FontFamily"));
                Assert.True(shield,
                    $"{Path.GetFileName(file)}: Button style '{style.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value}' " +
                    "sets an icon font but has no TextBlock FontFamily shield in Style.Resources");
            }
        }

        Assert.True(checkedStyles >= 2, "expected IconGhostButton and DocIconBtn");
    }
}
