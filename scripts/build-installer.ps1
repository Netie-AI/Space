#Requires -Version 5.1
<#
.SYNOPSIS
  Compile installer\NetieSpace.iss into dist\installer\NetieSpace-Setup.exe (run publish.ps1 first).
.PARAMETER Dist
  Published folder. Default dist\NetieSpace.
.NOTES
  Exit 0 ok, 1 no published exe / compile failed, 2 Inno Setup 6 not installed.
#>
param([string]$Dist)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$iss = Join-Path $root 'installer\NetieSpace.iss'
if (-not $Dist) { $Dist = Join-Path $root 'dist\NetieSpace' }
$exe = Join-Path $Dist 'NetieSpace.exe'
$outDir = Join-Path $root 'dist\installer'

if (-not (Test-Path $exe)) {
    Write-Host "Run scripts\publish.ps1 first." -ForegroundColor Red
    exit 1
}

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $iscc) {
    Write-Host "Inno Setup 6 not found. Install from https://jrsoftware.org/isinfo.php then re-run." -ForegroundColor Yellow
    Write-Host "ISS script ready at: $iss"
    exit 2
}

# One version source: the csproj.
$csproj = Join-Path $root 'src\NetieSpace\NetieSpace.csproj'
$m = [regex]::Match((Get-Content -LiteralPath $csproj -Raw), '<Version>\s*([0-9][0-9A-Za-z.\-+]*)\s*</Version>')
if (-not $m.Success) { Write-Host "No <Version> in $csproj" -ForegroundColor Red; exit 1 }
$version = $m.Groups[1].Value

& $iscc "/DMyAppVersion=$version" "/DDistDir=$((Resolve-Path $Dist).Path)" "/DOutDir=$outDir" $iss
if ($LASTEXITCODE -ne 0) { Write-Host "ISCC failed with $LASTEXITCODE" -ForegroundColor Red; exit 1 }

$setup = Join-Path $outDir 'NetieSpace-Setup.exe'
if (-not (Test-Path $setup)) { Write-Host "Setup exe not produced: $setup" -ForegroundColor Red; exit 1 }
Write-Host ("Installer: {0} ({1} MB, version {2})" -f $setup, [math]::Round((Get-Item $setup).Length / 1MB, 1), $version) -ForegroundColor Green
exit 0
