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
        $b = Find-Button ([System.Windows.Automation.AutomationElement]::RootElement) $label -Exact
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

function Focus-Window($El) {
    try { [void][DeskNative]::SetForegroundWindow([IntPtr]$El.Current.NativeWindowHandle) } catch { }
    Start-Sleep -Milliseconds 300
}

function Windows-Titled([string]$Title) {
    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $Title
    return @([System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond))
}

function Stop-All {
    foreach ($p in Get-Process -Name NetieSpace -ErrorAction SilentlyContinue) { try { $p.Kill() } catch { } }
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
    Start-Sleep -Milliseconds 3500   # WebView2 paint
    Focus-Window $win
    $png = Save-Screen ("peek-" + $name + ".png")
    Dismiss-Dialogs $name
    $btns = @(Get-Buttons $win)
    $controls[$name] = $btns
    Check "peek $name" ((Test-Path $png) -and ((Get-Item $png).Length -gt 10240) -and ($btns.Count -gt 0)) "window in $($clock.ElapsedMilliseconds) ms, $($btns.Count) buttons (0 buttons means the peek chrome is not exposed or not drawn)"

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
        Focus-Window $peek
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
    Focus-Window $pdfWin
    Save-Screen "app-pdf-home.png" | Out-Null
    Dismiss-Dialogs "app"
    Focus-Window $pdfWin

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
