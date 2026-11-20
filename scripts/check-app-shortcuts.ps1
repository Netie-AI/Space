#Requires -Version 5.1
<#
.SYNOPSIS
  Independent reader for the Netie PDF / Image / Video / MD Start Menu shortcuts.
.DESCRIPTION
  Reads each .lnk back through its own COM interop (IShellLinkW, IPersistFile, IPropertyStore;
  read path only, no code shared with NetieSpace.exe) and prints one line per shortcut:
    OK <lnk> <aumid> <args>
  Exit 1 on a missing file or any mismatch (target, arguments, icon, AUMID).
.PARAMETER ProgramsDir
  Folder that holds the shortcuts. Default: the current user's Start Menu\Programs.
.PARAMETER ExePath
  Optional. When given, the shortcut target must equal this path; otherwise it must exist.
#>
param(
    [string]$ProgramsDir,
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'

if (-not $ProgramsDir) {
    $ProgramsDir = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs'
}

$source = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NetieCheck
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct PKey { public Guid Fmt; public uint Pid; }

    [StructLayout(LayoutKind.Sequential)]
    public struct PVar
    {
        public ushort Vt; public ushort R1; public ushort R2; public ushort R3;
        public IntPtr A; public IntPtr B;
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    public class ShellLinkObject { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ILink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder f, int cch, IntPtr fd, uint flags);
        void GetIDList(out IntPtr p);
        void SetIDList(IntPtr p);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder s, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string s);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder s, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string s);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder s, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string s);
        void GetHotkey(out short h);
        void SetHotkey(short h);
        void GetShowCmd(out int c);
        void SetShowCmd(int c);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder s, int cch, out int i);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string s, int i);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string s, uint r);
        void Resolve(IntPtr h, uint f);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string s);
    }

    [ComImport, Guid("0000010B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFile
    {
        void GetClassID(out Guid g);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string f, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string f, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string f);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string f);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IStore
    {
        void GetCount(out uint c);
        void GetAt(uint i, out PKey k);
        void GetValue(ref PKey k, out PVar v);
        void SetValue(ref PKey k, ref PVar v);
        void Commit();
    }

    public class LinkInfo
    {
        public string Target;
        public string Arguments;
        public string IconPath;
        public string Aumid;
    }

    public static class Reader
    {
        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PVar v);

        public static LinkInfo Read(string lnkPath)
        {
            ILink link = (ILink)new ShellLinkObject();
            try
            {
                ((IFile)link).Load(lnkPath, 0);
                var info = new LinkInfo();

                var sb = new StringBuilder(1024);
                link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                info.Target = sb.ToString();

                sb = new StringBuilder(1024);
                link.GetArguments(sb, sb.Capacity);
                info.Arguments = sb.ToString();

                sb = new StringBuilder(1024);
                int idx;
                link.GetIconLocation(sb, sb.Capacity, out idx);
                info.IconPath = sb.ToString();

                var key = new PKey();
                key.Fmt = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
                key.Pid = 5;
                PVar pv;
                ((IStore)link).GetValue(ref key, out pv);
                try
                {
                    // VT_LPWSTR = 31, VT_BSTR = 8; anything else (VT_EMPTY = 0) means no AUMID.
                    if (pv.Vt == 31 || pv.Vt == 8)
                        info.Aumid = Marshal.PtrToStringUni(pv.A);
                    else
                        info.Aumid = "";
                }
                finally
                {
                    PropVariantClear(ref pv);
                }
                return info;
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
    }
}
'@

if (-not ('NetieCheck.Reader' -as [type])) {
    Add-Type -TypeDefinition $source
}

$expected = @(
    @{ File = 'Netie PDF.lnk';   Aumid = 'Netie.Pdf';   Args = '--app pdf';   Icon = 'netie-pdf.ico' },
    @{ File = 'Netie Image.lnk'; Aumid = 'Netie.Image'; Args = '--app image'; Icon = 'netie-image.ico' },
    @{ File = 'Netie Video.lnk'; Aumid = 'Netie.Video'; Args = '--app video'; Icon = 'netie-video.ico' },
    @{ File = 'Netie MD.lnk';    Aumid = 'Netie.Md';    Args = '--app md';    Icon = 'netie-md.ico' }
)

$failed = 0
foreach ($e in $expected) {
    $lnk = Join-Path $ProgramsDir $e.File
    if (-not (Test-Path -LiteralPath $lnk)) {
        Write-Host "FAIL $lnk missing" -ForegroundColor Red
        $failed++
        continue
    }

    try {
        $info = [NetieCheck.Reader]::Read($lnk)
    } catch {
        Write-Host "FAIL $lnk unreadable: $($_.Exception.Message)" -ForegroundColor Red
        $failed++
        continue
    }

    $problems = @()
    if ($info.Aumid -cne $e.Aumid) { $problems += "aumid '$($info.Aumid)' expected '$($e.Aumid)'" }
    if ($info.Arguments -cne $e.Args) { $problems += "args '$($info.Arguments)' expected '$($e.Args)'" }
    if ($ExePath) {
        if ($info.Target -ine $ExePath) { $problems += "target '$($info.Target)' expected '$ExePath'" }
    } elseif (-not (Test-Path -LiteralPath $info.Target)) {
        $problems += "target '$($info.Target)' does not exist"
    }
    $iconWanted = Join-Path (Join-Path (Split-Path $info.Target -Parent) 'Assets\apps') $e.Icon
    if ($info.IconPath -ine $iconWanted) { $problems += "icon '$($info.IconPath)' expected '$iconWanted'" }
    elseif (-not (Test-Path -LiteralPath $info.IconPath)) { $problems += "icon file '$($info.IconPath)' does not exist" }

    if ($problems.Count -gt 0) {
        Write-Host "FAIL $lnk : $($problems -join '; ')" -ForegroundColor Red
        $failed++
    } else {
        Write-Host "OK $lnk $($info.Aumid) $($info.Arguments)"
    }
}

if ($failed -gt 0) {
    Write-Host "$failed of $($expected.Count) shortcuts failed" -ForegroundColor Red
    exit 1
}
exit 0
