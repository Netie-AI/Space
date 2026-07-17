# Build installer after publish.ps1
# Requires Inno Setup 6: https://jrsoftware.org/isinfo.php

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$iss = Join-Path $root 'installer\NetieSpace.iss'
$exe = Join-Path $root 'dist\NetieSpace\NetieSpace.exe'

if (-not (Test-Path $exe)) {
    Write-Host "Run scripts\publish.ps1 first." -ForegroundColor Red
    exit 1
}

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host "Inno Setup not found. Install from https://jrsoftware.org/isinfo.php then re-run." -ForegroundColor Yellow
    Write-Host "ISS script ready at: $iss"
    exit 2
}

& $iscc $iss
Write-Host "Installer: dist\installer\NetieSpace-Setup.exe" -ForegroundColor Green
