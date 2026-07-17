#Requires -Version 5.1
param(
    [int]$MaxPdfs = 3
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'dist\NetieSpace\NetieSpace.exe'
$log = Join-Path $env:LOCALAPPDATA 'NetieSpace\logs\app.log'

Write-Host '=== Netie Space PDF sign stress ===' -ForegroundColor Cyan

if (-not (Test-Path $exe)) {
    Write-Host 'Missing dist exe — run scripts\publish.ps1 first' -ForegroundColor Red
    exit 1
}

$pdfs = @(Get-ChildItem (Join-Path $env:USERPROFILE 'Downloads\*.pdf') -ErrorAction SilentlyContinue |
    Where-Object { $_.BaseName -notmatch '_signed_' } |
    Sort-Object Length |
    Select-Object -First $MaxPdfs)

if ($pdfs.Count -eq 0) {
    Write-Host 'No PDFs in Downloads — skip sign matrix'
    exit 0
}

$fail = 0
foreach ($f in $pdfs) {
    Write-Host ''
    Write-Host ("Stamp test: {0}" -f $f.Name) -ForegroundColor Gray
    Get-Process NetieSpace -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
    $mark = Get-Date
    $p = Start-Process -FilePath $exe -ArgumentList @('--stamp-test', $f.FullName) -PassThru -Wait -NoNewWindow
    $out = $null
    if (Test-Path $log) {
        $line = Get-Content $log | Where-Object { $_ -match 'Stamp test OK' } | Select-Object -Last 1
        if ($line) { $out = $line }
    }

    if ($p.ExitCode -ne 0) {
        Write-Host ("[FAIL] Exit code {0}" -f $p.ExitCode) -ForegroundColor Red
        $fail++
        continue
    }

    $signed = Get-ChildItem (Join-Path $f.DirectoryName ($f.BaseName + '_signed_*.pdf')) |
        Where-Object { $_.LastWriteTime -ge $mark.AddSeconds(-5) } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $signed) {
        Write-Host '[FAIL] No signed sibling PDF created' -ForegroundColor Red
        $fail++
        continue
    }

    if ($signed.Length -lt ($f.Length * 0.9)) {
        Write-Host ("[WARN] Signed file smaller than source - may still be OK ({0} bytes)" -f $signed.Length) -ForegroundColor Yellow
    }

    Write-Host ("[OK] {0} ({1} bytes)" -f $signed.Name, $signed.Length) -ForegroundColor Green
}

Write-Host ''
if ($fail -gt 0) {
    Write-Host ("Sign stress FAILED ({0})" -f $fail) -ForegroundColor Red
    exit 2
}
Write-Host ("Sign stress PASS ({0} files)" -f $pdfs.Count) -ForegroundColor Green
