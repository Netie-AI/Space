using System;

namespace NetieSpace.Apps.Pdf;

/// <summary>
/// "Is this page blank": decided from the pixels of its thumbnail, so a page that is only a white fill, a
/// scanner's grey paper tone or a few specks reads as blank, and a page with a line of text does not.
/// </summary>
public static class PdfBlankPage
{
    /// <summary>A pixel darker than this (0..255 luminance, against a paper estimate) counts as ink.</summary>
    public const int InkDelta = 48;

    /// <summary>Blank when at most this share of pixels is ink (0.02 percent: a dust speck, never a word: a false "blank" would lead to deleting a page).</summary>
    public const double MaxInkShare = 0.0002;

    /// <summary>
    /// <paramref name="bgra"/> is Bgra32, <paramref name="stride"/> bytes per row. The paper tone is the brightest
    /// common level (95th percentile of luminance), so a grey scan is judged against itself. Edges (a scanner's
    /// shadow) are ignored: a 3 percent margin on every side.
    /// </summary>
    public static bool IsBlank(byte[] bgra, int width, int height, int stride)
    {
        if (width < 8 || height < 8 || bgra.Length < stride * height) return false; // unknown is not blank
        var mx = Math.Max(1, width * 3 / 100);
        var my = Math.Max(1, height * 3 / 100);
        var hist = new int[256];
        var total = 0;
        for (var y = my; y < height - my; y++)
        {
            var row = y * stride;
            for (var x = mx; x < width - mx; x++)
            {
                hist[Luma(bgra, row + x * 4)]++;
                total++;
            }
        }
        if (total == 0) return false;

        var cut = (int)(total * 0.95);
        var run = 0;
        var paper = 255;
        for (var l = 0; l < 256; l++)
        {
            run += hist[l];
            if (run >= cut) { paper = l; break; }
        }
        if (paper < 150) return false; // a dark page is a picture, not a blank
        var threshold = paper - InkDelta;
        var ink = 0;
        for (var l = 0; l < 256 && l < threshold; l++) ink += hist[l];
        return ink <= total * MaxInkShare;
    }

    private static int Luma(byte[] p, int i) => (p[i + 2] * 299 + p[i + 1] * 587 + p[i] * 114) / 1000;
}
