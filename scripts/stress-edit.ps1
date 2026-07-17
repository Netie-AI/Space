#Requires -Version 5.1
param(
    [int]$Seconds = 25,
    [int]$LaunchCount = 3
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'dist\NetieSpace\NetieSpace.exe'
$log = Join-Path $env:LOCALAPPDATA 'NetieSpace\logs\app.log'

if (-not (Test-Path $exe)) {
    Write-Host "Missing dist exe. Run scripts\publish.ps1 -IncludeEnv first." -ForegroundColor Red
    exit 1
}

Write-Host "=== Netie Space stress smoke ===" -ForegroundColor Cyan
Write-Host "Launches=$LaunchCount Seconds=$Seconds"

for ($i = 1; $i -le $LaunchCount; $i++) {
    Write-Host ""
    Write-Host "[$i/$LaunchCount] Launch..." -ForegroundColor Gray
    Get-Process NetieSpace -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 400
    $p = Start-Process -FilePath $exe -PassThru
    Start-Sleep -Seconds $Seconds
    $alive = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
    if (-not $alive) {
        Write-Host "[FAIL] Process exited early. Check log:" -ForegroundColor Red
        Write-Host $log
        if (Test-Path $log) { Get-Content $log -Tail 40 }
        exit 2
    }
    Write-Host "[OK] Still running pid=$($p.Id)" -ForegroundColor Green
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

if (Test-Path $log) {
    Write-Host ""
    Write-Host "--- Recent log ---" -ForegroundColor Cyan
    Get-Content $log -Tail 25
}

Write-Host ""
Write-Host "Running PDF matrix..." -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'stress-pdf.ps1') -Seconds 6 -MaxPdfs 3
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Manual: Space on PDF - zoom, scroll, Edit, thumbs. Then mp4 Edit 10x." -ForegroundColor Yellow
Write-Host "Done." -ForegroundColor Green
