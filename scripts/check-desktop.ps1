# Drives the real windows of a built NetieSpace.exe on an interactive Windows desktop and
# records what a person would see: full-screen PNGs, peek timings, the buttons visible in each
# peek, and assertions on the Netie PDF / Netie Image app processes.
# Works under Windows PowerShell 5.1 and pwsh. Exit 1 on any failed assertion.
# Nothing is faked: a missing desktop or WebView2 runtime is reported by name and fails the run.
param(
    [string]$Exe = "",
    [string]$Out = (Join-Path $env:TEMP "s50-desktop")
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Exe) {
    $found = Get-ChildItem -Path (Join-Path $root "src\NetieSpace\bin") -Recurse -Filter NetieSpace.exe -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $found) { Write-Host "FAIL NetieSpace.exe not built under src\NetieSpace\bin"; exit 1 }
    $Exe = $found.FullName
}
New-Item -ItemType Directory -Force -Path $Out | Out-Null
Write-Host "exe: $Exe"
Write-Host "out: $Out"
$script:failed = 0
function Check([string]$Name, [bool]$Ok, [string]$Detail) {
    if ($Ok) { Write-Host "OK   $Name $Detail" }
    else { Write-Host "FAIL $Name $Detail"; $script:failed++ }
}

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class DeskNative {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
"@
[void][DeskNative]::SetProcessDPIAware()

# 1. Display and environment.
if (Get-Command Set-DisplayResolution -ErrorAction SilentlyContinue) {
    try { Set-DisplayResolution -Width 1920 -Height 1080 -Force | Out-Null; Write-Host "display set to 1920x1080" }
    catch { Write-Host "note: Set-DisplayResolution failed: $($_.Exception.Message)" }
} else {
    Write-Host "note: Set-DisplayResolution not available; keeping the runner's display"
}
$env:NETIE_SKIP_LICENSE = "1"
$sw = [DeskNative]::GetSystemMetrics(0); $sh = [DeskNative]::GetSystemMetrics(1)
Write-Host "screen: ${sw}x${sh} interactive=$([Environment]::UserInteractive)"
Check "interactive desktop" ([Environment]::UserInteractive -and $sw -gt 0 -and $sh -gt 0) "screen=${sw}x${sh}"
$wv = $false
foreach ($k in @("HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                 "HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
                 "HKCU:\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}")) {
    if (Test-Path $k) { $wv = $true; Write-Host "webview2 runtime: $((Get-ItemProperty $k).pv)" }
}
Check "WebView2 runtime present" $wv "(missing means every WebView2 pane renders blank)"

# Helpers ------------------------------------------------------------------------------------
function Save-Screen([string]$Name) {
    $path = Join-Path $Out $Name
    $w = [DeskNative]::GetSystemMetrics(0); $h = [DeskNative]::GetSystemMetrics(1)
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try { $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size) } finally { $g.Dispose() }
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $path
}

function Find-Window([string]$Title, [int[]]$Pids) {
    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $Title
    $hits = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
    foreach ($e in $hits) {
        if ((-not $Pids) -or ($Pids -contains $e.Current.ProcessId)) { return $e }
    }
    return $null
}

function Wait-Window([string]$Title, [int[]]$Pids, [int]$TimeoutSec = 30) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $w = Find-Window $Title $Pids
        if ($w) { return $w }
        Start-Sleep -Milliseconds 100
    }
    return $null
}

function Get-Buttons($Win) {
    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Button)
    $names = @()
    foreach ($b in $Win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        try {
            if ($b.Current.IsOffscreen) { continue }
            $n = [string]$b.Current.Name
            if ($n.Trim()) { $names += $n.Trim() }
        } catch { }
    }
    return ,$names
}

function Find-Button($Win, [string]$Pattern, [switch]$Exact) {
    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Button)
    foreach ($b in $Win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        try {
            $n = ([string]$b.Current.Name).Trim()
            if ($Exact) { if ($n -eq $Pattern) { return $b } }
            elseif ($n -like $Pattern) { return $b }
        } catch { }
    }
    return $null
}

# A tile is a Button whose content is a Grid, so it carries no name of its own: find the text
# named $Text, then walk up the control tree to the Button that holds it.
function Find-ButtonByText($Win, [string]$Text) {
    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $Text
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    foreach ($t in $Win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        $e = $t
        for ($i = 0; $i -lt 6 -and $e; $i++) {
            if ($e.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) { return $e }
            $e = $walker.GetParent($e)
        }
    }
    return $null
}

function Wait-ButtonByText($Win, [string]$Text, [int]$TimeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $b = Find-ButtonByText $Win $Text
        if ($b) { return $b }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

# First-run consent windows of Space (local memory, cloud disclosure) sit on top of everything.
# Screenshots are taken first; this answers them with the private choice so the run can continue.
function Dismiss-Dialogs([string]$Tag) {
    foreach ($label in @("Not now", "Keep local only")) {
        $b = $null
        # Only windows of this run's Space process: never another application's button.
        foreach ($w in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
            if ($w.Current.ProcessId -ne $space.Id) { continue }
            $b = Find-Button $w $label -Exact
            if ($b) { break }
        }
        if ($b) {
            try { [void](Invoke-Element $b); Write-Host "note: dismissed first-run dialog '$label' ($Tag)"; Start-Sleep -Milliseconds 500 } catch { }
        }
    }
}

function Invoke-Element($El) {
    # Waits up to 10 s for the control to be enabled. Returns $false instead of throwing.
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) {
        try {
            if ($El.Current.IsEnabled) {
                $p = $El.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                $p.Invoke()
                return $true
            }
        } catch { Write-Host "note: invoke retry: $($_.Exception.Message)" }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Wait-Button($Win, [string]$Pattern, [int]$TimeoutSec = 20, [switch]$Exact) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if ($Exact) { $b = Find-Button $Win $Pattern -Exact } else { $b = Find-Button $Win $Pattern }
        if ($b) { return $b }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

function Get-TextLine($Win, [string]$Prefix) {
    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Text)
    foreach ($t in $Win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        try {
            $n = ([string]$t.Current.Name)
            if ($n -like ($Prefix + "*")) { return $n }
        } catch { }
    }
    return $null
}

# Windows refuses SetForegroundWindow from a process that did not get the last input once another
# app (for example a Netie PDF started from peek) holds the foreground. A tapped Alt key lifts that lock.
# Returns true only when the window really is in front, so a focus failure is never blamed on the app.
function Focus-Window($El) {
    $h = [IntPtr]0
    try { $h = [IntPtr]$El.Current.NativeWindowHandle } catch { return $false }
    for ($i = 0; $i -lt 3; $i++) {
        try {
            [DeskNative]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
            [DeskNative]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
            [void][DeskNative]::SetForegroundWindow($h)
        } catch { }
        Start-Sleep -Milliseconds 300
        if ([DeskNative]::GetForegroundWindow() -eq $h) { return $true }
    }
    return $false
}

function Windows-Titled([string]$Title) {
    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $Title
    return @([System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond))
}

function Stop-All {
    foreach ($p in Get-Process -Name NetieSpace -ErrorAction SilentlyContinue) { try { $p.Kill() } catch { } }
}

# Pixel analysis of a screenshot region. Contrast is measured on the ink, never on the log or the DOM:
# the page background is the most common grey in the crop, ink is whatever differs from it by 60+ grey
# levels, and the ratio is WCAG relative luminance of that background against the farthest quarter of the ink.
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
public static class PixelStats {
    static double Lin(int c) { double v = c / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
    // Returns { contrastRatio, inkFraction, backgroundGrey, meanGrey, stdGrey }.
    public static double[] Region(string png, int x, int y, int w, int h) {
        using (var bmp = new Bitmap(png)) {
            x = Math.Max(0, Math.Min(x, bmp.Width - 1)); y = Math.Max(0, Math.Min(y, bmp.Height - 1));
            w = Math.Max(1, Math.Min(w, bmp.Width - x)); h = Math.Max(1, Math.Min(h, bmp.Height - y));
            var rect = new Rectangle(x, y, w, h);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var grey = new int[w * h]; var rgb = new int[w * h];
            var hist = new int[256];
            double sum = 0, sum2 = 0;
            try {
                var row = new byte[Math.Abs(data.Stride)];
                for (int j = 0; j < h; j++) {
                    System.Runtime.InteropServices.Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)j * data.Stride), row, 0, w * 4);
                    for (int i = 0; i < w; i++) {
                        int b = row[i * 4], g = row[i * 4 + 1], r = row[i * 4 + 2];
                        int gr = (int)(0.299 * r + 0.587 * g + 0.114 * b);
                        grey[j * w + i] = gr; rgb[j * w + i] = (r << 16) | (g << 8) | b;
                        hist[gr]++; sum += gr; sum2 += (double)gr * gr;
                    }
                }
            } finally { bmp.UnlockBits(data); }
            int n = w * h, bg = 0;
            for (int k = 1; k < 256; k++) if (hist[k] > hist[bg]) bg = k;
            double mean = sum / n, std = Math.Sqrt(Math.Max(0, sum2 / n - mean * mean));
            var inkDist = new System.Collections.Generic.List<int>();
            var inkGrey = new System.Collections.Generic.List<int>();
            for (int k = 0; k < n; k++) {
                int d = Math.Abs(grey[k] - bg);
                if (d >= 60) { inkDist.Add(d); inkGrey.Add(grey[k]); }
            }
            double frac = (double)inkDist.Count / n;
            if (inkDist.Count == 0) return new double[] { 1.0, 0.0, bg, mean, std };
            var order = new int[inkDist.Count];
            for (int k = 0; k < order.Length; k++) order[k] = k;
            Array.Sort(inkDist.ToArray(), order);
            int from = (int)(order.Length * 0.75);
            double inkSum = 0; int cnt = 0;
            for (int k = from; k < order.Length; k++) { inkSum += inkGrey[order[k]]; cnt++; }
            int ink = (int)Math.Round(inkSum / Math.Max(1, cnt));
            double l1 = Lin(bg), l2 = Lin(ink);
            double ratio = (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
            return new double[] { ratio, frac, bg, mean, std };
        }
    }
    // First line of dark text in a light region: { centreX, centreY, minX, maxX, topY, bottomY } or null.
    // Used to put the pointer on real ink (a page title) without knowing where the viewer drew the page.
    public static int[] FirstInkLine(string png, int x, int y, int w, int h, int darkBelow) {
        using (var bmp = new Bitmap(png)) {
            x = Math.Max(0, Math.Min(x, bmp.Width - 1)); y = Math.Max(0, Math.Min(y, bmp.Height - 1));
            w = Math.Max(1, Math.Min(w, bmp.Width - x)); h = Math.Max(1, Math.Min(h, bmp.Height - y));
            int top = -1, bottom = -1, minX = int.MaxValue, maxX = -1, gap = 0;
            for (int j = 0; j < h; j++) {
                int dark = 0, rowMin = int.MaxValue, rowMax = -1;
                for (int i = 0; i < w; i++) {
                    Color c = bmp.GetPixel(x + i, y + j);
                    int gr = (int)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
                    if (gr < darkBelow) { dark++; if (i < rowMin) rowMin = i; if (i > rowMax) rowMax = i; }
                }
                if (dark >= 3) {
                    if (top < 0) top = j;
                    bottom = j; gap = 0;
                    if (rowMin < minX) minX = rowMin;
                    if (rowMax > maxX) maxX = rowMax;
                } else if (top >= 0) {
                    if (++gap > 2) break;
                }
            }
            if (top < 0) return null;
            return new int[] { x + (minX + maxX) / 2, y + (top + bottom) / 2, x + minX, x + maxX, y + top, y + bottom };
        }
    }
    public static void Crop(string png, string outPng, int x, int y, int w, int h) {
        using (var bmp = new Bitmap(png)) {
            x = Math.Max(0, Math.Min(x, bmp.Width - 1)); y = Math.Max(0, Math.Min(y, bmp.Height - 1));
            w = Math.Max(1, Math.Min(w, bmp.Width - x)); h = Math.Max(1, Math.Min(h, bmp.Height - y));
            using (var c = bmp.Clone(new Rectangle(x, y, w, h), bmp.PixelFormat)) c.Save(outPng, ImageFormat.Png);
        }
    }
}
"@

# Visible Button rectangles of one window: name, left, top, width, height.
function Get-ButtonRects($Win) {
    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Button)
    $rows = @()
    foreach ($b in $Win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        try {
            if ($b.Current.IsOffscreen) { continue }
            $r = $b.Current.BoundingRectangle
            if ($r.IsEmpty -or $r.Width -le 0) { continue }
            $rows += [pscustomobject]@{ Name = ([string]$b.Current.Name).Trim(); X = [int]$r.X; Y = [int]$r.Y; W = [int]$r.Width; H = [int]$r.Height }
        } catch { }
    }
    return ,$rows
}

# The visible "Loading..." text of a window, or $null. Only text that is on screen counts.
function Get-LoadingText($Win) {
    $tcond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Text)
    foreach ($t in $Win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tcond)) {
        try {
            $n = [string]$t.Current.Name
            if ($n -notlike "Loading*") { continue }
            $r = $t.Current.BoundingRectangle
            if ($t.Current.IsOffscreen -or $r.IsEmpty -or $r.Width -le 0) { continue }
            return ("'{0}' at {1},{2} {3}x{4} class={5} id={6}" -f $n, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height, $t.Current.ClassName, $t.Current.AutomationId)
        } catch { }
    }
    return $null
}

# Waits until the peek has finished loading: no visible "Loading..." text left in the window.
# Returns @{ Ms = elapsed or -1; Stuck = the text that never went away }.
function Wait-PeekRendered($Win, [int]$TimeoutSec = 15) {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Milliseconds 700   # the overlay itself only appears after 180 ms
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $quiet = 0
    $last = $null
    while ((Get-Date) -lt $deadline) {
        $last = Get-LoadingText $Win
        # A first-run consent window (local memory, cloud disclosure) is modal and pins the overlay until a
        # person answers it. After 6 s answer it with the private choice, as a person would, then keep waiting.
        if ($last -and $clock.ElapsedMilliseconds -gt 6000) { Dismiss-Dialogs "while loading" }
        if ($last) { $quiet = 0 } else { $quiet++ }
        if ($quiet -ge 3) { Start-Sleep -Milliseconds 1200; return @{ Ms = $clock.ElapsedMilliseconds; Stuck = "" } }  # 3 clean looks, then one paint
        Start-Sleep -Milliseconds 300
    }
    return @{ Ms = -1; Stuck = $last }
}

# Fixtures -----------------------------------------------------------------------------------
$fx = Join-Path $Out "fixtures"
New-Item -ItemType Directory -Force -Path $fx | Out-Null
$src = Join-Path $root "tests\fixtures\preview"
foreach ($f in @("Q3_Report.pdf", "Contract_Lease.pdf", "Portrait_product.png", "Brand_mark.svg", "page.html", "README.md", "config.json", "Program.cs")) {
    Copy-Item (Join-Path $src $f) (Join-Path $fx $f) -Force
}
# Markdown with a local picture beside it.
Copy-Item (Join-Path $src "Portrait_product.png") (Join-Path $fx "notes_picture.png") -Force
Set-Content -Path (Join-Path $fx "notes_with_picture.md") -Encoding UTF8 -Value @"
# Product notes

A local picture sits beside this file.

![portrait](notes_picture.png)

- first point
- second point
"@
# 3 second mp4.
$mp4 = Join-Path $fx "clip.mp4"
$ff = Get-Command ffmpeg -ErrorAction SilentlyContinue
if ($ff) {
    & ffmpeg -hide_banner -loglevel error -y -f lavfi -i "testsrc=duration=3:size=640x360:rate=30" -f lavfi -i "sine=frequency=440:duration=3" -pix_fmt yuv420p -c:v libx264 -c:a aac -shortest $mp4
}
# Minimal .docx and .xlsx (zip of XML parts).
function New-OpenXml([string]$Path, [hashtable]$Parts) {
    if (Test-Path $Path) { Remove-Item $Path -Force }
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::Open($Path, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($k in $Parts.Keys) {
            $e = $zip.CreateEntry($k)
            $s = $e.Open()
            $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($Parts[$k])
            $s.Write($bytes, 0, $bytes.Length)
            $s.Dispose()
        }
    } finally { $zip.Dispose() }
}
$ctDocx = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>'
$relRoot = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>'
$docBody = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Lease summary</w:t></w:r></w:p><w:p><w:r><w:t>Tenant pays rent on the first of each month.</w:t></w:r></w:p></w:body></w:document>'
New-OpenXml (Join-Path $fx "memo.docx") @{ "[Content_Types].xml" = $ctDocx; "_rels/.rels" = $relRoot; "word/document.xml" = $docBody }
$ctXlsx = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>'
$relX = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>'
$wb = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets></workbook>'
$wbRel = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>'
$sheet = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>Item</t></is></c><c r="B1" t="inlineStr"><is><t>Amount</t></is></c></row><row r="2"><c r="A2" t="inlineStr"><is><t>Rent</t></is></c><c r="B2"><v>1200</v></c></row><row r="3"><c r="A3" t="inlineStr"><is><t>Deposit</t></is></c><c r="B3"><v>2400</v></c></row></sheetData></worksheet>'
New-OpenXml (Join-Path $fx "budget.xlsx") @{ "[Content_Types].xml" = $ctXlsx; "_rels/.rels" = $relX; "xl/workbook.xml" = $wb; "xl/_rels/workbook.xml.rels" = $wbRel; "xl/worksheets/sheet1.xml" = $sheet }

# 2. Start Space and wait for ready.
Stop-All
$log = Join-Path $env:LOCALAPPDATA "NetieSpace\logs\app.log"
if (Test-Path $log) { Remove-Item $log -Force -ErrorAction SilentlyContinue }
$space = Start-Process -FilePath $Exe -PassThru
$ready = $false
$deadline = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $deadline) {
    if ($space.HasExited) { break }
    if ((Test-Path $log) -and ((Get-Content -Raw $log -ErrorAction SilentlyContinue) -match "\bReady\.")) { $ready = $true; break }
    Start-Sleep -Milliseconds 300
}
Check "space ready" $ready "pid=$($space.Id) exited=$($space.HasExited)"
if (-not $ready) {
    if (Test-Path $log) { Copy-Item $log (Join-Path $Out "app.log") -Force }
    Save-Screen "space-not-ready.png" | Out-Null
    Stop-All
    exit 1
}
Start-Sleep -Seconds 2
Save-Screen "space-ready.png" | Out-Null
Dismiss-Dialogs "ready"

# 3. Peek each fixture.
$fixtures = @("Q3_Report.pdf", "Contract_Lease.pdf", "Portrait_product.png", "Brand_mark.svg", "page.html",
              "README.md", "notes_with_picture.md", "config.json", "Program.cs", "memo.docx", "budget.xlsx")
if (Test-Path $mp4) { $fixtures += "clip.mp4" } else { Check "mp4 fixture" $false "ffmpeg not found; no video peek" }
$timings = @{}
$controls = @{}
$headerRows = @{}
$title = "NETIE SPACE Preview"
foreach ($name in $fixtures) {
    $file = Join-Path $fx $name
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $fwd = Start-Process -FilePath $Exe -ArgumentList @("--preview", ('"' + $file + '"')) -PassThru
    $win = Wait-Window $title @($space.Id) 30
    $clock.Stop()
    if (-not $win) {
        Check "peek $name" $false "no peek window within 30 s"
        Save-Screen ("peek-" + $name + "-missing.png") | Out-Null
        continue
    }
    $timings[$name] = $clock.ElapsedMilliseconds
    # Wait for the content, not a fixed time: no "Loading preview..." left, then one paint.
    $rendered = Wait-PeekRendered $win 45   # the first peek starts WebView2 cold (22 s on the runner)
    Check "peek $name finished loading" ($rendered.Ms -ge 0) "after $($rendered.Ms) ms stuck=$($rendered.Stuck) (-1 means a visible 'Loading...' never went away)"
    Start-Sleep -Milliseconds 1500   # WebView2 paint
    [void](Focus-Window $win)
    $png = Save-Screen ("peek-" + $name + ".png")
    Dismiss-Dialogs $name
    $btns = Get-Buttons $win
    $controls[$name] = $btns
    Check "peek $name" ((Test-Path $png) -and ((Get-Item $png).Length -gt 10240) -and ($btns.Count -gt 0)) "window in $($clock.ElapsedMilliseconds) ms, $($btns.Count) buttons (0 buttons means the peek chrome is not exposed or not drawn)"

    # No tooltip may be showing in a fresh peek (nothing moved the pointer, so nothing may be hovered).
    $tipCond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::ToolTip)
    $tips = @()
    foreach ($w3 in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        try {
            if ($w3.Current.ProcessId -ne $space.Id) { continue }
            if ($w3.Current.ControlType -eq [System.Windows.Automation.ControlType]::ToolTip) { $tips += [string]$w3.Current.Name; continue }
            foreach ($t3 in $w3.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tipCond)) { $tips += [string]$t3.Current.Name }
        } catch { }
    }
    Check "peek $name shows no stale tooltip" ($tips.Count -eq 0) "tooltips=[$($tips -join ' | ')]"

    # Header: one row. Every header button shares one Y band; a wrapped header spreads over 2-3 bands.
    $winRect = $win.Current.BoundingRectangle
    $rects = Get-ButtonRects $win
    $top = [int]$winRect.Y
    # Only the names the peek header has ever held (so the viewer's own toolbar inside WebView2 never counts),
    # in the top 150 px: a wrapped header spreads these over 2-3 rows, one row keeps them in a single Y band.
    $headerPattern = '^(Open in Netie|Edit$|Done$|Workbench$|Summary|Hide summary|AI:|Cloud:|Chat$|Look up|Remove BG|OCR$|Copy$|Compress$|Rotate 90|Send to Comfy|Windows AI|Share$|Settings$|More$|Close$)'
    $headerBtns = @($rects | Where-Object { ($_.Y - $top) -lt 150 -and $_.Name -match $headerPattern })
    if ($headerBtns.Count -gt 0) {
        $centers = @($headerBtns | ForEach-Object { $_.Y + [int]($_.H / 2) })
        $spread = ($centers | Measure-Object -Maximum).Maximum - ($centers | Measure-Object -Minimum).Minimum
        Check "peek $name header is one row" ($spread -le 14) "buttons=$($headerBtns.Count) centreY spread=${spread}px names=$(($headerBtns | ForEach-Object { $_.Name }) -join ' | ')"
        Check "peek $name header holds at most 7 buttons" ($headerBtns.Count -le 7) "count=$($headerBtns.Count)"
        $more = @($headerBtns | Where-Object { $_.Name -eq "More" })
        Check "peek $name has a More button" ($more.Count -eq 1) ""
    } else {
        Check "peek $name header buttons found" $false "no header button found in the top 150 px of the window"
    }
    $headerRows[$name] = $headerBtns

    # Summary strip: not on pictures and clips unless asked.
    if ($name -in @("Portrait_product.png", "clip.mp4")) {
        $strip = Get-TextLine $win "Type:"
        Check "peek $name shows no summary strip by default" ($null -eq $strip) "text='$strip'"
    }

    # Image: opens at Fit, which never enlarges, and the label shows the real zoom.
    if ($name -eq "Portrait_product.png") {
        $zl = $null
        $tcond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Text)
        foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tcond)) {
            try { $n = [string]$t.Current.Name; if ($n -match '^\d+%$') { $zl = $n; break } } catch { }
        }
        $zv = 0; if ($zl) { $zv = [int]($zl.TrimEnd('%')) }
        Check "peek image zoom label is shown and is at most 100%" ($zl -and $zv -le 100 -and $zv -ge 5) "zoom='$zl' (684% was the old Fit on a 64 px picture)"

        # More sheet: every secondary action is still there.
        $moreBtn = Find-Button $win "More" -Exact
        if ($moreBtn -and (Invoke-Element $moreBtn)) {
            Start-Sleep -Milliseconds 900
            Save-Screen "peek-more-Portrait_product.png" | Out-Null
            $all = @()
            foreach ($w2 in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
                if ($w2.Current.ProcessId -ne $space.Id) { continue }
                $all += Get-Buttons $w2
            }
            $need = @("Summary", "Chat", "Remove BG", "OCR", "Copy", "Compress", "Rotate 90", "Send to Comfy...", "Windows AI...")
            $missing = @($need | Where-Object { $all -notcontains $_ })
            Check "More sheet lists every image action" ($missing.Count -eq 0) "missing=[$($missing -join ', ')] found=[$($all -join ' | ')]"
            [void](Invoke-Element $moreBtn)   # toggle closed
            Start-Sleep -Milliseconds 400
        } else {
            Check "More sheet opens" $false "More button not found or not invokable"
        }
    }

    # Word: the page text is readable. Crop the page area, measure ink against its background.
    if ($name -eq "memo.docx") {
        $cx = [int]($winRect.X + $winRect.Width * 0.12); $cy = [int]($top + 190)
        $cw = [int]($winRect.Width * 0.76); $chh = [int]($winRect.Height * 0.40)
        [PixelStats]::Crop($png, (Join-Path $Out "peek-memo-crop.png"), $cx, $cy, $cw, $chh)
        $st = [PixelStats]::Region($png, $cx, $cy, $cw, $chh)
        Check "memo.docx text is readable (contrast >= 4.5, ink visible)" (($st[0] -ge 4.5) -and ($st[1] -ge 0.0003) -and ($st[1] -le 0.35)) ("contrast={0:N2} inkFraction={1:N5} backgroundGrey={2} meanGrey={3:N1}" -f $st[0], $st[1], $st[2], $st[3])
    }

    # Video: record what MediaElement reported, plainly, and check the pane is not black.
    if ($name -eq "clip.mp4") {
        $probe = @()
        if (Test-Path $log) {
            try {
                $fs = [System.IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
                $sr = New-Object System.IO.StreamReader $fs
                $probe = @($sr.ReadToEnd() -split "`n" | Where-Object { $_ -match '\[video\]' })
                $sr.Dispose(); $fs.Dispose()
            } catch { Write-Host "note: could not read app.log for the video probe: $($_.Exception.Message)" }
        }
        $probe | ForEach-Object { Write-Host "video: $($_.Trim())" }
        $probe | Set-Content -Path (Join-Path $Out "video-probe.txt") -Encoding UTF8
        Check "video probe logged what MediaElement reports" ($probe.Count -gt 0) "lines=$($probe.Count) (see video-probe.txt: natural=WxH, hasVideo, MediaFailed)"
        $wr = $winRect
        $vs = [PixelStats]::Region($png, [int]($wr.X + $wr.Width * 0.1), [int]($top + 140), [int]($wr.Width * 0.8), [int]($wr.Height * 0.4))
        Check "video pane is not black (a picture or the still frame)" ($vs[4] -gt 3) ("meanGrey={0:N1} stdGrey={1:N1} (an empty pane is flat: std below 3)" -f $vs[3], $vs[4])
    }

    # Q3_Report: the bridge button, while the peek is open.
    if ($name -eq "Q3_Report.pdf") {
        $b = Wait-Button $win "Open in Netie PDF" 15 -Exact
        Check "peek pdf has Open in Netie PDF" ($null -ne $b) ""
        if ($b) {
            $clicked = Invoke-Element $b
            Check "Open in Netie PDF invoked" $clicked ""
            $appWin = Wait-Window "Netie PDF" $null 30
            $ownPid = 0
            if ($appWin) { $ownPid = $appWin.Current.ProcessId }
            Check "Open in Netie PDF starts a Netie PDF process" ($appWin -and ($ownPid -ne $space.Id)) "pid=$ownPid space=$($space.Id)"
            if ($appWin) {
                Start-Sleep -Milliseconds 1500
                Save-Screen "peek-bridge-netie-pdf.png" | Out-Null
            }
        }
    }

    # Close the peek with Esc; fall back to the window pattern so one stuck peek cannot cascade.
    $peek = Find-Window $title @($space.Id)
    if ($peek) {
        $front = Focus-Window $peek
        Check "peek $name is in front before Esc" $front "foreground=$([DeskNative]::GetForegroundWindow()) peek=$($peek.Current.NativeWindowHandle)"
        [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
    } else { Write-Host "note: peek $name was already gone before Esc" }
    $gone = $false
    for ($i = 0; $i -lt 40; $i++) {
        if (-not (Find-Window $title @($space.Id))) { $gone = $true; break }
        Start-Sleep -Milliseconds 150
    }
    if ($peek) { Check "Esc closes peek $name" $gone "" }
    if (-not $gone) {
        try {
            $left = Find-Window $title @($space.Id)
            if ($left) { ($left.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).Close() }
            Start-Sleep -Milliseconds 800
        } catch { }
    }
    Start-Sleep -Milliseconds 400
}
$timings | ConvertTo-Json | Set-Content -Path (Join-Path $Out "peek-timings.json") -Encoding UTF8
$controls | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $Out "peek-controls.json") -Encoding UTF8
$headerRows | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $Out "peek-header-rects.json") -Encoding UTF8

# 4b. Quick Look flow (wave 6): a student peeks a lecture PDF, scrolls, finds "exam", steps through the folder
# (PNG, DOCX, MP4), copies a paragraph, presses Esc. Every step is a real key press on the real window;
# every claim is read from the window's UI Automation tree or the clipboard, and every step has a screenshot.
function New-LecturePdf([string]$Path, [int]$Pages, [int[]]$ExamPages) {
    # A minimal text PDF written by hand (ASCII, exact xref offsets). "exam" appears only on $ExamPages.
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append("%PDF-1.4`n")
    $kids = (1..$Pages | ForEach-Object { "{0} 0 R" -f (4 + 2 * ($_ - 1)) }) -join " "
    $objs = @{}
    $objs[1] = "<< /Type /Catalog /Pages 2 0 R >>"
    $objs[2] = "<< /Type /Pages /Kids [$kids] /Count $Pages >>"
    $objs[3] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
    for ($i = 1; $i -le $Pages; $i++) {
        $po = 4 + 2 * ($i - 1); $co = $po + 1
        $objs[$po] = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents $co 0 R >>"
        $lines = "BT /F1 22 Tf 72 700 Td (Lecture 1 - page $i of $Pages) Tj ET`nBT /F1 13 Tf 72 660 Td (Notes for section $i. Read the chapter before class.) Tj ET`n"
        if ($ExamPages -contains $i) { $lines += "BT /F1 13 Tf 72 630 Td (This topic will be on the exam.) Tj ET`n" }
        $objs[$co] = "<< /Length $($lines.Length) >>`nstream`n" + $lines + "endstream"
    }
    $total = 3 + 2 * $Pages
    $offs = @{}
    for ($n = 1; $n -le $total; $n++) { $offs[$n] = $sb.Length; [void]$sb.Append("$n 0 obj`n" + $objs[$n] + "`nendobj`n") }
    $xref = $sb.Length
    [void]$sb.Append("xref`n0 $($total + 1)`n0000000000 65535 f `n")
    for ($n = 1; $n -le $total; $n++) { [void]$sb.Append(("{0:D10} 00000 n `n" -f $offs[$n])) }
    [void]$sb.Append("trailer`n<< /Size $($total + 1) /Root 1 0 R >>`nstartxref`n$xref`n%%EOF`n")
    [System.IO.File]::WriteAllBytes($Path, [System.Text.Encoding]::ASCII.GetBytes($sb.ToString()))
}

function Get-UiaName($Win, [string]$Id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::AutomationIdProperty), $Id
    try {
        $e = $Win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($e) { return [string]$e.Current.Name }
    } catch { }
    return $null
}

# Polls an element's name until the predicate holds. Returns @{ Ok; Value; Ms } (Value is the last name seen).
function Wait-UiaName($Win, [string]$Id, [scriptblock]$Pred, [double]$TimeoutSec = 10) {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $v = $null
    while ($clock.Elapsed.TotalSeconds -lt $TimeoutSec) {
        $v = Get-UiaName $Win $Id
        if (($null -ne $v) -and (& $Pred $v)) { return @{ Ok = $true; Value = $v; Ms = $clock.ElapsedMilliseconds } }
        Start-Sleep -Milliseconds 100
    }
    return @{ Ok = $false; Value = $v; Ms = $clock.ElapsedMilliseconds }
}

# Visible text of any window of this run's Space process whose name matches (the help sheet is its own popup window).
function Find-SpaceText([string]$Text) {
    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $Text
    foreach ($w in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        try {
            if ($w.Current.ProcessId -ne $space.Id) { continue }
            $hit = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
            if ($hit) { return $hit }
        } catch { }
    }
    return $null
}

function Get-ZoomText($Win) {
    $tcond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Text)
    foreach ($t in $Win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tcond)) {
        try { $n = [string]$t.Current.Name; if ($n -match '^\d+%$') { return $n } } catch { }
    }
    return $null
}

function Get-ClipText {
    for ($i = 0; $i -lt 8; $i++) {
        try { $t = [System.Windows.Forms.Clipboard]::GetText(); return [string]$t } catch { Start-Sleep -Milliseconds 200 }
    }
    return ""
}
function Clear-Clip { for ($i = 0; $i -lt 8; $i++) { try { [System.Windows.Forms.Clipboard]::Clear(); return } catch { Start-Sleep -Milliseconds 200 } } }

function Send-Keys([string]$Keys) { [System.Windows.Forms.SendKeys]::SendWait($Keys) }

function Click-At([int]$X, [int]$Y, [int]$Times = 1) {
    [void][DeskNative]::SetCursorPos($X, $Y)
    Start-Sleep -Milliseconds 120
    for ($i = 0; $i -lt $Times; $i++) {
        [DeskNative]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
        [DeskNative]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 60
    }
}

$lec = Join-Path $fx "lecture"
New-Item -ItemType Directory -Force -Path $lec | Out-Null
New-LecturePdf (Join-Path $lec "lecture_01_notes.pdf") 40 @(5, 17, 33)
Copy-Item (Join-Path $src "Portrait_product.png") (Join-Path $lec "lecture_02_diagram.png") -Force
Copy-Item (Join-Path $fx "memo.docx") (Join-Path $lec "lecture_03_memo.docx") -Force
if (Test-Path $mp4) { Copy-Item $mp4 (Join-Path $lec "lecture_04_clip.mp4") -Force }
$lecNames = @(Get-ChildItem $lec -File | Sort-Object Name | ForEach-Object { $_.Name })
$lecTotal = $lecNames.Count
$flowSteps = [ordered]@{}
$null = Start-Process -FilePath $Exe -ArgumentList @("--preview", ('"' + (Join-Path $lec $lecNames[0]) + '"')) -PassThru
$fwin = Wait-Window $title @($space.Id) 30
Check "flow: peek opens the lecture PDF" ($null -ne $fwin) "folder holds $lecTotal files: $($lecNames -join ', ')"
if ($fwin) {
    [void](Wait-PeekRendered $fwin 45)
    Dismiss-Dialogs "flow"
    [void](Focus-Window $fwin)
    $fr = $fwin.Current.BoundingRectangle

    # 1. Open: file name, "1 of N", "Page 1 / 40".
    $r = Wait-UiaName $fwin "PeekFileName" { param($v) $v -eq $lecNames[0] } 10
    Check "flow: header shows the file name" $r.Ok "name='$($r.Value)'"
    $r = Wait-UiaName $fwin "PeekCounter" { param($v) $v -eq "1 of $lecTotal" } 10
    Check "flow: header counter reads '1 of $lecTotal'" $r.Ok "counter='$($r.Value)'"
    $r = Wait-UiaName $fwin "PeekPage" { param($v) $v -eq "Page 1 / 40" } 20
    Check "flow: PDF shows 'Page 1 / 40'" $r.Ok "page='$($r.Value)'"
    Save-Screen "flow-1-pdf-open.png" | Out-Null

    # 2. Scroll: Page Down x3 changes the page; End goes to the last page; Home returns.
    $p0 = Get-UiaName $fwin "PeekPage"
    foreach ($k in 1..3) { Send-Keys "{PGDN}"; Start-Sleep -Milliseconds 450 }
    $r = Wait-UiaName $fwin "PeekPage" { param($v) ($v -match '^Page \d+ / 40$') -and ($v -ne $p0) } 8
    Check "flow: Page Down moves the page indicator" $r.Ok "before='$p0' after='$($r.Value)'"
    Save-Screen "flow-2-pagedown.png" | Out-Null
    Send-Keys "{END}"
    $r = Wait-UiaName $fwin "PeekPage" { param($v) $v -eq "Page 40 / 40" } 12
    Check "flow: End jumps to 'Page 40 / 40'" $r.Ok "page='$($r.Value)'"
    Save-Screen "flow-3-end.png" | Out-Null
    Send-Keys "{HOME}"
    $r = Wait-UiaName $fwin "PeekPage" { param($v) $v -eq "Page 1 / 40" } 12
    Check "flow: Home returns to 'Page 1 / 40'" $r.Ok "page='$($r.Value)'"

    # 3. Find: Ctrl+F opens the bar, typing finds, Enter steps, Esc closes the bar but not the peek.
    Send-Keys "^f"
    $box = $null
    $fd = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $fd -and -not $box) {
        $bc = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::AutomationIdProperty), "PeekFindBox"
        $box = $fwin.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $bc)
        if (-not $box) { Start-Sleep -Milliseconds 150 }
    }
    Check "flow: Ctrl+F opens the find bar" ($null -ne $box) ""
    Send-Keys "exam"
    $r = Wait-UiaName $fwin "PeekFindCount" { param($v) $v -match '^\d+ of \d+$' } 20
    $matchCount = 0
    if ($r.Ok) { $matchCount = [int](($r.Value -split ' of ')[1]) }
    Check "flow: find reports a match count > 0" ($matchCount -gt 0) "readout='$($r.Value)' after $($r.Ms) ms"
    Check "flow: find counts the 3 exam pages of the fixture" ($matchCount -eq 3) "count=$matchCount (exam is on pages 5, 17, 33)"
    Start-Sleep -Milliseconds 900
    $pg = Get-UiaName $fwin "PeekPage"
    Check "flow: find scrolled to the first match" (($pg -match '^Page \d+ / 40$') -and ($pg -ne "Page 1 / 40")) "page='$pg'"
    Save-Screen "flow-4-find.png" | Out-Null
    Send-Keys "{ENTER}"
    $r = Wait-UiaName $fwin "PeekFindCount" { param($v) $v -eq "2 of $matchCount" } 8
    Check "flow: Enter steps to the next match" $r.Ok "readout='$($r.Value)'"
    Save-Screen "flow-5-find-next.png" | Out-Null
    Send-Keys "{ESC}"
    Start-Sleep -Milliseconds 700
    $bc = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::AutomationIdProperty), "PeekFindBox"
    $boxGone = -not $fwin.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $bc)
    Check "flow: Esc closes the find bar" $boxGone ""
    Check "flow: the peek is still open after closing find" ($null -ne (Find-Window $title @($space.Id))) ""

    # 4. Copy: triple-click the page title (real ink found in the screenshot), Ctrl+C, read the clipboard.
    Send-Keys "{HOME}"
    [void](Wait-UiaName $fwin "PeekPage" { param($v) $v -eq "Page 1 / 40" } 12)
    Start-Sleep -Milliseconds 1200
    $pageEl = $null
    $pc = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::AutomationIdProperty), "PeekPage"
    $pageEl = $fwin.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $pc)
    $shot = Save-Screen "flow-6-before-copy.png"
    $line = $null
    if ($pageEl) {
        $pr = $pageEl.Current.BoundingRectangle
        $line = [PixelStats]::FirstInkLine($shot, [int]($fr.X + $fr.Width * 0.22), [int]($pr.Bottom + 30), [int]($fr.Width * 0.7), [int]($fr.Height * 0.4), 90)
    }
    Check "flow: found the page title ink to select" ($null -ne $line) "line=$(if ($line) { $line -join ',' })"
    Clear-Clip
    if ($line) {
        Click-At $line[0] $line[1] 3
        Start-Sleep -Milliseconds 400
        Send-Keys "^c"
        Start-Sleep -Milliseconds 600
    }
    $clip = Get-ClipText
    Check "flow: Ctrl+C copies the selected PDF text" ($clip -match "Lecture 1") "clipboard='$($clip.Trim())'"
    Save-Screen "flow-7-pdf-copied.png" | Out-Null

    # 5. Zoom: Ctrl + zooms in, Ctrl 0 fits again.
    [void](Focus-Window $fwin)
    $z0 = Get-ZoomText $fwin
    Send-Keys "^{ADD}"
    $zr = $null
    $zd = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $zd) { $zr = Get-ZoomText $fwin; if ($zr -and $zr -ne $z0) { break }; Start-Sleep -Milliseconds 200 }
    Check "flow: Ctrl + zooms in" ($zr -and $zr -ne $z0) "before=$z0 after=$zr"
    Save-Screen "flow-8-zoom-in.png" | Out-Null
    Send-Keys "^0"
    $zd = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $zd) { $zr = Get-ZoomText $fwin; if ($zr -eq $z0) { break }; Start-Sleep -Milliseconds 200 }
    Check "flow: Ctrl 0 fits again" ($zr -eq $z0) "fit=$z0 now=$zr"

    # 6. Help: "?" shows the cheat sheet, Esc closes it, the peek stays.
    Send-Keys "?"
    $hd = (Get-Date).AddSeconds(6); $help = $null
    while ((Get-Date) -lt $hd -and -not $help) { $help = Find-SpaceText "Keyboard shortcuts"; if (-not $help) { Start-Sleep -Milliseconds 150 } }
    Check "flow: ? shows the keyboard cheat sheet" ($null -ne $help) ""
    Start-Sleep -Milliseconds 600
    Save-Screen "flow-9-help.png" | Out-Null
    Send-Keys "{ESC}"
    Start-Sleep -Milliseconds 700
    Check "flow: Esc closes the cheat sheet" ($null -eq (Find-SpaceText "Keyboard shortcuts")) ""
    Check "flow: the peek is still open after closing help" ($null -ne (Find-Window $title @($space.Id))) ""

    # 7. Step through the folder: Right shows the next file (title and "k of N" change); the last wraps to the first.
    [void](Focus-Window $fwin)
    for ($i = 1; $i -lt $lecTotal; $i++) {
        $want = $lecNames[$i]
        Send-Keys "{RIGHT}"
        $r = Wait-UiaName $fwin "PeekFileName" { param($v) $v -eq $want } 10
        $flowSteps[$want] = $r.Ms
        Check "flow: Right shows $want" $r.Ok "name='$($r.Value)' after $($r.Ms) ms"
        $r2 = Wait-UiaName $fwin "PeekCounter" { param($v) $v -eq "$($i + 1) of $lecTotal" } 5
        Check "flow: counter reads '$($i + 1) of $lecTotal' on $want" $r2.Ok "counter='$($r2.Value)'"
        [void](Wait-PeekRendered $fwin 25)
        Start-Sleep -Milliseconds 800
        Save-Screen ("flow-10-next-{0}.png" -f $i) | Out-Null
        if ($want -like "*.docx") {
            # Copy a paragraph from the Word peek: select all, copy, read what the clipboard holds.
            Clear-Clip
            Send-Keys "^a"
            Start-Sleep -Milliseconds 300
            Send-Keys "^c"
            Start-Sleep -Milliseconds 600
            $clip = Get-ClipText
            Check "flow: Ctrl+C copies text from the Word peek" ($clip -match "Tenant pays rent") "clipboard='$($clip.Trim())'"
            Save-Screen "flow-11-docx-copied.png" | Out-Null
        }
    }
    Send-Keys "{RIGHT}"
    $r = Wait-UiaName $fwin "PeekFileName" { param($v) $v -eq $lecNames[0] } 10
    Check "flow: Right on the last file wraps to the first" $r.Ok "name='$($r.Value)'"
    $r = Wait-UiaName $fwin "PeekCounter" { param($v) $v -eq "1 of $lecTotal" } 5
    Check "flow: wrap reads '1 of $lecTotal'" $r.Ok "counter='$($r.Value)'"
    Start-Sleep -Milliseconds 1200
    Save-Screen "flow-12-wrapped.png" | Out-Null
    Send-Keys "{LEFT}"
    $r = Wait-UiaName $fwin "PeekFileName" { param($v) $v -eq $lecNames[$lecTotal - 1] } 10
    Check "flow: Left on the first file wraps to the last" $r.Ok "name='$($r.Value)'"
    Start-Sleep -Milliseconds 800

    # 8. Esc closes the peek.
    [void](Focus-Window $fwin)
    Send-Keys "{ESC}"
    $gone = $false
    for ($i = 0; $i -lt 50; $i++) { if (-not (Find-Window $title @($space.Id))) { $gone = $true; break }; Start-Sleep -Milliseconds 150 }
    Check "flow: Esc closes the peek" $gone ""
    Save-Screen "flow-13-closed.png" | Out-Null
    if (-not $gone) {
        try { $left = Find-Window $title @($space.Id); if ($left) { ($left.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).Close() } } catch { }
    }
    $flowSteps | ConvertTo-Json | Set-Content -Path (Join-Path $Out "flow-step-ms.json") -Encoding UTF8
}

# Open-to-content per kind, read from the app's own log (one "[peek] open-to-content" line per open).
# Warm = WebView2 was already running when the file was asked for. The budget is the median of the warm opens.
$openLines = @()
if (Test-Path $log) {
    try {
        $fs = [System.IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
        $sr = New-Object System.IO.StreamReader $fs
        $openLines = @($sr.ReadToEnd() -split "`n" | Where-Object { $_ -match '\[peek\] open-to-content' })
        $sr.Dispose(); $fs.Dispose()
    } catch { Write-Host "note: could not read app.log for open timings: $($_.Exception.Message)" }
}
$opens = @()
foreach ($l in $openLines) {
    if ($l -match 'kind=(\w+) ms=(\d+) warm=(\d)') { $opens += [pscustomobject]@{ Kind = $Matches[1]; Ms = [int]$Matches[2]; Warm = ($Matches[3] -eq "1") } }
}
$opens | ForEach-Object { Write-Host ("open: {0,-9} {1,6} ms warm={2}" -f $_.Kind, $_.Ms, $_.Warm) }
$summary = [ordered]@{}
foreach ($g in ($opens | Group-Object Kind)) {
    $warmMs = @($g.Group | Where-Object { $_.Warm } | ForEach-Object { $_.Ms } | Sort-Object)
    $coldMs = @($g.Group | Where-Object { -not $_.Warm } | ForEach-Object { $_.Ms })
    $median = $null
    if ($warmMs.Count -gt 0) { $median = $warmMs[[int][math]::Floor(($warmMs.Count - 1) / 2)] }
    $summary[$g.Name] = [ordered]@{ warmMedianMs = $median; warmMaxMs = $(if ($warmMs.Count) { $warmMs[-1] } else { $null }); warmCount = $warmMs.Count; coldMs = $coldMs }
}
$summary | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $Out "open-to-content.json") -Encoding UTF8
$opens | ForEach-Object { "{0} {1} warm={2}" -f $_.Kind, $_.Ms, $_.Warm } | Set-Content -Path (Join-Path $Out "open-to-content.txt") -Encoding UTF8
Check "open-to-content was logged for the peeks" ($opens.Count -ge 8) "lines=$($opens.Count)"
foreach ($kind in $summary.Keys) {
    $m = $summary[$kind].warmMedianMs
    if ($null -eq $m) { Write-Host "note: no warm open of $kind to judge"; continue }
    # A clip is ready when MediaElement opens it; on the runner that waits on the media pipeline (seen 3.7 s), so it is reported, not gated.
    if ($kind -eq "Video") { Write-Host "note: Video warm median $m ms (reported, not gated: MediaElement start-up on the runner)"; continue }
    Check "open-to-content $kind warm median is within 600 ms" ($m -le 600) "median=${m} ms max=$($summary[$kind].warmMaxMs) ms over $($summary[$kind].warmCount) warm opens"
}

# 5. Drive the apps. Start from no app process: the Space tray process is the only one left.
foreach ($p in Get-Process -Name NetieSpace -ErrorAction SilentlyContinue) {
    if ($p.Id -ne $space.Id) { try { $p.Kill() } catch { } }
}
Start-Sleep -Seconds 1
$pdfIn = Join-Path $fx "Q3_Report.pdf"
$pdfApp = Start-Process -FilePath $Exe -ArgumentList @("--app", "pdf", "--open", ('"' + $pdfIn + '"')) -PassThru
$pdfWin = Wait-Window "Netie PDF" @($pdfApp.Id) 40
Check "netie pdf window" ($null -ne $pdfWin) "pid=$($pdfApp.Id)"
if ($pdfWin) {
    Start-Sleep -Seconds 3
    [void](Focus-Window $pdfWin)
    Save-Screen "app-pdf-home.png" | Out-Null
    Dismiss-Dialogs "app"
    [void](Focus-Window $pdfWin)

    $tile = Wait-ButtonByText $pdfWin "Rotate PDF" 30
    Check "Rotate PDF tile found" ($null -ne $tile) ""
    if ($tile) {
        Check "Rotate PDF tile invoked" (Invoke-Element $tile) ""
        Start-Sleep -Seconds 2
        Save-Screen "app-pdf-rotate-page.png" | Out-Null
        $act = Wait-Button $pdfWin "Rotate" 15 -Exact
        Check "Rotate action button found" ($null -ne $act) ""
        if ($act) {
            Check "Rotate action invoked" (Invoke-Element $act) ""
            $line = $null
            $deadline = (Get-Date).AddSeconds(90)
            while ((Get-Date) -lt $deadline) {
                $line = Get-TextLine $pdfWin "Done:"
                if (-not $line) { $line = Get-TextLine $pdfWin "cannot" }
                if ($line) { break }
                Start-Sleep -Milliseconds 400
            }
            Start-Sleep -Milliseconds 500
            Save-Screen "app-pdf-rotate-result.png" | Out-Null
            Check "rotate result line" ($line -and $line.StartsWith("Done:")) "line='$line'"
            if ($line -and $line.StartsWith("Done:")) {
                $outPath = $line.Substring(5).Trim()
                Check "rotate output file exists" (Test-Path $outPath) "path=$outPath"
            } else {
                Check "rotate output file exists" $false "no Done line (tool page may not have taken the opened file)"
            }
        }
    }

    # Second launch: still exactly one Netie PDF process, window in the foreground.
    $again = Start-Process -FilePath $Exe -ArgumentList @("--app", "pdf") -PassThru
    [void]$again.WaitForExit(15000)
    Start-Sleep -Seconds 2
    $pdfWins = @(Windows-Titled "Netie PDF")
    $pdfPids = @($pdfWins | ForEach-Object { $_.Current.ProcessId } | Sort-Object -Unique)
    Check "one Netie PDF process after second launch" (($pdfWins.Count -eq 1) -and ($pdfPids.Count -eq 1)) "windows=$($pdfWins.Count) pids=$($pdfPids -join ',')"
    $fg = [DeskNative]::GetForegroundWindow()
    $fgPid = [uint32]0
    [void][DeskNative]::GetWindowThreadProcessId($fg, [ref]$fgPid)
    $isFg = $pdfWins.Count -ge 1 -and ([IntPtr]$pdfWins[0].Current.NativeWindowHandle -eq $fg)
    Check "Netie PDF window is foreground" $isFg "foreground pid=$fgPid netie-pdf pid=$($pdfPids -join ',')"
    Save-Screen "app-pdf-second-launch.png" | Out-Null

    # Family button: Netie Image becomes its own process.
    $pdfWin = Find-Window "Netie PDF" $null
    if ($pdfWin) {
        $fam = Wait-Button $pdfWin "Netie Image" 10 -Exact
        Check "Netie Image family button found" ($null -ne $fam) ""
        if ($fam) {
            Check "Netie Image family button invoked" (Invoke-Element $fam) ""
            $imgWin = Wait-Window "Netie Image" $null 40
            $imgPid = 0
            if ($imgWin) { $imgPid = $imgWin.Current.ProcessId }
            Check "Netie Image process appears" ($imgWin -and ($imgPid -ne $space.Id)) "pid=$imgPid"
            Start-Sleep -Seconds 3
            Save-Screen "app-image-home.png" | Out-Null
        }
    }
} else {
    Save-Screen "app-pdf-missing.png" | Out-Null
}

if (Test-Path $log) { Copy-Item $log (Join-Path $Out "app.log") -Force }
Stop-All
if ($script:failed -gt 0) { Write-Host "$($script:failed) check(s) failed"; exit 1 }
Write-Host "all checks passed"
exit 0
