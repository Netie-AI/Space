#Requires -Version 5.1
<#
.SYNOPSIS
  Publish self-contained Netie Space and launch it (correct route — never framework-only).
#>
param([switch]$NoLaunch)

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'publish.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\NetieSpace\NetieSpace.exe'
if (-not $NoLaunch) {
    Start-Process $exe
    Write-Host "Launched $exe" -ForegroundColor Green
}
