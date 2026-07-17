#Requires -Version 5.1
<#
.SYNOPSIS
  Install .NET 8 SDK + Desktop Runtime locally (no admin required).
.DESCRIPTION
  Downloads the REAL install script from dot.net/v1 (NOT https://dot.net which returns HTML).
  You likely already have everything - run with -CheckOnly first.
#>
param(
    [ValidateSet('sdk', 'runtime', 'desktop', 'all')]
    [string]$Install = 'all',
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$DotNetRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
$InstallScript = Join-Path $PSScriptRoot 'dotnet-install.ps1'
$ScriptUrl = 'https://dot.net/v1/dotnet-install.ps1'

function Add-DotNetToPath {
    if ($env:PATH -notlike "*$DotNetRoot*") {
        $env:PATH = "$DotNetRoot;$env:PATH"
    }
    $userPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
    if ($userPath -notlike "*$DotNetRoot*") {
        [Environment]::SetEnvironmentVariable('PATH', "$DotNetRoot;$userPath", 'User')
        Write-Host "[OK] Added to user PATH: $DotNetRoot" -ForegroundColor Green
    }
}

function Get-DotNetExe {
    $exe = Join-Path $DotNetRoot 'dotnet.exe'
    if (Test-Path $exe) { return $exe }
    return $null
}

Write-Host "`n=== Netie Space .NET Setup ===" -ForegroundColor Cyan
Write-Host "Install location: $DotNetRoot`n"

Add-DotNetToPath
$dotnet = Get-DotNetExe

if ($dotnet) {
    Write-Host "Installed SDKs:" -ForegroundColor Yellow
    & $dotnet --list-sdks
    Write-Host "`nInstalled Runtimes:" -ForegroundColor Yellow
    & $dotnet --list-runtimes
    Write-Host ""
}

$hasSdk = $dotnet -and ((& $dotnet --list-sdks 2>$null) -match '8\.')
$hasDesktop = $dotnet -and ((& $dotnet --list-runtimes 2>$null) -match 'Microsoft\.WindowsDesktop\.App 8\.')
$hasCore = $dotnet -and ((& $dotnet --list-runtimes 2>$null) -match 'Microsoft\.NETCore\.App 8\.')

if ($CheckOnly) {
    Write-Host "SDK 8.x:          $(if ($hasSdk) { 'YES' } else { 'MISSING' })" -ForegroundColor $(if ($hasSdk) { 'Green' } else { 'Red' })
    Write-Host "Desktop Runtime:  $(if ($hasDesktop) { 'YES' } else { 'MISSING' })" -ForegroundColor $(if ($hasDesktop) { 'Green' } else { 'Red' })
    Write-Host "Core Runtime:   $(if ($hasCore) { 'YES' } else { 'MISSING' })" -ForegroundColor $(if ($hasCore) { 'Green' } else { 'Red' })
    if ($hasSdk -and $hasDesktop) {
        Write-Host "`nYou are good - no install needed for Netie Space." -ForegroundColor Green
    }
    exit 0
}

if ($hasSdk -and $hasDesktop -and $Install -eq 'all') {
    Write-Host "Already have SDK + Desktop Runtime 8.x. Skipping install." -ForegroundColor Green
    Write-Host "Run with -CheckOnly to verify anytime.`n"
    exit 0
}

# Download the REAL script (not https://dot.net which is an HTML redirect page)
Write-Host "Downloading install script from $ScriptUrl ..." -ForegroundColor Gray
try {
    Invoke-WebRequest -Uri $ScriptUrl -OutFile $InstallScript -UseBasicParsing
    $head = Get-Content $InstallScript -TotalCount 3 -ErrorAction Stop
    if ($head -join ' ' -match '<html|DOCTYPE') {
        throw "Downloaded HTML instead of PowerShell script. Check your network/DNS."
    }
} catch {
    Write-Host "Primary URL failed, trying GitHub mirror..." -ForegroundColor Yellow
    $mirror = 'https://raw.githubusercontent.com/dotnet/install-scripts/main/src/dotnet-install.ps1'
    Invoke-WebRequest -Uri $mirror -OutFile $InstallScript -UseBasicParsing
}

$common = @('-InstallDir', $DotNetRoot, '-Channel', '8.0', '-Quality', 'ga', '-Architecture', 'x64')

switch ($Install) {
    'sdk'      { & $InstallScript @common -InstallType 'sdk' }
    'runtime'  { & $InstallScript @common -Runtime 'dotnet' }
    'desktop'  { & $InstallScript @common -Runtime 'windowsdesktop' }
    'all'      {
        if (-not $hasSdk)     { & $InstallScript @common -InstallType 'sdk' }
        if (-not $hasCore)    { & $InstallScript @common -Runtime 'dotnet' }
        if (-not $hasDesktop) { & $InstallScript @common -Runtime 'windowsdesktop' }
    }
}

Add-DotNetToPath
$dotnet = Get-DotNetExe
if ($dotnet) {
    Write-Host "`nDone. Version:" -ForegroundColor Green
    & $dotnet --version
    Write-Host "`nRun: .\run.bat" -ForegroundColor Cyan
}
