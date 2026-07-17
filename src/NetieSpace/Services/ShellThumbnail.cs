using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>
/// Instant Shell / GDI thumbnail for first paint while Office COM / LO warms.
/// </summary>
public static class ShellThumbnail
{
    public static BitmapSource? TryGetBitmapSource(string path, int size = 512)
    {
        var hbm = TryGetShellHBitmap(path, size);
        if (hbm != IntPtr.Zero)
        {
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(
                    hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Shell HBITMAP → BitmapSource failed: {ex.Message}");
            }
            finally
            {
                DeleteObject(hbm);
            }
        }

        return TryExtractAssociatedIcon(path);
    }

    public static string? TryWritePng(string path, string destPng, int size = 512)
    {
        try
        {
            var bmp = TryGetBitmapSource(path, size);
            if (bmp is null)
                return null;

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            IoDirectory.CreateDirectory(IoPath.GetDirectoryName(destPng)!);
            using var fs = IoFile.Create(destPng);
            encoder.Save(fs);
            return destPng;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Shell thumbnail PNG write failed: {ex.Message}");
            return null;
        }
    }

    private static BitmapSource? TryExtractAssociatedIcon(string path)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null)
                return null;

            using var gdi = icon.ToBitmap();
            using var ms = new MemoryStream();
            gdi.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            return null;
        }
    }

    private static IntPtr TryGetShellHBitmap(string path, int size)
    {
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var unk);
            if (hr != 0 || unk is null)
                return IntPtr.Zero;

            try
            {
                if (unk is not IShellItemImageFactory factory)
                    return IntPtr.Zero;

                var sz = new SIZE { cx = size, cy = size };
                var hrImg = factory.GetImage(sz, SIIGBF.ResizeToFit | SIIGBF.BiggerSizeOk, out var hbm);
                return hrImg == 0 ? hbm : IntPtr.Zero;
            }
            finally
            {
                Marshal.ReleaseComObject(unk);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"IShellItemImageFactory failed: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [Flags]
    private enum SIIGBF
    {
        ResizeToFit = 0x00,
        BiggerSizeOk = 0x01,
        MemoryOnly = 0x02,
        IconOnly = 0x04,
        ThumbnailOnly = 0x08,
        InCacheOnly = 0x10
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }
}
