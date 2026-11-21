using System;
using System.Linq;

namespace NetieSpace.Apps.Image;

/// <summary>A rectangle as fractions (0 to 1) of the picture: left, top, width, height. Independent of how large it is drawn.</summary>
public readonly record struct CropRect(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
}

public enum CropHandle { None, Move, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left }

/// <summary>
/// The maths of the draggable crop box: the starting box for a shape, moving it, dragging a handle with or without a
/// fixed shape, and which part of the box a pointer is on. Pure, so the drag is tested without a window.
/// </summary>
public static class CropBox
{
    public static readonly string[] Shapes = { "free", "1:1", "4:5", "16:9" };

    /// <summary>The shape's width / height, null for "free".</summary>
    public static double? Ratio(string shape) => ImageGeometry.ParseAspect(shape);

    /// <summary>The largest centred box of the shape; for "free", the picture with a tenth left on every side.</summary>
    public static CropRect Initial(int imgW, int imgH, string shape)
    {
        if (Ratio(shape) is not { } r)
            return new CropRect(0.1, 0.1, 0.8, 0.8);
        double w = imgW, h = w / r;
        if (h > imgH) { h = imgH; w = h * r; }
        return new CropRect((imgW - w) / 2 / imgW, (imgH - h) / 2 / imgH, w / imgW, h / imgH);
    }

    /// <summary>The same box in a new shape: same centre and width share, kept inside the picture.</summary>
    public static CropRect Refit(CropRect box, int imgW, int imgH, string shape)
    {
        if (Ratio(shape) is not { } r)
            return box;
        var (x, y, w, h) = ImageGeometry.CropFromBox(imgW, imgH, box.X, box.Y, box.W, box.H, r);
        return new CropRect((double)x / imgW, (double)y / imgH, (double)w / imgW, (double)h / imgH);
    }

    /// <summary>The pixel rectangle of a box on a picture of this size, always inside it.</summary>
    public static (int X, int Y, int W, int H) ToPixels(CropRect box, int imgW, int imgH, string shape) =>
        ImageGeometry.CropFromBox(imgW, imgH, Math.Clamp(box.X, 0, 1), Math.Clamp(box.Y, 0, 1),
            Math.Clamp(box.W, 1e-6, 1), Math.Clamp(box.H, 1e-6, 1), Ratio(shape));

    /// <summary>
    /// The text the "box" option carries. A fixed shape is "centre x, centre y, zoom" so one box fits photos of any proportions;
    /// a free box is its four fractions.
    /// </summary>
    public static string ToOption(CropRect box, int imgW, int imgH, string shape)
    {
        string F(double v) => v.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);
        if (Ratio(shape) is not { } r)
            return string.Join(",", new[] { box.X, box.Y, box.W, box.H }.Select(F));
        var largestW = Math.Min((double)imgW, imgH * r);
        var zoom = Math.Clamp(box.W * imgW / largestW, 1e-4, 1.0);
        return string.Join(",", F(box.X + box.W / 2), F(box.Y + box.H / 2), F(zoom));
    }

    /// <summary>Slides the whole box by a fraction of the picture, stopping at the edges.</summary>
    public static CropRect Move(CropRect box, double dx, double dy) =>
        box with
        {
            X = Math.Clamp(box.X + dx, 0, 1 - box.W),
            Y = Math.Clamp(box.Y + dy, 0, 1 - box.H),
        };

    /// <summary>
    /// Drags one handle by (dx, dy), fractions of the picture. With a shape, the box keeps it: a corner pulls the
    /// opposite corner's anchor, an edge scales about the middle of the opposite edge. Never leaves the picture and
    /// never gets smaller than <paramref name="minPx"/> on its short side.
    /// </summary>
    public static CropRect Resize(CropRect box, CropHandle handle, double dx, double dy, int imgW, int imgH, string shape, int minPx = 24)
    {
        if (handle == CropHandle.Move) return Move(box, dx, dy);
        if (handle == CropHandle.None) return box;

        double W = imgW, H = imgH;
        double left = box.X * W, top = box.Y * H, right = box.Right * W, bottom = box.Bottom * H;
        double dxp = dx * W, dyp = dy * H;
        var ratio = Ratio(shape);

        if (ratio is not { } r)
        {
            if (handle is CropHandle.TopLeft or CropHandle.Left or CropHandle.BottomLeft) left += dxp;
            if (handle is CropHandle.TopRight or CropHandle.Right or CropHandle.BottomRight) right += dxp;
            if (handle is CropHandle.TopLeft or CropHandle.Top or CropHandle.TopRight) top += dyp;
            if (handle is CropHandle.BottomLeft or CropHandle.Bottom or CropHandle.BottomRight) bottom += dyp;
            left = Math.Clamp(left, 0, W); right = Math.Clamp(right, 0, W);
            top = Math.Clamp(top, 0, H); bottom = Math.Clamp(bottom, 0, H);
            if (right - left < minPx)
            {
                if (handle is CropHandle.TopLeft or CropHandle.Left or CropHandle.BottomLeft) left = Math.Max(0, right - minPx);
                else right = Math.Min(W, left + minPx);
            }
            if (bottom - top < minPx)
            {
                if (handle is CropHandle.TopLeft or CropHandle.Top or CropHandle.TopRight) top = Math.Max(0, bottom - minPx);
                else bottom = Math.Min(H, top + minPx);
            }
            return new CropRect(left / W, top / H, (right - left) / W, (bottom - top) / H);
        }

        var minW = Math.Max(minPx, minPx * r);
        if (handle is CropHandle.TopLeft or CropHandle.TopRight or CropHandle.BottomLeft or CropHandle.BottomRight)
        {
            var isLeft = handle is CropHandle.TopLeft or CropHandle.BottomLeft;
            var isTop = handle is CropHandle.TopLeft or CropHandle.TopRight;
            var anchorX = isLeft ? right : left;
            var anchorY = isTop ? bottom : top;
            var movedX = (isLeft ? left : right) + dxp;
            var movedY = (isTop ? top : bottom) + dyp;
            var dirX = isLeft ? -1 : 1;
            var dirY = isTop ? -1 : 1;
            // The side the handle started on is the only side the box can grow toward; crossing the anchor holds at the minimum.
            var w = Math.Max(0, (movedX - anchorX) * dirX);
            var h = Math.Max(0, (movedY - anchorY) * dirY);
            w = Math.Max(w, h * r);
            var availW = dirX < 0 ? anchorX : W - anchorX;
            var availH = dirY < 0 ? anchorY : H - anchorY;
            w = Math.Max(w, minW);
            w = Math.Min(w, Math.Min(availW, availH * r));
            var hh = w / r;
            var l = dirX < 0 ? anchorX - w : anchorX;
            var t = dirY < 0 ? anchorY - hh : anchorY;
            return new CropRect(l / W, t / H, w / W, hh / H);
        }

        // An edge: scale about the middle of the opposite edge, keeping the other axis centred on the old centre.
        var cx = (left + right) / 2;
        var cy = (top + bottom) / 2;
        double nw, nh;
        if (handle is CropHandle.Left or CropHandle.Right)
        {
            var edge = handle == CropHandle.Left ? left + dxp : right + dxp;
            nw = handle == CropHandle.Left ? right - edge : edge - left;
            nw = Math.Max(nw, minW);
            var maxW = Math.Min(handle == CropHandle.Left ? right : W - left, 2 * Math.Min(cy, H - cy) * r);
            nw = Math.Min(nw, maxW);
            nh = nw / r;
            var l = handle == CropHandle.Left ? right - nw : left;
            return new CropRect(l / W, (cy - nh / 2) / H, nw / W, nh / H);
        }
        else
        {
            var edge = handle == CropHandle.Top ? top + dyp : bottom + dyp;
            nh = handle == CropHandle.Top ? bottom - edge : edge - top;
            nh = Math.Max(nh, minW / r);
            var maxH = Math.Min(handle == CropHandle.Top ? bottom : H - top, 2 * Math.Min(cx, W - cx) / r);
            nh = Math.Min(nh, maxH);
            nw = nh * r;
            var t = handle == CropHandle.Top ? bottom - nh : top;
            return new CropRect((cx - nw / 2) / W, t / H, nw / W, nh / H);
        }
    }

    /// <summary>
    /// What a pointer at (px, py) is on, in the units the box is drawn in. <paramref name="box"/> is the drawn rectangle,
    /// <paramref name="grab"/> the half-size of a handle. A shape shows only its corners as handles.
    /// </summary>
    public static CropHandle HitTest(double boxX, double boxY, double boxW, double boxH, double px, double py, double grab, bool cornersOnly)
    {
        bool NearX(double x) => Math.Abs(px - x) <= grab;
        bool NearY(double y) => Math.Abs(py - y) <= grab;
        var l = boxX; var r = boxX + boxW; var t = boxY; var b = boxY + boxH;
        var midX = (l + r) / 2; var midY = (t + b) / 2;
        if (NearX(l) && NearY(t)) return CropHandle.TopLeft;
        if (NearX(r) && NearY(t)) return CropHandle.TopRight;
        if (NearX(l) && NearY(b)) return CropHandle.BottomLeft;
        if (NearX(r) && NearY(b)) return CropHandle.BottomRight;
        if (!cornersOnly)
        {
            if (NearX(midX) && NearY(t)) return CropHandle.Top;
            if (NearX(midX) && NearY(b)) return CropHandle.Bottom;
            if (NearX(l) && NearY(midY)) return CropHandle.Left;
            if (NearX(r) && NearY(midY)) return CropHandle.Right;
        }
        return px >= l && px <= r && py >= t && py <= b ? CropHandle.Move : CropHandle.None;
    }
}
