using System.Text.RegularExpressions;
using NetieSpace.Apps.Tools;
using NetieSpace.Apps.Tools.Catalog;

namespace NetieSpace.Tests;

public class ToolRegistryTests
{
    private static readonly Regex ClosedReasons = new(
        @"^(not built in this version|engine missing: .+|licence not cleared for shipping: .+|needs Space#3 disclose|cloud only)$");

    [Fact]
    public void Catalog_has_91_entries_split_37_16_38_with_unique_ids()
    {
        var all = ToolRegistry.BuildCatalog();
        Assert.Equal(91, all.Count);
        Assert.Equal(37, all.Count(e => e.App == ToolApp.Pdf));
        Assert.Equal(16, all.Count(e => e.App == ToolApp.Image));
        Assert.Equal(38, all.Count(e => e.App == ToolApp.Video));
        Assert.Equal(91, all.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(all, e =>
        {
            Assert.StartsWith(e.App switch { ToolApp.Pdf => "pdf-", ToolApp.Image => "img-", _ => "vid-" }, e.Id);
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
        });
    }

    [Fact]
    public void Catalog_ids_equal_the_research_table_ids_exactly()
    {
        var md = IoFile.ReadAllLines(ToolTestSupport.RepoFile("docs", "research", "findings", "2026-09-17_ecosystem_tool_catalog.md"));
        var fromDoc = md.Select(l => Regex.Match(l, @"^\| ((pdf|img|vid)-[a-z0-9-]+) \|"))
            .Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(91, fromDoc.Count);
        var fromCode = ToolRegistry.BuildCatalog().Select(e => e.Id).ToList();
        Assert.Equal(fromDoc.OrderBy(x => x, StringComparer.Ordinal), fromCode.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_unavailable_entry_has_a_reason_from_the_closed_set()
    {
        var reg = ToolRegistry.Load();
        var all = reg.All();
        Assert.Equal(91, all.Count);
        foreach (var t in all)
        {
            if (t.Availability.Available)
            {
                Assert.Equal("", t.Availability.Text);
                Assert.NotNull(t.Runner);
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(t.Availability.Text), t.Entry.Id);
                Assert.Matches(ClosedReasons, t.Availability.Text);
                Assert.NotEqual(UnavailableReason.None, t.Availability.Reason);
            }
        }
    }

    [Theory]
    [InlineData("PyMuPDF")]
    [InlineData("pymupdf4llm")]
    [InlineData("Ghostscript")]
    [InlineData("OnlyOffice")]
    public void Agpl_engine_is_unavailable_with_licence_reason_even_when_probe_says_present(string engineName)
    {
        var engine = Engines.All.Single(e => e.Name == engineName);
        Assert.Equal(EngineLicense.AGPL, engine.License);
        var entry = ToolTestSupport.FakeEntry(engine);
        var probe = FuncProbe.AlwaysPresent();

        var a = ToolAvailabilityRules.Compute(entry, hasRunner: true, probe);

        Assert.False(a.Available);
        Assert.Equal(UnavailableReason.LicenceNotCleared, a.Reason);
        Assert.Equal("licence not cleared for shipping: " + engineName, a.Text);
    }

    [Fact]
    public void Same_entry_on_a_cleared_engine_is_available_so_the_licence_gate_is_what_decides()
    {
        var probe = FuncProbe.AlwaysPresent();
        var cleared = ToolAvailabilityRules.Compute(ToolTestSupport.FakeEntry(Engines.PikePdf), true, probe);
        var banned = ToolAvailabilityRules.Compute(ToolTestSupport.FakeEntry(Engines.PyMuPdf), true, probe);
        Assert.True(cleared.Available);
        Assert.False(banned.Available);
    }

    [Fact]
    public void Four_banned_engines_are_all_registered_as_agpl_and_nothing_else_is()
    {
        var agpl = Engines.All.Where(e => e.License == EngineLicense.AGPL).Select(e => e.Name).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "Ghostscript", "OnlyOffice", "PyMuPDF", "pymupdf4llm" }, agpl);
    }

    [Fact]
    public void Named_catalog_entries_report_the_contracted_reasons_with_a_present_engine_probe()
    {
        var reg = ToolRegistry.Load(probe: FuncProbe.AlwaysPresent());
        string Text(string id) => reg.Find(id)!.Availability.Text;

        Assert.True(reg.Find("pdf-extract-text")!.Availability.Available);
        Assert.Equal("not built in this version", Text("pdf-watermark"));
        Assert.Equal("licence not cleared for shipping: Ghostscript", Text("pdf-to-pdfa"));
        Assert.Equal("needs Space#3 disclose", Text("pdf-summarize"));
        Assert.Equal("cloud only", Text("pdf-sign-request"));
    }

    [Fact]
    public void Missing_engine_reports_engine_missing_with_the_probe_detail()
    {
        var reg = ToolRegistry.Load(probe: new FuncProbe(e => e.Name == "rembg" ? (false, "rembg") : (true, "")));
        Assert.Equal("engine missing: rembg", reg.Find("img-remove-bg")!.Availability.Text);
    }

    [Fact]
    public void Real_probes_never_claim_a_cloud_or_banned_engine_present()
    {
        var p = new EngineProbes();
        Assert.False(p.Probe(Engines.CloudLlm).Ok);
        Assert.False(p.Probe(Engines.HostedSigner).Ok);
        Assert.True(p.Probe(Engines.PdfPig).Ok);
    }

    [Fact]
    public void Provider_with_unknown_tool_id_throws_naming_the_id()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ToolRegistry.Load(
            probe: FuncProbe.AlwaysPresent(),
            extraProviders: new IToolProvider[] { new FakeProvider(("pdf-no-such-tool", (_, _, _) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()))) }));
        Assert.Contains("pdf-no-such-tool", ex.Message);
    }

    [Fact]
    public void Two_runners_for_one_id_throw_naming_the_id()
    {
        ToolRunnerDelegate noop = (_, _, _) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        var ex = Assert.Throws<InvalidOperationException>(() => ToolRegistry.Load(
            probe: FuncProbe.AlwaysPresent(),
            extraProviders: new IToolProvider[] { new FakeProvider(("pdf-merge", noop)), new FakeProvider(("pdf-merge", noop)) }));
        Assert.Contains("pdf-merge", ex.Message);
    }

    [Fact]
    public void Reflection_finds_providers_in_an_assembly_and_skips_opted_out_fakes()
    {
        var found = ToolRegistry.DiscoverProviders(typeof(ToolRegistryTests).Assembly);
        Assert.Contains(found, p => p.GetType().FullName == "NetieSpace.Apps.Pdf.PdfToolProvider");
        Assert.DoesNotContain(found, p => p is FakeProvider);
    }

    [Fact]
    public void Default_load_picks_up_the_pdf_provider_runner_by_reflection()
    {
        var t = ToolRegistry.Load(probe: FuncProbe.AlwaysPresent()).Find("pdf-extract-text")!;
        Assert.NotNull(t.Runner);
        Assert.True(t.Availability.Available);
    }

    [Fact]
    public void Availability_is_computed_lazily_and_each_engine_is_probed_once()
    {
        var probe = FuncProbe.AlwaysPresent();
        var reg = ToolRegistry.Load(probe: probe);
        Assert.Equal(0, probe.Calls);
        reg.All();
        var after = probe.Calls;
        reg.All();
        reg.Find("pdf-merge");
        Assert.True(after > 0);
        Assert.Equal(after, probe.Calls);
        Assert.True(after < 91, "probes are cached per engine, not per tool");
    }

    [Fact]
    public void InstallOffer_rejects_zero_size_and_empty_source_url()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InstallOffer("https://example.test/x.bin", 0, "MIT"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InstallOffer("https://example.test/x.bin", -5, "MIT"));
        Assert.Throws<ArgumentException>(() => new InstallOffer("", 10, "MIT"));
        Assert.Throws<ArgumentException>(() => new InstallOffer("   ", 10, "MIT"));
        var ok = new InstallOffer("https://example.test/x.bin", 10, "MIT");
        Assert.Equal(10, ok.SizeBytes);
    }

    [Fact]
    public void No_catalog_entry_ships_an_install_offer_until_a_url_and_size_are_known()
    {
        Assert.All(ToolRegistry.BuildCatalog(), e => Assert.Null(e.InstallOffer));
    }

    [Fact]
    public void Tools_folder_is_free_of_wpf_and_service_references()
    {
        var dir = ToolTestSupport.RepoFile("src", "NetieSpace", "Apps", "Tools");
        foreach (var f in IoDirectory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            var text = IoFile.ReadAllText(f);
            Assert.DoesNotContain("System.Windows", text);
            Assert.DoesNotContain("NetieSpace.Views", text);
            Assert.DoesNotContain("NetieSpace.Services", text);
            Assert.True(text.All(c => c < 128), "non-ASCII in " + f);
        }
    }
}

/// <summary>Names and verbs are product copy: shown on tiles and on the action button.</summary>
public class ToolCatalogCopyTests
{
    private static readonly string[] SourceSites = { "123apps", "clideo", "ilovepdf", "iloveimg", "iloveapi", "api-only", "(both)" };

    [Fact]
    public void Names_verbs_and_categories_carry_no_source_site_and_no_sentence()
    {
        var entries = NetieSpace.Apps.Tools.ToolRegistry.Default.All().Select(t => t.Entry).ToList();
        Assert.Equal(91, entries.Count);
        foreach (var e in entries)
        {
            foreach (var shown in new[] { e.Name, e.Verb, e.Category })
            {
                Assert.False(string.IsNullOrWhiteSpace(shown), e.Id);
                Assert.DoesNotContain("(", shown);
                Assert.All(SourceSites, s => Assert.DoesNotContain(s, shown, StringComparison.OrdinalIgnoreCase));
            }
            Assert.True(e.Name.Length <= 24, $"{e.Id}: name too long for a tile: {e.Name}");
            Assert.Matches("^[A-Z][a-z]+$", e.Verb); // one word: it is the action button text
        }
    }
}
