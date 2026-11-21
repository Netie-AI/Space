using System;
using System.Linq;

namespace NetieSpace.Apps.Tools.WinRt;

/// <summary>Real Windows probes. Windows only; found by reflection from <see cref="PlatformProbes"/>.</summary>
public sealed class WinRtProbes : IWinRtProbes
{
    public (bool Ok, string Detail) Ocr()
    {
        var langs = global::Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages;
        return langs.Count > 0 ? (true, "") : (false, "Windows OCR language pack");
    }

    public (bool Ok, string Detail) Speech()
    {
        var voices = global::Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices;
        return voices.Count > 0 ? (true, "") : (false, "Windows speech voice");
    }

    public (bool Ok, string Detail) WebView2()
    {
        try
        {
            var v = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
            return string.IsNullOrWhiteSpace(v) ? (false, "WebView2 runtime") : (true, "");
        }
        catch (Exception)
        {
            return (false, "WebView2 runtime");
        }
    }
}
