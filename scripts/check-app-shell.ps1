# Runs the built NetieSpace.exe headless and asserts what a person would receive:
# app snapshots (S50-E1-b), the tool list and one real tool run (S50-E2-a).
# Windows only. Works under Windows PowerShell 5.1 and pwsh. Exit 1 on any failure.
param(
    [string]$Exe = "",
    [string]$Out = (Join-Path $env:TEMP "s50-check")
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

function Invoke-App([string[]]$ArgList, [int]$TimeoutSec = 120) {
    $errFile = Join-Path $Out ("stderr-" + [guid]::NewGuid().ToString("N") + ".txt")
    $outFile = Join-Path $Out ("stdout-" + [guid]::NewGuid().ToString("N") + ".txt")
    $quoted = $ArgList | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $p = Start-Process -FilePath $Exe -ArgumentList $quoted -PassThru -NoNewWindow `
        -RedirectStandardError $errFile -RedirectStandardOutput $outFile
    if (-not $p.WaitForExit($TimeoutSec * 1000)) {
        try { $p.Kill() } catch { }
        return @{ Code = -999; Err = "timed out after $TimeoutSec s"; Out = "" }
    }
    $p.WaitForExit()
    $err = ""; $std = ""
    if (Test-Path $errFile) { $err = [string](Get-Content -Raw $errFile) }
    if (Test-Path $outFile) { $std = [string](Get-Content -Raw $outFile) }
    return @{ Code = $p.ExitCode; Err = $err; Out = $std }
}

function Check([string]$Name, [bool]$Ok, [string]$Detail) {
    if ($Ok) { Write-Host "OK   $Name $Detail" }
    else { Write-Host "FAIL $Name $Detail"; $script:failed++ }
}

# 1. Home snapshot of each app: exit 0 and a PNG a person could look at.
foreach ($app in @("pdf", "image", "video")) {
    $png = Join-Path $Out "$app-home.png"
    $r = Invoke-App @("--app", $app, "--snapshot", $png)
    $size = 0
    if (Test-Path $png) { $size = (Get-Item $png).Length }
    Check "snapshot $app home" (($r.Code -eq 0) -and ($size -gt 10240)) "exit=$($r.Code) bytes=$size $($r.Err)"
}

# 2. Tool page snapshot, available and unavailable.
foreach ($view in @("pdf-extract-text", "pdf-to-pdfa", "pdf-watermark", "pdf-split")) {
    $png = Join-Path $Out "$view.png"
    $r = Invoke-App @("--app", "pdf", "--snapshot", $png, "--view", $view)
    $size = 0
    if (Test-Path $png) { $size = (Get-Item $png).Length }
    Check "snapshot view $view" (($r.Code -eq 0) -and ($size -gt 10240)) "exit=$($r.Code) bytes=$size $($r.Err)"
}

# 3. Unknown view: exit 2 and stderr names it. No PNG.
$png = Join-Path $Out "nope.png"
$r = Invoke-App @("--app", "pdf", "--snapshot", $png, "--view", "nope")
Check "snapshot unknown view" (($r.Code -eq 2) -and ($r.Err -match "nope") -and -not (Test-Path $png)) "exit=$($r.Code) $($r.Err)"

# 4. Unknown app: exit 2.
$r = Invoke-App @("--app", "word")
Check "unknown app" (($r.Code -eq 2) -and ($r.Err -match "word")) "exit=$($r.Code) $($r.Err)"

# 5. Tool list: 91 rows, 37 / 16 / 38, every unavailable row has a reason.
$json = Join-Path $Out "tools.json"
$r = Invoke-App @("--list-tools", "--json", $json)
$count = 0; $pdf = 0; $img = 0; $vid = 0; $noReason = 0; $avail = 0
if (Test-Path $json) {
    $doc = Get-Content -Raw $json | ConvertFrom-Json
    $count = @($doc.tools).Count
    $pdf = @($doc.tools | Where-Object { $_.app -eq "pdf" }).Count
    $img = @($doc.tools | Where-Object { $_.app -eq "image" }).Count
    $vid = @($doc.tools | Where-Object { $_.app -eq "video" }).Count
    $avail = @($doc.tools | Where-Object { $_.available }).Count
    $noReason = @($doc.tools | Where-Object { (-not $_.available) -and (-not $_.reason) }).Count
}
Check "list-tools" (($r.Code -eq 0) -and ($count -eq 91) -and ($pdf -eq 37) -and ($img -eq 16) -and ($vid -eq 38) -and ($noReason -eq 0)) `
    "exit=$($r.Code) total=$count pdf=$pdf image=$img video=$vid available=$avail unavailable-without-reason=$noReason $($r.Err)"
$lines = @($r.Out -split "`n" | Where-Object { $_.Trim() }).Count
Check "list-tools stdout" ($lines -eq 91) "lines=$lines"

# 6. One real tool run: the text file exists and is not empty.
$pdfIn = Join-Path $root "tests\fixtures\preview\Q3_Report.pdf"
$toolOut = Join-Path $Out "extract"
$result = Join-Path $Out "extract.json"
$r = Invoke-App @("--tool", "pdf-extract-text", "--in", $pdfIn, "--out", $toolOut, "--json", $result)
$ok = $false; $txtBytes = 0
if (Test-Path $result) {
    $doc = Get-Content -Raw $result | ConvertFrom-Json
    $ok = [bool]$doc.ok
    foreach ($o in @($doc.outputs)) { if (Test-Path $o) { $txtBytes += (Get-Item $o).Length } }
}
Check "tool pdf-extract-text" (($r.Code -eq 0) -and $ok -and ($txtBytes -gt 0)) "exit=$($r.Code) ok=$ok bytes=$txtBytes $($r.Err)"

# 7. An unavailable tool runs nothing: exit 2, no output folder.
$deadOut = Join-Path $Out "pdfa"
$r = Invoke-App @("--tool", "pdf-to-pdfa", "--in", $pdfIn, "--out", $deadOut)
Check "tool unavailable" (($r.Code -eq 2) -and -not (Test-Path $deadOut)) "exit=$($r.Code) $($r.Err)"

# 8. Built tools are reported available (25 pdf, 12 image, 31 video).
Check "built tools available" ($avail -ge 68) "available=$avail (expect at least 68; fewer means an engine probe failed on this PC)"

function Invoke-Tool([string]$Id, [string[]]$Inputs, [string[]]$Extra = @()) {
    $dir = Join-Path $Out ("run-" + $Id)
    $res = Join-Path $Out ("run-" + $Id + ".json")
    $a = @("--tool", $Id)
    foreach ($i in $Inputs) { $a += @("--in", $i) }
    $a += @("--out", $dir, "--json", $res) + $Extra
    $r = Invoke-App $a 300
    $ok = $false; $bytes = 0; $n = 0
    if (Test-Path $res) {
        $doc = Get-Content -Raw $res | ConvertFrom-Json
        $ok = [bool]$doc.ok
        foreach ($o in @($doc.outputs)) { if (Test-Path $o) { $n++; $bytes += (Get-Item $o).Length } }
    }
    Check "tool $Id" (($r.Code -eq 0) -and $ok -and ($n -ge 1) -and ($bytes -gt 0)) "exit=$($r.Code) ok=$ok outputs=$n bytes=$bytes $($r.Err)"
}

# 9. One real run per app through the exe.
$pdfB = Join-Path $root "src\NetieSpace\Assets\pdfjs\sample.pdf"
Invoke-Tool "pdf-merge" @($pdfIn, $pdfB)
Invoke-Tool "pdf-rotate" @($pdfIn)
Invoke-Tool "img-to-jpg" @((Join-Path $root "src\NetieSpace\Assets\netie-icon.png"))

# Tools that draw text need a real Windows font.
$icon = Join-Path $root "src\NetieSpace\Assets\netie-icon.png"
Invoke-Tool "pdf-to-jpg" @($pdfIn) @("--opt", "dpi=96")
Invoke-Tool "pdf-page-numbers" @($pdfIn)
Invoke-Tool "pdf-watermark" @($pdfIn) @("--opt", "text=DRAFT")
Invoke-Tool "pdf-protect" @($pdfIn) @("--opt", "password=check-only")
Invoke-Tool "img-resize" @($icon) @("--opt", "width=32")
Invoke-Tool "img-watermark" @($icon) @("--opt", "text=Netie")
Invoke-Tool "img-meme" @($icon) @("--opt", "top=Top line", "--opt", "bottom=Bottom line")

Invoke-Tool "pdf-to-word" @($pdfIn)
Invoke-Tool "pdf-to-powerpoint" @($pdfIn)
Invoke-Tool "img-blur-face" @($icon) @("--opt", "regions=8,8,24,24")
# These two need an OCR language and a speech voice on this PC. A PC without them must fail
# here with the reason, not pass: the tools list as available.
Invoke-Tool "pdf-ocr" @($pdfIn)
Invoke-Tool "vid-tts" @() @("--opt", "text=Netie check.")

# 10. Start Menu registration: write, read back with an independent reader, remove.
$programs = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$r = Invoke-App @("--register-apps")
$lnk = @("Netie PDF.lnk", "Netie Image.lnk", "Netie Video.lnk") | ForEach-Object { Join-Path $programs $_ }
$present = @($lnk | Where-Object { Test-Path $_ }).Count
Check "register-apps" (($r.Code -eq 0) -and ($present -eq 3)) "exit=$($r.Code) shortcuts=$present $($r.Out) $($r.Err)"
$reader = Join-Path $PSScriptRoot "check-app-shortcuts.ps1"
$readOut = & powershell -NoProfile -ExecutionPolicy Bypass -File $reader -ExePath $Exe 2>&1 | Out-String
$readCode = $LASTEXITCODE
Check "shortcut AUMID read-back" ($readCode -eq 0) "exit=$readCode $readOut"
$r = Invoke-App @("--unregister-apps")
$left = @($lnk | Where-Object { Test-Path $_ }).Count
Check "unregister-apps" (($r.Code -eq 0) -and ($left -eq 0)) "exit=$($r.Code) left=$left $($r.Out) $($r.Err)"

$ffmpeg = $null
$cmd = Get-Command ffmpeg -ErrorAction SilentlyContinue
if ($cmd) { $ffmpeg = $cmd.Source }
elseif (Test-Path (Join-Path $root "tools\ffmpeg\ffmpeg.exe")) { $ffmpeg = Join-Path $root "tools\ffmpeg\ffmpeg.exe" }
if (-not $ffmpeg) {
    Check "video fixture" $false "ffmpeg not on PATH and not in tools\ffmpeg; the video tools cannot be checked"
} else {
    $clip = Join-Path $Out "clip in.mp4"
    & $ffmpeg -hide_banner -loglevel error -y -f lavfi -i "testsrc=size=320x180:rate=25:duration=3" -f lavfi -i "sine=frequency=440:duration=3" -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest $clip
    Check "video fixture" ((Test-Path $clip) -and ((Get-Item $clip).Length -gt 0)) $clip
    Invoke-Tool "vid-mute" @($clip)
    Invoke-Tool "vid-trim" @($clip) @("--opt", "start=0.5", "--opt", "end=2")
    Invoke-Tool "vid-add-text" @($clip) @("--opt", "text=Netie Video")
    Invoke-Tool "vid-filters" @($clip)
    Invoke-Tool "vid-to-gif" @($clip)
    $srt = Join-Path $Out "it's subs.srt"
    Set-Content -Path $srt -Encoding ASCII -Value "1`r`n00:00:00,500 --> 00:00:02,000`r`nSubtitle check`r`n"
    Invoke-Tool "vid-subtitles" @($clip, $srt)
}

# Lane checks: every scripts/checks.d/*.ps1 is dot-sourced here, so it can call Invoke-App,
# Invoke-Tool and Check and read $Exe, $Out, $root, $pdfIn, $icon. One file per lane keeps
# parallel work from editing this script.
$laneDir = Join-Path $PSScriptRoot "checks.d"
if (Test-Path $laneDir) {
    foreach ($lane in (Get-ChildItem -Path $laneDir -Filter *.ps1 | Sort-Object Name)) {
        Write-Host "== lane check $($lane.Name)"
        . $lane.FullName
    }
}

if ($script:failed -gt 0) { Write-Host "FAILED $($script:failed) check(s)"; exit 1 }
Write-Host "PASS scripts/check-app-shell.ps1"
