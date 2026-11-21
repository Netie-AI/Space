using System.Collections.Generic;

namespace NetieSpace.Apps.Tools.Catalog;

/// <summary>
/// Every engine the catalog names, with its licence. The four AGPL engines at the bottom are
/// banned from the shipping build; the licence gate refuses them even when they are installed.
/// </summary>
public static class Engines
{
    // In-process managed libraries: always present.
    public static readonly EngineRef PdfSharpCore = new("PdfSharpCore", EngineLicense.MIT, true, "inproc");
    public static readonly EngineRef PdfPig = new("PdfPig", EngineLicense.Apache2, true, "inproc");
    public static readonly EngineRef OpenXml = new("DocumentFormat.OpenXml", EngineLicense.MIT, true, "inproc");

    // Windows platform pieces.
    public static readonly EngineRef Wic = new("Windows imaging (WIC)", EngineLicense.InProcessManaged, false, "windows");
    public static readonly EngineRef WinRt = new("Windows runtime APIs", EngineLicense.WinRT, false, "winrt");
    // Finer keys: the same Windows runtime, but the thing a tool really needs (see Refinements).
    public static readonly EngineRef WinRtOcr = new("Windows OCR language pack", EngineLicense.WinRT, false, "winrt-ocr");
    public static readonly EngineRef WinRtSpeech = new("Windows speech voice", EngineLicense.WinRT, false, "winrt-speech");
    public static readonly EngineRef WebView2 = new("WebView2 runtime", EngineLicense.Proprietary, false, "webview2");
    public static readonly EngineRef OfficeWord = new("Word COM", EngineLicense.OfficeCom, false, "com:Word.Application");
    public static readonly EngineRef OfficeExcel = new("Excel COM", EngineLicense.OfficeCom, false, "com:Excel.Application");
    public static readonly EngineRef OfficePowerPoint = new("PowerPoint COM", EngineLicense.OfficeCom, false, "com:PowerPoint.Application");

    // Out-of-process binaries.
    public static readonly EngineRef Ffmpeg = new("FFmpeg", EngineLicense.GPL3OutOfProcess, true, "ffmpeg");
    public static readonly EngineRef FfmpegVidstab = new("FFmpeg filter vidstabdetect+vidstabtransform or deshake", EngineLicense.GPL3OutOfProcess, true, "ffmpeg-filters:vidstabdetect,vidstabtransform|deshake");
    public static readonly EngineRef FfmpegDrawtext = new("FFmpeg filter drawtext", EngineLicense.GPL3OutOfProcess, true, "ffmpeg-filters:drawtext");
    public static readonly EngineRef FfmpegSubtitles = new("FFmpeg filter subtitles", EngineLicense.GPL3OutOfProcess, true, "ffmpeg-filters:subtitles");
    public static readonly EngineRef Tesseract = new("Tesseract", EngineLicense.Apache2, false, "tesseract");
    public static readonly EngineRef Whisper = new("whisper.cpp", EngineLicense.MIT, false, "whisper");
    public static readonly EngineRef VeraPdf = new("veraPDF", EngineLicense.MPL2, false, "path:verapdf");
    public static readonly EngineRef RealEsrgan = new("Real-ESRGAN (ONNX Runtime)", EngineLicense.MIT, false, "file:tools/models/realesrgan-x4.onnx|Real-ESRGAN model");

    // Python wheels (non-AGPL). Probe: python runtime first, then the named module.
    public static readonly EngineRef PikePdf = new("pikepdf", EngineLicense.MPL2, false, "python:pikepdf");
    public static readonly EngineRef OcrMyPdf = new("ocrmypdf", EngineLicense.MPL2, false, "python:ocrmypdf");
    public static readonly EngineRef PythonPptx = new("python-pptx", EngineLicense.MIT, false, "python:pptx");
    public static readonly EngineRef MarkItDown = new("markitdown", EngineLicense.MIT, false, "python:markitdown");
    public static readonly EngineRef Rembg = new("rembg", EngineLicense.MIT, false, "python:rembg");

    // Not local.
    public static readonly EngineRef CloudLlm = new("cloud LLM", EngineLicense.Proprietary, false, "cloud");
    public static readonly EngineRef HostedSigner = new("hosted signer portal", EngineLicense.Proprietary, false, "cloud");

    // Banned in the shipping build (AGPL). Present or not, the gate refuses them.
    public static readonly EngineRef PyMuPdf = new("PyMuPDF", EngineLicense.AGPL, false, "python:fitz");
    public static readonly EngineRef PyMuPdf4Llm = new("pymupdf4llm", EngineLicense.AGPL, false, "python:pymupdf4llm");
    public static readonly EngineRef Ghostscript = new("Ghostscript", EngineLicense.AGPL, false, "path:gswin64c");
    public static readonly EngineRef OnlyOffice = new("OnlyOffice", EngineLicense.AGPL, false, "path:DocumentBuilder");

    public static IReadOnlyList<EngineRef> All { get; } = new[]
    {
        PdfSharpCore, PdfPig, OpenXml, Wic, WinRt, WinRtOcr, WinRtSpeech, WebView2, OfficeWord, OfficeExcel, OfficePowerPoint,
        Ffmpeg, FfmpegVidstab, FfmpegDrawtext, FfmpegSubtitles, Tesseract, Whisper, VeraPdf, RealEsrgan, PikePdf, OcrMyPdf, PythonPptx, MarkItDown, Rembg,
        CloudLlm, HostedSigner, PyMuPdf, PyMuPdf4Llm, Ghostscript, OnlyOffice,
    };

    /// <summary>
    /// Tools whose catalog row names a coarser engine than they really need. The registry uses the finer
    /// engine for availability, list-tools and the doctor. When a catalog row is edited to name the
    /// finer engine itself, the entry here is a no-op and can be deleted.
    /// </summary>
    public static IReadOnlyDictionary<string, EngineRef> Refinements { get; } = new Dictionary<string, EngineRef>
    {
        ["pdf-ocr"] = WinRtOcr,
        ["vid-tts"] = WinRtSpeech,
        ["vid-stabilize"] = FfmpegVidstab,
        ["vid-add-text"] = FfmpegDrawtext,
        ["vid-subtitles"] = FfmpegSubtitles,
    };

    public static EngineRef EffectiveFor(ToolEntry entry) =>
        Refinements.TryGetValue(entry.Id, out var e) ? e : entry.Engine;
}
