using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace NetieSpace.Apps.Pdf.WinRt;

/// <summary>
/// Writes a .pptx whose slides each hold one full-slide picture. Pure Open XML, no WinRT: the part layout
/// (presentation, master, one blank layout, theme, slides, images) is written by hand so the file opens in
/// PowerPoint without repair. Dispose finishes the package; a deck that was not disposed is not a file.
/// </summary>
public sealed class PptxDeck : IDisposable
{
    public const long EmuPerInch = 914400;
    private const long MinSlideEmu = EmuPerInch;          // PowerPoint refuses slides under 1 inch
    private const long MaxSlideEmu = 56 * EmuPerInch;     // and over 56 inches

    private const string NsA = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string NsR = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string NsP = "http://schemas.openxmlformats.org/presentationml/2006/main";

    private readonly PresentationDocument _doc;
    private readonly PresentationPart _presentation;
    private readonly SlideLayoutPart _layout;
    private readonly List<string> _slideRelIds = new();
    private readonly string _masterRelId;
    private bool _finished;

    public long SlideWidthEmu { get; }
    public long SlideHeightEmu { get; }
    public int SlideCount => _slideRelIds.Count;

    /// <summary>Slide size in EMU for a page of the given size in 1/96 inch units, clamped to what PowerPoint allows.</summary>
    public static (long Width, long Height) SlideSizeFor(double pageWidth96, double pageHeight96)
    {
        if (!(pageWidth96 > 0) || !(pageHeight96 > 0) || double.IsInfinity(pageWidth96) || double.IsInfinity(pageHeight96))
            throw new ArgumentOutOfRangeException(nameof(pageWidth96), "page size must be positive");
        var w = pageWidth96 / 96.0 * EmuPerInch;
        var h = pageHeight96 / 96.0 * EmuPerInch;
        var longest = Math.Max(w, h);
        var shortest = Math.Min(w, h);
        var scale = 1.0;
        if (longest > MaxSlideEmu) scale = MaxSlideEmu / longest;
        else if (shortest < MinSlideEmu) scale = MinSlideEmu / shortest;
        w *= scale;
        h *= scale;
        // Scaling up a very thin page could overshoot the maximum; the maximum wins.
        if (Math.Max(w, h) > MaxSlideEmu)
        {
            var s2 = MaxSlideEmu / Math.Max(w, h);
            w *= s2;
            h *= s2;
        }
        // A very thin page cannot keep its ratio inside 1..56 in on both sides; the picture is fitted and
        // centred, so only the slide ratio gives way.
        w = Math.Min(Math.Max(w, MinSlideEmu), MaxSlideEmu);
        h = Math.Min(Math.Max(h, MinSlideEmu), MaxSlideEmu);
        return ((long)Math.Round(w), (long)Math.Round(h));
    }

    public PptxDeck(string path, long slideWidthEmu, long slideHeightEmu)
    {
        if (slideWidthEmu < MinSlideEmu || slideHeightEmu < MinSlideEmu
            || slideWidthEmu > MaxSlideEmu || slideHeightEmu > MaxSlideEmu)
            throw new ArgumentOutOfRangeException(nameof(slideWidthEmu), "slide size must be between 1 and 56 inches");
        SlideWidthEmu = slideWidthEmu;
        SlideHeightEmu = slideHeightEmu;

        _doc = PresentationDocument.Create(path, DocumentFormat.OpenXml.PresentationDocumentType.Presentation);
        try
        {
            _presentation = _doc.AddPresentationPart();

            var master = _presentation.AddNewPart<SlideMasterPart>();
            var theme = master.AddNewPart<ThemePart>();
            theme.Theme = new A.Theme(ThemeXml);
            _presentation.AddPart(theme);

            _layout = master.AddNewPart<SlideLayoutPart>();
            _layout.SlideLayout = new P.SlideLayout(LayoutXml);
            _layout.AddPart(master);

            master.SlideMaster = new P.SlideMaster(MasterXml(master.GetIdOfPart(_layout)));
            _masterRelId = _presentation.GetIdOfPart(master);
        }
        catch
        {
            _doc.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Adds a slide holding the picture, scaled to fit the slide and centred. <paramref name="png"/> says
    /// whether the bytes are PNG (otherwise JPEG); the signature is checked so a mislabelled stream fails here.
    /// </summary>
    public void AddPicture(Stream image, bool png, int pixelWidth, int pixelHeight, string name)
    {
        if (_finished) throw new InvalidOperationException("the deck is already finished");
        if (pixelWidth <= 0 || pixelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), "picture size must be positive");

        var slide = _presentation.AddNewPart<SlidePart>();
        slide.AddPart(_layout);
        var imagePart = slide.AddImagePart(png ? ImagePartType.Png : ImagePartType.Jpeg);
        imagePart.FeedData(image);
        CheckSignature(imagePart, png);

        // Fit inside the slide, keep the aspect ratio, centre.
        var scale = Math.Min(SlideWidthEmu / (double)pixelWidth, SlideHeightEmu / (double)pixelHeight);
        var cx = (long)Math.Round(pixelWidth * scale);
        var cy = (long)Math.Round(pixelHeight * scale);
        var x = (SlideWidthEmu - cx) / 2;
        var y = (SlideHeightEmu - cy) / 2;

        var label = System.Security.SecurityElement.Escape(XmlSafe(name)) ?? "";
        var rel = slide.GetIdOfPart(imagePart);
        slide.Slide = new P.Slide(
            $"<p:sld xmlns:a=\"{NsA}\" xmlns:r=\"{NsR}\" xmlns:p=\"{NsP}\"><p:cSld><p:spTree>"
            + "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>"
            + "<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>"
            + $"<p:pic><p:nvPicPr><p:cNvPr id=\"2\" name=\"{label}\" descr=\"{label}\"/><p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>"
            + $"<p:blipFill><a:blip r:embed=\"{rel}\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>"
            + $"<p:spPr><a:xfrm><a:off x=\"{Inv(x)}\" y=\"{Inv(y)}\"/><a:ext cx=\"{Inv(cx)}\" cy=\"{Inv(cy)}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>"
            + "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>");
        _slideRelIds.Add(_presentation.GetIdOfPart(slide));
    }

    /// <summary>
    /// Text that is safe inside an XML 1.0 attribute: control characters, lone surrogates and U+FFFE / U+FFFF are
    /// dropped (a file name can carry any of them on Windows); a proper surrogate pair (an emoji) stays.
    /// </summary>
    internal static string XmlSafe(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var sb = new System.Text.StringBuilder(name.Length);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsHighSurrogate(c) && i + 1 < name.Length && char.IsLowSurrogate(name[i + 1]))
            {
                sb.Append(c).Append(name[i + 1]);
                i++;
            }
            else if (c >= ' ' && System.Xml.XmlConvert.IsXmlChar(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string Inv(long v) => v.ToString(CultureInfo.InvariantCulture);

    private static void CheckSignature(ImagePart part, bool png)
    {
        using var s = part.GetStream(FileMode.Open, FileAccess.Read);
        var head = new byte[4];
        var n = s.Read(head, 0, 4);
        var ok = png
            ? n == 4 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47
            : n >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF;
        if (!ok)
            throw new InvalidDataException("picture bytes are not a " + (png ? "PNG" : "JPEG") + " image");
    }

    /// <summary>Writes the presentation part and closes the package.</summary>
    public void Dispose()
    {
        if (!_finished)
        {
            _finished = true;
            var ids = new System.Text.StringBuilder();
            for (var i = 0; i < _slideRelIds.Count; i++)
                ids.Append("<p:sldId id=\"").Append(256 + i).Append("\" r:id=\"").Append(_slideRelIds[i]).Append("\"/>");
            _presentation.Presentation = new P.Presentation(
                $"<p:presentation xmlns:a=\"{NsA}\" xmlns:r=\"{NsR}\" xmlns:p=\"{NsP}\">"
                + $"<p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"{_masterRelId}\"/></p:sldMasterIdLst>"
                + (_slideRelIds.Count > 0 ? "<p:sldIdLst>" + ids + "</p:sldIdLst>" : "")
                + $"<p:sldSz cx=\"{Inv(SlideWidthEmu)}\" cy=\"{Inv(SlideHeightEmu)}\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/>"
                + "</p:presentation>");
            _presentation.Presentation.Save();
        }
        _doc.Dispose();
    }

    private const string GroupHeader =
        "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>"
        + "<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>";

    private const string LayoutXml =
        "<p:sldLayout xmlns:a=\"" + NsA + "\" xmlns:r=\"" + NsR + "\" xmlns:p=\"" + NsP + "\" type=\"blank\" preserve=\"1\">"
        + "<p:cSld name=\"Blank\"><p:spTree>" + GroupHeader + "</p:spTree></p:cSld>"
        + "<p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>";

    private static string MasterXml(string layoutRelId) =>
        "<p:sldMaster xmlns:a=\"" + NsA + "\" xmlns:r=\"" + NsR + "\" xmlns:p=\"" + NsP + "\">"
        + "<p:cSld><p:bg><p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef></p:bg><p:spTree>" + GroupHeader + "</p:spTree></p:cSld>"
        + "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>"
        + "<p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"" + layoutRelId + "\"/></p:sldLayoutIdLst>"
        + "<p:txStyles><p:titleStyle/><p:bodyStyle/><p:otherStyle/></p:txStyles></p:sldMaster>";

    private const string Fill = "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>";
    private const string Line = "<a:ln w=\"6350\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln>";

    private const string ThemeXml =
        "<a:theme xmlns:a=\"" + NsA + "\" name=\"Netie\"><a:themeElements>"
        + "<a:clrScheme name=\"Netie\">"
        + "<a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1><a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>"
        + "<a:dk2><a:srgbClr val=\"1F2937\"/></a:dk2><a:lt2><a:srgbClr val=\"F3F4F6\"/></a:lt2>"
        + "<a:accent1><a:srgbClr val=\"2563EB\"/></a:accent1><a:accent2><a:srgbClr val=\"7C3AED\"/></a:accent2>"
        + "<a:accent3><a:srgbClr val=\"059669\"/></a:accent3><a:accent4><a:srgbClr val=\"D97706\"/></a:accent4>"
        + "<a:accent5><a:srgbClr val=\"DC2626\"/></a:accent5><a:accent6><a:srgbClr val=\"0891B2\"/></a:accent6>"
        + "<a:hlink><a:srgbClr val=\"2563EB\"/></a:hlink><a:folHlink><a:srgbClr val=\"7C3AED\"/></a:folHlink></a:clrScheme>"
        + "<a:fontScheme name=\"Netie\"><a:majorFont><a:latin typeface=\"Calibri\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>"
        + "<a:minorFont><a:latin typeface=\"Calibri\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont></a:fontScheme>"
        + "<a:fmtScheme name=\"Netie\">"
        + "<a:fillStyleLst>" + Fill + Fill + Fill + "</a:fillStyleLst>"
        + "<a:lnStyleLst>" + Line + Line + Line + "</a:lnStyleLst>"
        + "<a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst>"
        + "<a:bgFillStyleLst>" + Fill + Fill + Fill + "</a:bgFillStyleLst>"
        + "</a:fmtScheme></a:themeElements></a:theme>";
}
