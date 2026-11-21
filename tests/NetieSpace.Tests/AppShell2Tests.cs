using NetieSpace.Apps.Shell;
using NetieSpace.Apps.Tools;

namespace NetieSpace.Tests;

/// <summary>Home search, open-with ranking and tile glyphs: the decisions behind the home grid.</summary>
public class AppShell2HomeTests
{
    private static HomeTile T(string id, string name, string verb, string cat, string types, bool available = true) =>
        new(id, "X", name, verb, available, available ? null : "not built", cat,
            types.Length == 0 ? Array.Empty<string>() : types.Split(','));

    private static readonly HomeTile Merge = T("pdf-merge", "Merge PDF", "Merge", "Organize PDF", ".pdf");
    private static readonly HomeTile Word = T("pdf-from-word", "Word to PDF", "Convert", "Convert to PDF", ".doc,.docx");
    private static readonly HomeTile Jpg = T("pdf-from-jpg", "JPG to PDF", "Convert", "Convert to PDF", ".jpg,.jpeg,.png");
    private static readonly HomeTile Rotate = T("pdf-rotate", "Rotate PDF", "Rotate", "Edit PDF", ".pdf");
    private static readonly HomeTile Compress = T("pdf-compress", "Compress PDF", "Compress", "Optimize PDF", ".pdf,.jpg", available: false);
    private static readonly HomeTile Any = T("any", "Any tool", "Run", "More", "");

    private static IReadOnlyList<HomeCategory> Cats() => new[]
    {
        new HomeCategory("Organize PDF", new[] { Merge }),
        new HomeCategory("Convert to PDF", new[] { Word, Jpg }),
        new HomeCategory("Edit PDF", new[] { Rotate }),
        new HomeCategory("Optimize PDF", new[] { Compress }),
    };

    [Fact]
    public void Empty_query_shows_everything()
    {
        var all = ToolSearch.Filter(Cats(), "   ");
        Assert.Equal(5, all.Sum(c => c.Tiles.Count));
        Assert.True(ToolSearch.Matches(Merge, null));
    }

    [Theory]
    [InlineData("merge", "pdf-merge")]
    [InlineData("MERGE", "pdf-merge")]
    [InlineData("rotate", "pdf-rotate")]
    [InlineData("optimize", "pdf-compress")]      // category
    [InlineData(".docx", "pdf-from-word")]        // accepted type with the dot
    [InlineData("docx", "pdf-from-word")]         // accepted type without it
    [InlineData("png", "pdf-from-jpg")]
    public void Query_matches_name_verb_category_and_type(string query, string expectedId)
    {
        var ids = ToolSearch.Filter(Cats(), query).SelectMany(c => c.Tiles).Select(t => t.ToolId).ToList();
        Assert.Contains(expectedId, ids);
    }

    [Fact]
    public void Tokens_are_anded_not_ored()
    {
        var ids = ToolSearch.Filter(Cats(), "convert pdf").SelectMany(c => c.Tiles).Select(t => t.ToolId).ToList();
        Assert.Equal(new[] { "pdf-from-word", "pdf-from-jpg" }, ids);

        // Both tokens must hit: "word" and "rotate" never share a tool.
        Assert.Empty(ToolSearch.Filter(Cats(), "word rotate"));
    }

    [Fact]
    public void Categories_without_a_match_are_dropped()
    {
        var shown = ToolSearch.Filter(Cats(), "rotate");
        Assert.Equal(new[] { "Edit PDF" }, shown.Select(c => c.Name));
    }

    [Fact]
    public void No_match_gives_the_plain_no_tools_line()
    {
        var grid = HomeGrid.Build(Cats(), null, "zzzz", null);
        Assert.Equal("No tools", grid.EmptyLine);
        Assert.Empty(grid.Categories);
        Assert.Null(HomeGrid.Build(Cats(), null, "merge", null).EmptyLine);
    }

    [Fact]
    public void A_typed_query_searches_every_category_not_just_the_open_one()
    {
        var grid = HomeGrid.Build(Cats(), "Edit PDF", "merge", null);
        Assert.Equal("pdf-merge", grid.Categories.Single().Tiles.Single().ToolId);
        // No query: the sidebar choice applies.
        Assert.Equal("Edit PDF", HomeGrid.Build(Cats(), "Edit PDF", "", null).Categories.Single().Name);
    }

    [Fact]
    public void Open_with_lists_only_available_tools_that_accept_the_type_specific_first()
    {
        var ranked = OpenWithRanking.Rank(new[] { Any, Merge, Word, Jpg, Rotate, Compress }, @"C:\x\Report.PDF");
        // Compress accepts .pdf but is unavailable; Any accepts everything and goes last.
        Assert.Equal(new[] { "pdf-merge", "pdf-rotate", "any" }, ranked.Select(t => t.ToolId));
    }

    [Fact]
    public void Open_with_ranks_fewer_accepted_types_first()
    {
        var narrow = T("a", "Narrow", "Go", "Edit PDF", ".png");
        var wide = T("b", "Wide", "Go", "Edit PDF", ".jpg,.png,.gif,.bmp");
        var mid = T("c", "Mid", "Go", "Edit PDF", ".png,.jpg");
        var ranked = OpenWithRanking.Rank(new[] { wide, mid, narrow }, "p.png");
        Assert.Equal(new[] { "a", "c", "b" }, ranked.Select(t => t.ToolId));
    }

    [Fact]
    public void Open_with_is_empty_for_an_unknown_or_missing_extension()
    {
        Assert.Empty(OpenWithRanking.Rank(new[] { Merge, Word }, "notes.xyz"));
        Assert.Empty(OpenWithRanking.Rank(new[] { Merge }, "README"));
        Assert.Empty(OpenWithRanking.Rank(new[] { Merge }, null));
    }

    [Fact]
    public void Grid_puts_the_open_with_group_first_and_keeps_the_rest()
    {
        var grid = HomeGrid.Build(Cats(), null, null, "a.docx");
        Assert.Equal("Suggested for a.docx", grid.OpenWithTitle);
        Assert.Equal(new[] { "pdf-from-word" }, grid.OpenWith.Select(t => t.ToolId));
        Assert.Equal(4, grid.Categories.Count); // the full grid is still there below
        Assert.Null(HomeGrid.Build(Cats(), null, null, null).OpenWithTitle);
    }

    [Fact]
    public void Real_catalog_routes_a_pdf_to_pdf_tools_and_a_jpg_to_the_image_app()
    {
        var tools = ToolRegistry.Default.All();
        var pdf = HomeModel.Categories(AppIdentity.Pdf, tools).SelectMany(c => c.Tiles).ToList();
        var image = HomeModel.Categories(AppIdentity.Image, tools).SelectMany(c => c.Tiles).ToList();

        var forPdf = OpenWithRanking.Rank(pdf, "a.pdf").Select(t => t.ToolId).ToList();
        Assert.Contains("pdf-merge", forPdf);
        Assert.DoesNotContain("pdf-from-word", forPdf);

        var forJpg = OpenWithRanking.Rank(image, "a.jpg").Select(t => t.ToolId).ToList();
        Assert.Contains("img-resize", forJpg);
        Assert.Empty(OpenWithRanking.Rank(image, "a.pdf"));
    }
}

public class AppShell2GlyphTests
{
    [Fact]
    public void Every_catalog_category_resolves_to_a_valid_geometry_of_its_own()
    {
        var cats = ToolRegistry.Default.All().Select(t => t.Entry.Category).Distinct().ToList();
        Assert.True(cats.Count >= 20);
        foreach (var c in cats)
        {
            var d = TileGlyphs.ForCategory(c);
            Assert.False(string.IsNullOrWhiteSpace(d), c);
            Assert.True(TileGlyphs.IsValidPath(d), c + ": " + d);
            // A category must not silently land on the fallback.
            Assert.NotEqual(TileGlyphs.Fallback, d);
        }
    }

    [Fact]
    public void Unknown_category_falls_back_to_a_valid_geometry()
    {
        Assert.Equal(TileGlyphs.Fallback, TileGlyphs.ForCategory("No such category"));
        Assert.Equal(TileGlyphs.Fallback, TileGlyphs.ForCategory(null));
        Assert.True(TileGlyphs.IsValidPath(TileGlyphs.Fallback));
    }

    [Fact]
    public void Every_table_entry_is_valid_and_stays_on_the_grid()
    {
        foreach (var c in TileGlyphs.Categories)
        {
            var d = TileGlyphs.ForCategory(c);
            Assert.True(TileGlyphs.IsValidPath(d), c);
            var nums = System.Text.RegularExpressions.Regex.Matches(d, @"-?\d+(?:\.\d+)?")
                .Select(m => double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture));
            Assert.All(nums, n => Assert.InRange(n, 0, TileGlyphs.Grid));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("L4 4 H8")]            // must start with M
    [InlineData("M4 4 X8 8")]          // unknown command
    [InlineData("M4 4 L8")]            // odd argument count
    [InlineData("M4 4 Z 3")]           // Z takes none
    [InlineData("M4 4 L8 8 url(x)")]   // stray text
    [InlineData("M4 4 L8 8; M1 1")]    // stray punctuation
    [InlineData("M4 4 A5 5 0 0 1 8 8")] // arcs are not used here
    public void Validator_rejects_malformed_geometry(string bad)
    {
        Assert.False(TileGlyphs.IsValidPath(bad));
    }

    [Fact]
    public void Tile_carries_the_category_geometry_and_keeps_the_monogram_for_other_uses()
    {
        var tile = HomeModel.Categories(AppIdentity.Pdf, ToolRegistry.Default.All())
            .SelectMany(c => c.Tiles).First(t => t.ToolId == "pdf-merge");
        Assert.Equal(TileGlyphs.ForCategory("Organize PDF"), tile.PathData);
        Assert.Equal("M", tile.Glyph);
    }
}

#if WINDOWS
public class AppShell2GlyphWindowsTests
{
    [Fact]
    public void Geometry_Parse_accepts_every_glyph()
    {
        foreach (var c in TileGlyphs.Categories.Concat(new[] { "unknown category" }))
        {
            var g = System.Windows.Media.Geometry.Parse(TileGlyphs.ForCategory(c));
            Assert.False(g.IsEmpty(), c);
            var b = g.Bounds;
            Assert.True(b.Width > 0 || b.Height > 0, c);
        }
    }
}
#endif
