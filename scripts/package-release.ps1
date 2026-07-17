#Requires -Version 5.1
<#
.SYNOPSIS
  Build a shareable Netie Space zip (no personal API keys).
#>
param(
    [switch]$IncludeVenv,
    [switch]$NoSingleFile
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist\NetieSpace'
$zipPath = Join-Path $root 'dist\NetieSpace-Setup.zip'
$stage = Join-Path $root 'dist\NetieSpace-Release'

Write-Host "`n=== Packaging Netie Space release ===" -ForegroundColor Cyan

$publishArgs = @{}
if ($IncludeVenv) { $publishArgs['IncludeVenv'] = $true }
if ($NoSingleFile) { $publishArgs['NoSingleFile'] = $true }

& (Join-Path $PSScriptRoot 'publish.ps1') @publishArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Rebuild stage without secrets
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item (Join-Path $dist '*') $stage -Recurse -Force

# Strip secrets
$secretEnv = Join-Path $stage '.env'
if (Test-Path $secretEnv) {
    Remove-Item $secretEnv -Force
    Write-Host "[OK] Removed .env secrets from release package" -ForegroundColor Green
}

# .env.example
@'
# Optional — or use in-app: tray icon → API Keys…
GROQ_API_KEY=
OPENROUTER_API_KEY=
SEA_LION_API_KEY=
'@ | Set-Content (Join-Path $stage '.env.example') -Encoding UTF8

# Install.bat for recipients
@'
@echo off
setlocal
cd /d "%~dp0"

echo.
echo  Netie Space — Install
echo  =====================
echo.

tasklist /FI "IMAGENAME eq NetieSpace.exe" /NH 2>nul | find /I "NetieSpace.exe" >nul
if not errorlevel 1 (
    echo  Stopping running NetieSpace.exe...
    taskkill /IM NetieSpace.exe /T >nul 2>&1
    timeout /T 2 /NOBREAK >nul
)

powershell -ExecutionPolicy Bypass -NoProfile -Command ^
  "$exe = Join-Path '%~dp0' 'NetieSpace.exe'; " ^
  "Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'NetieSpace' -Value ('\"' + $exe + '\"'); " ^
  "Write-Host '[OK] Registered startup'"

start "" "%~dp0NetieSpace.exe"

echo.
echo  Installed and started.
echo  - Select a file in Explorer or Desktop, press Space to preview
echo  - Space again closes preview
echo  - Tray (blue dot) → API Keys… for optional AI
echo  - Buy API is currently unavailable
echo.
pause
'@ | Set-Content (Join-Path $stage 'Install.bat') -Encoding ASCII

@'
Netie Space — Install
=====================

1. Unzip this folder anywhere (e.g. Documents\NetieSpace).
2. Run Install.bat (registers auto-start and launches the app).
3. Select a file in File Explorer or on the Desktop → press Space.
4. Press Space again to close the preview (Esc also works).

Optional AI
-----------
Tray icon (blue dot) → API Keys…
  - Paste a Groq and/or OpenRouter key
  - "Buy API" is currently unavailable

Without a key you can still preview files and use ↓ MD / ↓ PDF
(conversion needs Python + packages; see README).

Requirements
------------
- Windows 10/11 x64
- Microsoft Edge WebView2 Runtime (usually preinstalled)
- No separate .NET install needed (runtime is bundled)
'@ | Set-Content (Join-Path $stage 'INSTALL.txt') -Encoding UTF8

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipPath -Force

$sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "`nRelease zip: $zipPath ($sizeMb MB)" -ForegroundColor Green
Write-Host "Upload this zip for others to download." -ForegroundColor Cyan
