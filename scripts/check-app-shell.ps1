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
foreach ($view in @("pdf-extract-text", "pdf-to-pdfa")) {
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

if ($script:failed -gt 0) { Write-Host "FAILED $($script:failed) check(s)"; exit 1 }
Write-Host "PASS scripts/check-app-shell.ps1"
