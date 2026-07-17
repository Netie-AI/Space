#Requires -Version 5.1
<#
.SYNOPSIS
  Install SF Pro fonts on Windows for Netie Space UI.
.NOTES
  .dmg files are macOS disk images — Windows cannot mount them natively.
  This script tries: (1) existing .otf/.ttf folder, (2) 7-Zip DMG extract, (3) manual instructions.
#>
param(
    [string]$Source = "$env:USERPROFILE\Downloads\SF-Pro.dmg",
    [string]$FontsDir = "$env:LOCALAPPDATA\Microsoft\Windows\Fonts"
)

$ErrorActionPreference = 'Stop'

function Install-FontFile([string]$fontPath) {
    $name = Split-Path $fontPath -Leaf
    $dest = Join-Path $FontsDir $name
    if (-not (Test-Path $dest)) {
        Copy-Item $fontPath $dest -Force
    }
    # Register with Windows (per-user, no admin)
    $regPath = 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Fonts'
    $displayName = "$name (TrueType)"
    if (-not (Get-ItemProperty $regPath -ErrorAction SilentlyContinue | Get-Member -Name $displayName -ErrorAction SilentlyContinue)) {
        New-ItemProperty -Path $regPath -Name $displayName -Value $name -PropertyType String -Force | Out-Null
    }
    Write-Host "  [OK] $name" -ForegroundColor Green
}

function Find-SfProFonts([string]$root) {
    Get-ChildItem -Path $root -Recurse -Include '*.otf','*.ttf','*.ttc' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match 'SF.?Pro|SanFrancisco|SFPro' }
}

Write-Host "`n=== SF Pro Font Installer ===" -ForegroundColor Cyan

# Already installed?
$installed = Get-ChildItem 'C:\Windows\Fonts' -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match 'SFPro|SF Pro|San Francisco' }
if ($installed) {
    Write-Host "SF Pro already in C:\Windows\Fonts:" -ForegroundColor Green
    $installed | ForEach-Object { Write-Host "  $($_.Name)" }
    exit 0
}

$extractDir = Join-Path $env:TEMP "sfpro-extract"
if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force }
New-Item -ItemType Directory -Path $extractDir -Force | Out-Null

$fonts = @()

# 1) Look for loose font files in Downloads (best path on Windows)
$downloads = Join-Path $env:USERPROFILE 'Downloads'
foreach ($folder in @(
    (Join-Path $downloads 'SF-Pro-Fonts'),
    (Join-Path $downloads 'SF Pro'),
    $downloads
)) {
    if (Test-Path $folder) { $fonts += Find-SfProFonts $folder }
}

# 2) Try portable 7-Zip on .dmg (bundled or system)
$sevenZip = @(
    (Join-Path $PSScriptRoot '7zip-full\7z.exe'),
    (Join-Path $PSScriptRoot '7zip\7z.exe'),
    'C:\Program Files\7-Zip\7z.exe',
    'C:\Program Files (x86)\7-Zip\7z.exe',
    (Get-Command 7z -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if ($fonts.Count -eq 0 -and (Test-Path $Source) -and $sevenZip) {
    Write-Host "Extracting DMG with 7-Zip: $Source" -ForegroundColor Yellow
    & $sevenZip x $Source "-o$extractDir" -y | Out-Null
    $fonts += Find-SfProFonts $extractDir
}

# 3) Common extract subfolder names from Apple DMG
foreach ($sub in @('SF Pro', 'SFPro', 'Fonts', 'Library/Fonts')) {
    $p = Join-Path $extractDir $sub
    if (Test-Path $p) { $fonts += Find-SfProFonts $p }
}

$fonts = $fonts | Sort-Object FullName -Unique

if ($fonts.Count -eq 0) {
    Write-Host @"

Could not find SF Pro font files.

DMG files are macOS-only disk images. On Windows you need the actual .otf/.ttf files:

Option A - Extract on Mac, copy to Windows (recommended):
  1. Open SF-Pro.dmg on a Mac
  2. Copy all .otf files from the Fonts folder
  3. Create folder: $downloads\SF-Pro-Fonts\
  4. Paste .otf files there
  5. Run: .\install-sfpro.bat

Option B - Install 7-Zip (admin prompt), then re-run:
  winget install 7zip.7zip

Option C - Apple Developer fonts page:
  https://developer.apple.com/fonts/

Netie Space will use Segoe UI Variable until SF Pro is installed
(it is already configured as the primary fallback in the font stack).

"@ -ForegroundColor Yellow
    exit 1
}

Write-Host "Installing $($fonts.Count) font file(s) to user fonts..." -ForegroundColor Cyan
if (-not (Test-Path $FontsDir)) { New-Item -ItemType Directory -Path $FontsDir -Force | Out-Null }

foreach ($f in $fonts) {
    Install-FontFile $f.FullName
}

Write-Host "`nDone. Restart Netie Space to pick up SF Pro." -ForegroundColor Green
Write-Host "Installed fonts use family names: 'SF Pro Display', 'SF Pro Text'" -ForegroundColor Gray
