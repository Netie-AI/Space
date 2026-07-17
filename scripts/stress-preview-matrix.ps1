#Requires -Version 5.1
<#
.SYNOPSIS
  Stress-test Netie Space preview across every fixture file type (SVG, code, images, PDF, …).
#>
param(
    [int]$Seconds = 5,
    [string]$FixtureDir = ""
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'dist\NetieSpace\NetieSpace.exe'
$log = Join-Path $env:LOCALAPPDATA 'NetieSpace\logs\app.log'
if (-not $FixtureDir) {
    $FixtureDir = Join-Path $root 'tests\fixtures\preview'
}

Write-Host '=== Netie Space preview matrix stress ===' -ForegroundColor Cyan

if (-not (Test-Path $exe)) {
    Write-Host 'Missing dist exe — building portable package...' -ForegroundColor Yellow
    $publish = Join-Path $root 'scripts\publish.ps1'
    if (Test-Path $publish) {
        powershell -ExecutionPolicy Bypass -File $publish
    }
    if (-not (Test-Path $exe)) {
        Write-Host 'FAIL: still no dist\NetieSpace\NetieSpace.exe'
        exit 1
    }
}

if (-not (Test-Path $FixtureDir)) {
    Write-Host "FAIL: fixtures missing: $FixtureDir"
    exit 1
}

$codeViewer = Join-Path $root 'dist\NetieSpace\Assets\code-viewer\highlight.min.js'
if (-not (Test-Path $codeViewer)) {
    Write-Host '[WARN] code-viewer assets missing under dist — rebuild needed for syntax highlight' -ForegroundColor Yellow
}

$files = @(Get-ChildItem $FixtureDir -File | Sort-Object Extension, Name)
if ($files.Count -eq 0) {
    Write-Host 'FAIL: no fixture files'
    exit 1
}

Write-Host ("Fixtures: {0} files in {1}" -f $files.Count, $FixtureDir)

$fail = 0
$pass = 0
$results = @()

foreach ($f in $files) {
    $mb = [math]::Round($f.Length / 1KB, 1)
    Write-Host ''
    Write-Host ("Preview: {0} ({1} KB)" -f $f.Name, $mb) -ForegroundColor Gray
    Get-Process NetieSpace -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 400
    $mark = Get-Date
    $p = Start-Process -FilePath $exe -ArgumentList @('--preview', $f.FullName) -PassThru
    Start-Sleep -Seconds $Seconds
    $alive = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
    $errors = @()
    if (Test-Path $log) {
        $errors = @(Get-Content $log | Where-Object {
            ($_ -match 'ERROR|Preview failed|UI unhandled|Could not load') -and
            ($_ -match '^\d{4}-\d{2}-\d{2}')
        } | Where-Object {
            try {
                $ts = [datetime]::ParseExact($_.Substring(0, 19), 'yyyy-MM-dd HH:mm:ss', $null)
                $ts -ge $mark.AddSeconds(-2)
            } catch { $false }
        })
    }

    $status = 'OK'
    if (-not $alive) {
        $status = 'DIED'
        $fail++
        Write-Host '[FAIL] Process died' -ForegroundColor Red
    } elseif ($errors.Count -gt 0) {
        $status = 'LOG_ERROR'
        $fail++
        Write-Host '[FAIL] New errors in log:' -ForegroundColor Red
        $errors | Select-Object -Last 4 | ForEach-Object { Write-Host $_ }
    } else {
        $pass++
        Write-Host '[OK] Alive, no new ERROR' -ForegroundColor Green
    }

    $results += [pscustomobject]@{ File = $f.Name; Ext = $f.Extension; Status = $status }
    Get-Process NetieSpace -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 300
}

Write-Host ''
Write-Host '--- Matrix ---' -ForegroundColor Cyan
$results | Format-Table -AutoSize
Write-Host ("PASS {0} / FAIL {1} / TOTAL {2}" -f $pass, $fail, $files.Count)

if ($fail -gt 0) {
    Write-Host 'Preview matrix FAILED' -ForegroundColor Red
    exit 2
}
Write-Host 'Preview matrix PASS' -ForegroundColor Green
exit 0
