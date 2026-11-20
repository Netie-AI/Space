using System.Xml.Linq;
using NetieSpace.Helpers;
using NetieSpace.Models;

namespace NetieSpace.Tests;

/// <summary>
/// S50 wave 5 peek chrome: one header row, secondary actions in More, Fit never enlarges,
/// no summary strip on pictures and clips unless asked.
/// </summary>
public class PeekChromeTests
{
    private static string Repo(params string[] parts) =>
        Path.Combine(new[] { ProjectRoot.Find() }.Concat(parts).ToArray());

    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument PreviewXaml() => XDocument.Load(Repo("src", "NetieSpace", "Views", "PreviewWindow.xaml"));

    private static XElement Named(XDocument doc, string name) =>
        doc.Descendants().First(e => e.Attribute(X + "Name")?.Value == name);

    private static bool Inside(XElement e, string ancestorName) =>
        e.Ancestors().Any(a => a.Attribute(X + "Name")?.Value == ancestorName);

    // ---- 1. header ----

    [Fact]
    public void Secondary_actions_exist_and_live_in_the_More_sheet()
    {
        var doc = PreviewXaml();
        foreach (var name in new[]
        {
            "SummaryToggleButton", "ContinueChatButton", "ProviderButton", "RemoveBgButton", "OcrImageButton",
            "CopyImageButton", "CompressImageButton", "RotateImageButton", "SendToComfyButton",
            "WindowsAiButton", "EditInAppButton",
        })
        {
            var b = Named(doc, name);
            Assert.True(Inside(b, "PeekMorePanel"), $"{name} must be a row in the More sheet (moved, not removed)");
            Assert.False(Inside(b, "PeekHeaderRow"), $"{name} must not be in the header row");
        }
    }

    [Fact]
    public void Header_row_is_a_single_non_wrapping_row_with_the_primary_slot_and_icons()
    {
        var doc = PreviewXaml();
        var row = Named(doc, "PeekHeaderRow");
        Assert.Equal("StackPanel", row.Name.LocalName);
        Assert.DoesNotContain(row.Descendants(), e => e.Name.LocalName == "WrapPanel");

        foreach (var name in new[] { "LookupClipButton", "ShareButton", "SetupHelpButton", "MoreButton", "CloseChromeButton" })
            Assert.True(Inside(Named(doc, name), "PeekHeaderRow"), name);
        foreach (var name in new[] { "OpenInAppButton", "EditModeButton", "TextEditButton" })
            Assert.True(Inside(Named(doc, name), "PeekPrimarySlot"), name);
    }

    [Fact]
    public void More_button_is_named_and_opens_a_popup_that_closes_on_any_click()
    {
        var doc = PreviewXaml();
        var more = Named(doc, "MoreButton");
        Assert.Equal("More", more.Attribute("AutomationProperties.Name")?.Value);
        var popup = Named(doc, "MorePopup");
        Assert.Equal("False", popup.Attribute("StaysOpen")?.Value);
        Assert.NotNull(popup.Descendants().FirstOrDefault(e => e.Attribute("ButtonBase.Click") is not null));
    }

    [Fact]
    public void Zoom_controls_sit_in_the_content_toolbar_not_the_header()
    {
        var doc = PreviewXaml();
        foreach (var name in new[] { "ZoomOutButton", "ZoomLabel", "ZoomInButton", "ZoomFitButton" })
        {
            var e = Named(doc, name);
            Assert.True(Inside(e, "PeekToolbar"), name);
            Assert.False(Inside(e, "PeekHeaderRow"), name);
        }
        Assert.Equal("Right", Named(doc, "ZoomStrip").Attribute("HorizontalAlignment")?.Value);
    }

    [Fact]
    public void Edit_is_primary_only_without_an_Open_in_Netie_action()
    {
        Assert.False(PeekChromeRules.EditIsPrimary(openInAppShown: true));
        Assert.True(PeekChromeRules.EditIsPrimary(openInAppShown: false));
    }

    // ---- 2. tooltips ----

    [Fact]
    public void Drag_hint_tooltip_has_a_short_show_duration()
    {
        var img = Named(PreviewXaml(), "PreviewImage");
        Assert.Equal("Drag out to copy the file", img.Attribute("ToolTip")?.Value);
        Assert.True(int.Parse(img.Attribute("ToolTipService.ShowDuration")!.Value) <= 4000);
    }

    [Fact]
    public void Window_gates_tooltips_until_the_pointer_moves()
    {
        var cs = File.ReadAllText(Repo("src", "NetieSpace", "Views", "PreviewWindow.xaml.cs"));
        Assert.Contains("ToolTipService.ToolTipOpeningEvent", cs);
        Assert.Contains("e.Handled = true", cs);
        Assert.Contains("ResetTooltipGuard();", cs);
    }

    // ---- 3. summary ----

    [Theory]
    [InlineData(PreviewKind.Image, false)]
    [InlineData(PreviewKind.Video, false)]
    [InlineData(PreviewKind.Pdf, true)]
    [InlineData(PreviewKind.Office, true)]
    [InlineData(PreviewKind.Markdown, true)]
    [InlineData(PreviewKind.Text, true)]
    public void Summary_strip_is_off_by_default_for_pictures_and_clips(PreviewKind kind, bool expected) =>
        Assert.Equal(expected, PeekChromeRules.SummaryByDefault(kind));

    [Fact]
    public void Summary_strip_is_two_lines()
    {
        Assert.Equal(2, PeekChromeRules.SummaryLines);
        var doc = PreviewXaml();
        var text = Named(doc, "SummaryText");
        // 2 lines x LineHeight 19 = 38.
        Assert.Equal("19", text.Attribute("LineHeight")?.Value);
        Assert.Equal("38", text.Attribute("MaxHeight")?.Value);
        Assert.Equal("CharacterEllipsis", text.Attribute("TextTrimming")?.Value);
    }

    // ---- 4. Fit ----

    [Theory]
    [InlineData(1200, 800, 64, 64, 1.0)]     // small picture: 100%, never 684%
    [InlineData(1200, 800, 4000, 3000, 0.2667)]
    [InlineData(1200, 800, 1200, 800, 1.0)]
    [InlineData(0, 0, 64, 64, 0.05)]         // pane not laid out yet: floor, not a crash
    public void Fit_shrinks_large_pictures_and_never_enlarges_small_ones(
        double vw, double vh, double iw, double ih, double expected) =>
        Assert.Equal(expected, PeekChromeRules.FitZoom(vw, vh, iw, ih), 3);

    [Fact]
    public void Zoom_label_shows_the_real_rounded_zoom()
    {
        Assert.Equal("100%", PeekChromeRules.ZoomText(1.0));
        Assert.Equal("27%", PeekChromeRules.ZoomText(0.2667));
        Assert.Equal("400%", PeekChromeRules.ZoomText(4.0));
    }

    // ---- 5. video diagnostics ----

    [Fact]
    public void Video_open_logs_what_MediaElement_reports()
    {
        var cs = File.ReadAllText(Repo("src", "NetieSpace", "Views", "PreviewWindow.xaml.cs"));
        Assert.Contains("[video] ", cs);
        Assert.Contains("NaturalVideoWidth", cs);
        Assert.Contains("LogVideoProbe(\"MediaFailed\"", cs);
        Assert.Contains("LogVideoProbe(\"opened\")", cs);
    }
}
