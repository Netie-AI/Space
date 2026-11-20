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

# Waits until the peek has finished loading: no "Loading preview..." text left in the window.
# Returns the milliseconds it took, or -1 when the overlay never went away.
function Wait-PeekRendered($Win, [int]$TimeoutSec = 30) {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Milliseconds 700   # the overlay itself only appears after 180 ms
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $quiet = 0
    while ((Get-Date) -lt $deadline) {
        $loading = Get-TextLine $Win "Loading"
        if ($loading) { $quiet = 0 } else { $quiet++ }
        if ($quiet -ge 3) { Start-Sleep -Milliseconds 1200; return $clock.ElapsedMilliseconds }  # 3 clean looks, then one paint
        Start-Sleep -Milliseconds 300
    }
    return -1
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
    $renderMs = Wait-PeekRendered $win 30
    Check "peek $name finished loading" ($renderMs -ge 0) "after $renderMs ms (-1 means 'Loading preview...' never went away)"
    Start-Sleep -Milliseconds 1500   # WebView2 paint
    [void](Focus-Window $win)
    $png = Save-Screen ("peek-" + $name + ".png")
    Dismiss-Dialogs $name
    $btns = @(Get-Buttons $win)
    $controls[$name] = $btns
    Check "peek $name" ((Test-Path $png) -and ((Get-Item $png).Length -gt 10240) -and ($btns.Count -gt 0)) "window in $($clock.ElapsedMilliseconds) ms, $($btns.Count) buttons (0 buttons means the peek chrome is not exposed or not drawn)"

    # Header: one row. Every header button shares one Y band; a wrapped header spreads over 2-3 bands.
    $winRect = $win.Current.BoundingRectangle
    $rects = @(Get-ButtonRects $win)
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
                $all += @(Get-Buttons $w2)
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
        Check "video pane is not black" (($vs[3] -gt 12) -or ($vs[4] -gt 12)) ("meanGrey={0:N1} stdGrey={1:N1} (a black pane is mean~0 std~0)" -f $vs[3], $vs[4])
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
