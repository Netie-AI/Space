using System.Globalization;
using NetieSpace.Apps.Shell;
using NetieSpace.Apps.Tools;

namespace NetieSpace.Tests;

/// <summary>S50 wave 5 app UI: per-tool glyphs, readable option labels, result card data, preview plan, per-app accents.</summary>
public class AppUiTests
{
    private static IReadOnlyList<RegisteredTool> Tools => ToolRegistry.Default.All();

    [Fact]
    public void Every_catalog_tool_has_its_own_glyph_and_no_glyph_names_a_missing_tool()
    {
        var ids = Tools.Select(t => t.Entry.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(91, ids.Count);
        foreach (var id in ids)
            Assert.NotNull(ToolGlyphs.For(id));
        Assert.All(ToolGlyphs.ToolIds, id => Assert.Contains(id, ids));
        Assert.Equal(ids.Count, ToolGlyphs.ToolIds.Count);
    }

    [Fact]
    public void Tools_in_one_category_never_share_a_glyph()
    {
        foreach (var group in Tools.GroupBy(t => (t.Entry.App, t.Entry.Category)))
        {
            var seen = new Dictionary<int, string>();
            foreach (var t in group)
            {
                var cp = ToolGlyphs.CodePoint(t.Entry.Id)!.Value;
                Assert.InRange(cp, 0xE000, 0xF8FF); // private use area, where the icon fonts live
                Assert.False(seen.TryGetValue(cp, out var other),
                    $"{group.Key.Category}: {t.Entry.Id} and {other} both use U+{cp:X4}");
                seen[cp] = t.Entry.Id;
            }
        }
    }

    [Fact]
    public void The_five_tools_that_shared_one_icon_now_differ_and_the_tile_carries_the_glyph()
    {
        var ids = new[] { "pdf-merge", "pdf-split", "pdf-remove-pages", "pdf-extract-pages", "pdf-organize" };
        Assert.Equal(5, ids.Select(ToolGlyphs.For).Distinct().Count());
        var tile = HomeModel.Categories(AppIdentity.Pdf, Tools).SelectMany(c => c.Tiles).First(t => t.ToolId == "pdf-merge");
        Assert.Equal(ToolGlyphs.For("pdf-merge"), tile.IconGlyph);
        Assert.Null(ToolGlyphs.For("no-such-tool"));
    }

    [Theory]
    [InlineData("direction", "right", "Rotate right 90 degrees")]
    [InlineData("direction", "left", "Rotate left 90 degrees")]
    [InlineData("filter", "all", "All pages")]
    [InlineData("filter", "landscape", "Landscape pages only")]
    [InlineData("position", "bottom-center", "Bottom center")]
    [InlineData("format", "mp3", "MP3")]
    [InlineData("level", "extreme", "Smallest file")]
    public void Choice_values_read_as_labels(string key, string value, string label)
    {
        Assert.Equal(label, OptionLabels.For(key, value));
    }

    [Fact]
    public void Every_choice_in_the_catalog_has_a_label_that_is_not_the_raw_id_and_is_unique_within_its_option()
    {
        foreach (var t in Tools)
            foreach (var o in new ToolPageModel(t).Options.Where(o => o.Choices.Count > 0))
            {
                var labels = o.Choices.Select(o.ChoiceLabel).ToList();
                Assert.All(labels, l => Assert.False(string.IsNullOrWhiteSpace(l), $"{t.Entry.Id}.{o.Key}"));
                Assert.Equal(labels.Count, labels.Distinct(StringComparer.OrdinalIgnoreCase).Count());
                foreach (var (choice, label) in o.Choices.Zip(labels))
                {
                    Assert.DoesNotContain('_', label);
                    if (choice.Contains('-'))
                        Assert.DoesNotContain('-', label);
                    // the stored value is not rewritten
                    Assert.Contains(choice, o.Choices);
                }
            }
    }

    [Fact]
    public void An_all_pages_default_shows_a_hint_and_sends_nothing_so_the_tool_default_still_applies()
    {
        var rotate = Tools.First(t => t.Entry.Id == "pdf-rotate");
        var model = new ToolPageModel(rotate);
        var pages = model.Options.First(o => o.Key == "pages");
        Assert.Equal("1-end", pages.Default);       // the stored default is unchanged
        Assert.Equal("All pages", pages.Hint);
        Assert.Equal("", pages.StartText);

        var request = model.BuildRequest(new[] { "a.pdf" }, "out", new Dictionary<string, string> { ["pages"] = "", ["direction"] = "left" });
        Assert.False(request.Options.ContainsKey("pages"));
        Assert.Equal("left", request.Options["direction"]);

        var typed = model.BuildRequest(new[] { "a.pdf" }, "out", new Dictionary<string, string> { ["pages"] = "2-4" });
        Assert.Equal("2-4", typed.Options["pages"]);

        var sign = new ToolPageModel(Tools.First(t => t.Entry.Id == "pdf-sign-self")).Options.First(o => o.Key == "pages");
        Assert.Equal("Last page only", sign.Hint);
        Assert.Null(new ToolPageModel(Tools.First(t => t.Entry.Id == "pdf-split")).Options.First(o => o.Key == "ranges").Hint);
    }

    [Fact]
    public void Result_summary_names_the_file_size_and_keeps_the_done_line()
    {
        var ok = new ToolResult(true, ToolOutcome.Ok, "pdf-rotate", "PdfSharpCore",
            new[] { "/out/Q3_Report-rotated.pdf" }, null, null, 12);
        var s = ToolPageModel.Summarize(ok, _ => 48 * 1024);
        Assert.True(s.Ok);
        Assert.Equal("Q3_Report-rotated.pdf", s.Title);
        Assert.Equal("PDF file - 48 KB", s.Detail);
        Assert.StartsWith("Done: ", s.Line);          // check-desktop.ps1 reads the output path from this line
        Assert.Equal("/out/Q3_Report-rotated.pdf", s.Primary);

        var unknownSize = ToolPageModel.Summarize(ok, _ => null);
        Assert.Equal("PDF file", unknownSize.Detail);

        var many = new ToolResult(true, ToolOutcome.Ok, "pdf-split", "PdfSharpCore",
            new[] { "/out/a.pdf", "/out/b.pdf", "/out/c.pdf" }, null, null, 12);
        var m = ToolPageModel.Summarize(many, _ => 1024);
        Assert.Equal("3 files created", m.Title);
        Assert.Equal("3 files - 3 KB", m.Detail);
        Assert.Equal(3, m.FileCount);

        var bad = new ToolResult(false, ToolOutcome.Failed, "pdf-rotate", "PdfSharpCore",
            Array.Empty<string>(), "cannot open x.pdf", null, 0);
        var f = ToolPageModel.Summarize(bad, _ => null);
        Assert.False(f.Ok);
        Assert.Equal("Could not finish", f.Title);
        Assert.Equal("cannot open x.pdf", f.Line);
        Assert.Null(f.Primary);
    }

    [Theory]
    [InlineData("a.pdf", PreviewKind.Pdf)]
    [InlineData("A.PDF", PreviewKind.Pdf)]
    [InlineData("photo.JPG", PreviewKind.Image)]
    [InlineData("scan.tiff", PreviewKind.Image)]
    [InlineData("clip.mp4", PreviewKind.Other)]
    [InlineData("noext", PreviewKind.Other)]
    public void Preview_kind_follows_the_extension(string path, PreviewKind kind)
    {
        Assert.Equal(kind, PreviewPlan.KindOf(path));
    }

    [Fact]
    public void Preview_captions()
    {
        Assert.Equal("1 page", PreviewPlan.PageCaption(1, 1));
        Assert.Equal("5 pages", PreviewPlan.PageCaption(5, 5));
        Assert.Equal("40 pages - first 12 shown", PreviewPlan.PageCaption(40, 12));
        Assert.Equal("MP4", PreviewPlan.TypeBadge("x.mp4"));
        Assert.Equal("FILE", PreviewPlan.TypeBadge("x"));
    }

    [Fact]
    public void Each_app_has_its_own_accent_family_and_text_on_it_is_readable()
    {
        Assert.Equal(4, AppPalette.All.Select(p => p.Accent).Distinct().Count());
        foreach (var p in AppPalette.All)
        {
            foreach (var hex in new[] { p.Accent, p.Hover, p.Press, p.Tint, p.Strong, p.Ink })
                Assert.Matches("^#[0-9A-F]{6}$", hex);
            Assert.True(Contrast(p.Ink, p.Accent) >= 4.5, $"{p.Key}: ink on accent {Contrast(p.Ink, p.Accent):0.0}");
            Assert.True(Contrast(p.Ink, p.Hover) >= 4.5, $"{p.Key}: ink on hover");
            Assert.True(Contrast(p.Ink, p.Press) >= 4.5, $"{p.Key}: ink on press");
            Assert.True(Contrast(p.Strong, p.Tint) >= 4.5, $"{p.Key}: icon on tint {Contrast(p.Strong, p.Tint):0.0}");
            Assert.True(Contrast(p.Strong, "#FFFFFF") >= 4.5, $"{p.Key}: icon on white");
        }

        var (pr, pg, pb) = Rgb(AppPalette.Pdf.Accent);
        Assert.True(pr > pg + 60 && pr > pb + 60, "PDF is red");
        var (ir, ig, ib) = Rgb(AppPalette.Image.Accent);
        Assert.True(ig > ir + 60 && ib > ir + 60, "Image is teal");
        var (vr, vg, vb) = Rgb(AppPalette.Video.Accent);
        Assert.True(vb > vg + 60 && vr > vg, "Video is violet");
        var (mr, mg, mb) = Rgb(AppPalette.Md.Accent);
        Assert.True(Math.Abs(mr - mb) < 60 && mb >= mr && mg >= mr, "MD is slate");
        Assert.Equal(AppPalette.Pdf, AppPalette.For("PDF"));
        Assert.Equal(AppPalette.Md, AppPalette.For(AppIdentity.Md.Key));
        Assert.Equal(AppPalette.Image, AppPalette.For("nope"));
    }

    private static (int R, int G, int B) Rgb(string hex) => (
        int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static double Luminance(string hex)
    {
        var (r, g, b) = Rgb(hex);
        static double Lin(int c)
        {
            var v = c / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b);
    }

    private static double Contrast(string a, string b)
    {
        var (hi, lo) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (hi + 0.05) / (lo + 0.05);
    }
}
