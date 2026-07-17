#Requires -Version 5.1
<#
.SYNOPSIS
  One-shot setup: check .NET, install if missing, install SF Pro fonts, build app.
#>
param([switch]$SkipBuild)

$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

Write-Host "=== Netie Space Setup ===" -ForegroundColor Cyan

# 1. .NET
& "$PSScriptRoot\install-dotnet.ps1" -CheckOnly
$dotnetOk = $LASTEXITCODE -eq 0
if (-not $dotnetOk) {
    & "$PSScriptRoot\install-dotnet.ps1"
}

# 2. SF Pro (optional — won't fail setup)
& "$PSScriptRoot\install-sfpro.ps1"
# exit 1 from sfpro is OK — Segoe UI Variable fallback exists

# 3. Python venv deps
if (Test-Path ".\.venv\Scripts\pip.exe") {
    Write-Host "`nInstalling Python deps..." -ForegroundColor Cyan
    .\.venv\Scripts\pip.exe install -q -r requirements.txt
}

# 4. Build
if (-not $SkipBuild) {
    $env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
    Write-Host "`nBuilding..." -ForegroundColor Cyan
    dotnet build NetieSpace.sln -c Release
}

Write-Host "`n=== Setup complete ===" -ForegroundColor Green
Write-Host "Run: .\run.bat" -ForegroundColor Cyan
