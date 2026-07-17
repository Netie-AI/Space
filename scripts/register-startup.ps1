#Requires -Version 5.1
param(
    [string]$ExePath = ""
)

$root = Split-Path $PSScriptRoot -Parent
if (-not $ExePath) {
    $ExePath = Join-Path $root 'dist\NetieSpace\NetieSpace.exe'
}

if (-not (Test-Path $ExePath)) {
    Write-Host "Exe not found: $ExePath" -ForegroundColor Red
    Write-Host "Run scripts\publish.ps1 first." -ForegroundColor Yellow
    exit 1
}

$ExePath = (Resolve-Path $ExePath).Path
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Set-ItemProperty -Path $runKey -Name 'NetieSpace' -Value "`"$ExePath`""

Write-Host "[OK] Netie Space will auto-start on login." -ForegroundColor Green
Write-Host "     $ExePath" -ForegroundColor Gray
Write-Host "`nTo remove: Remove-ItemProperty HKCU:\Software\Microsoft\Windows\CurrentVersion\Run -Name NetieSpace" -ForegroundColor Gray
