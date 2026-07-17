#Requires -Version 5.1
param(
    [int]$Seconds = 8,
    [int]$MaxPdfs = 3
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'dist\NetieSpace\NetieSpace.exe'
$log = Join-Path $env:LOCALAPPDATA 'NetieSpace\logs\app.log'
$viewer = Join-Path $root 'dist\NetieSpace\Assets\pdfjs\netie-viewer.html'
$pdfjs = Join-Path $root 'dist\NetieSpace\Assets\pdfjs\build\pdf.mjs'

Write-Host '=== Netie Space PDF stress ===' -ForegroundColor Cyan

if (-not (Test-Path $exe)) { Write-Host 'Missing dist exe'; exit 1 }
if (-not (Test-Path $viewer) -or -not (Test-Path $pdfjs)) {
    Write-Host 'FAIL: pdf.js assets missing under dist'
    exit 1
}
Write-Host '[OK] pdf.js assets present'

$pdfs = @(Get-ChildItem (Join-Path $env:USERPROFILE 'Downloads\*.pdf') -ErrorAction SilentlyContinue |
    Sort-Object Length |
    Select-Object -First $MaxPdfs)
if ($pdfs.Count -eq 0) {
    Write-Host 'No PDFs in Downloads - skip preview matrix'
    exit 0
}

$fail = 0
foreach ($f in $pdfs) {
    $mb = [math]::Round($f.Length / 1MB, 1)
    Write-Host ''
    Write-Host ("Preview: {0} ({1} MB)" -f $f.Name, $mb) -ForegroundColor Gray
    Get-Process NetieSpace -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    $mark = Get-Date
    $p = Start-Process -FilePath $exe -ArgumentList @('--preview', $f.FullName) -PassThru
    Start-Sleep -Seconds $Seconds
    $alive = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
    $errors = @()
    if (Test-Path $log) {
        $errors = @(Get-Content $log | Where-Object {
            ($_ -match 'ERROR|Preview failed|UI unhandled') -and
            ($_ -match '^\d{4}-\d{2}-\d{2}')
        } | Where-Object {
            try {
                $ts = [datetime]::ParseExact($_.Substring(0, 19), 'yyyy-MM-dd HH:mm:ss', $null)
                $ts -ge $mark.AddSeconds(-2)
            } catch { $false }
        })
    }
    if (-not $alive) {
        Write-Host '[FAIL] Process died' -ForegroundColor Red
        $fail++
    } elseif ($errors.Count -gt 0) {
        Write-Host '[FAIL] New errors in log:' -ForegroundColor Red
        $errors | Select-Object -Last 5 | ForEach-Object { Write-Host $_ }
        $fail++
    } else {
        Write-Host '[OK] Alive, no new ERROR' -ForegroundColor Green
    }
    Get-Process NetieSpace -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 400
}

Write-Host ''
if ($fail -gt 0) {
    Write-Host ("PDF stress FAILED ({0})" -f $fail) -ForegroundColor Red
    exit 2
}
Write-Host ("PDF stress PASS ({0} files)" -f $pdfs.Count) -ForegroundColor Green
