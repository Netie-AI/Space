using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32;
using NetieSpace.Helpers;

namespace NetieSpace.Services;

/// <summary>
/// Resolve and create Windows IPreviewHandler instances (Explorer preview pane).
/// Space glass is AllowsTransparency — callers must host the HWND in an opaque overlay,
/// not inside PreviewWindow itself.
/// </summary>
public static class PreviewHandlerService
{
    public static readonly Guid HandlerShellEx = new("8895b1c6-b41f-4c1c-a562-0d564250836f");
    private const uint StgmRead = 0x00000000;
    private const uint StgmShareDenyNone = 0x00000040;

    public static bool IsDisabled
    {
        get
        {
            var v = Environment.GetEnvironmentVariable("NETIE_SPACE_PREVIEW_HANDLER");
            return v is "0" or "false" or "FALSE" or "off" or "OFF";
        }
    }

    public static bool TryParseClsid(string? raw, out Guid clsid)
    {
        clsid = Guid.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        raw = raw.Trim().Trim('{', '}');
        return Guid.TryParse(raw, out clsid) && clsid != Guid.Empty;
    }

    /// <summary>HKCR lookup for an extension (include the dot). ProgId fallback.</summary>
    public static bool TryFindClsid(string extension, out Guid clsid)
    {
        clsid = Guid.Empty;
        if (string.IsNullOrWhiteSpace(extension) || IsDisabled)
            return false;
        if (!extension.StartsWith('.'))
            extension = "." + extension;

        var keyPath = extension + @"\shellex\{" + HandlerShellEx + "}";
        if (TryReadHkcrDefault(keyPath, out clsid))
            return true;

        var progId = ReadHkcrDefault(extension);
        if (string.IsNullOrWhiteSpace(progId))
            return false;
        keyPath = progId + @"\shellex\{" + HandlerShellEx + "}";
        return TryReadHkcrDefault(keyPath, out clsid);
    }

    public static object? CreateHandler(Guid clsid)
    {
        try
        {
            var type = Type.GetTypeFromCLSID(clsid, throwOnError: false);
            if (type is null)
                return null;
            return Activator.CreateInstance(type);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"IPreviewHandler create failed: {ex.Message}");
            return null;
        }
    }

    public static bool TryInitialize(object handler, string filePath)
    {
        try
        {
            if (handler is IInitializeWithFile withFile)
            {
                withFile.Initialize(filePath, StgmRead);
                return true;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"IInitializeWithFile: {ex.Message}");
        }

        try
        {
            if (handler is IInitializeWithStream withStream)
            {
                var hr = SHCreateStreamOnFileEx(
                    filePath, StgmRead | StgmShareDenyNone, 0, false, IntPtr.Zero, out var stream);
                if (hr != 0 || stream is null)
                    return false;
                withStream.Initialize(stream, StgmRead);
                return true;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"IInitializeWithStream: {ex.Message}");
        }

        return false;
    }

    private static bool TryReadHkcrDefault(string subKey, out Guid clsid)
    {
        clsid = Guid.Empty;
        var raw = ReadHkcrDefault(subKey);
        return TryParseClsid(raw, out clsid);
    }

    private static string? ReadHkcrDefault(string subKey)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(subKey);
            return key?.GetValue(null) as string;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateStreamOnFileEx(
        string pszFile,
        uint grfMode,
        uint dwAttributes,
        bool fCreate,
        IntPtr pstmTemplate,
        out IStream ppstm);

    [ComImport]
    [Guid("8895b1c6-b41f-4c1c-a562-0d564250836f")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPreviewHandler
    {
        void SetWindow(IntPtr hwnd, ref RECT prc);
        void SetRect(ref RECT prc);
        void DoPreview();
        void Unload();
        void SetFocus();
        void QueryFocus(out IntPtr phwnd);
        [PreserveSig]
        uint TranslateAccelerator(ref MSG pmsg);
    }

    [ComImport]
    [Guid("b7d14566-0509-4cce-a71f-0a554233bd9b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithFile
    {
        void Initialize([MarshalAs(UnmanagedType.LPWStr)] string pszFilePath, uint grfMode);
    }

    [ComImport]
    [Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInitializeWithStream
    {
        void Initialize(IStream pstream, uint grfMode);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }
}
