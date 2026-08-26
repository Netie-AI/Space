#Requires -Version 5.1
<#
.SYNOPSIS
  Build a portable self-contained Netie Space package (no .NET install needed on target PC).
#>
param(
    [switch]$NoSingleFile,
    [switch]$RegisterStartup,
    [switch]$IncludeVenv,
    [switch]$IncludeEnv,
    [ValidateRange(1, 20)]
    [int]$MaxRetries = 5,
    [ValidateRange(1, 60)]
    [int]$RetryDelaySeconds = 2
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist\NetieSpace'
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"

function Invoke-WithRetry {
    param(
        [Parameter(Mandatory)]
        [scriptblock]$Operation,
        [Parameter(Mandatory)]
        [string]$Description
    )

    for ($attempt = 1; $attempt -le $MaxRetries; $attempt++) {
        try {
            & $Operation
            return
        } catch {
            if ($attempt -eq $MaxRetries) {
                throw "$Description failed after $MaxRetries attempts. $($_.Exception.Message)"
            }

            Write-Host "[WARN] $Description failed (attempt $attempt/$MaxRetries): $($_.Exception.Message)" -ForegroundColor Yellow
            Start-Sleep -Seconds $RetryDelaySeconds
        }
    }
}

function Stop-NetieSpace {
    $processes = @(Get-Process -Name 'NetieSpace' -ErrorAction SilentlyContinue)
    if ($processes.Count -eq 0) { return }

    Write-Host "Stopping running NetieSpace.exe..." -ForegroundColor Yellow
    # Force-kill first so dist\NetieSpace.exe can be replaced (CloseMainWindow often fails for tray apps)
    foreach ($process in $processes) {
        try { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue } catch { }
    }

    Invoke-WithRetry -Description 'Waiting for NetieSpace.exe to release its files' -Operation {
        Start-Sleep -Milliseconds 400
        if (Get-Process -Name 'NetieSpace' -ErrorAction SilentlyContinue) {
            Get-Process -Name 'NetieSpace' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
            throw 'The process is still running.'
        }
    }

    Write-Host "[OK] NetieSpace.exe stopped" -ForegroundColor Green
}

Write-Host "`n=== Publishing Netie Space (self-contained) ===" -ForegroundColor Cyan

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "dotnet not found. Run scripts\install-dotnet.ps1 first." -ForegroundColor Red
    exit 1
}

$publishArgs = @(
    'publish', (Join-Path $root 'src\NetieSpace\NetieSpace.csproj'),
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishReadyToRun=false',
    '-o', $dist
)

if (-not $NoSingleFile) {
    $publishArgs += @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true')
}

Invoke-WithRetry -Description 'Publishing Netie Space' -Operation {
    Stop-NetieSpace

    if (Test-Path $dist) {
        Remove-Item $dist -Recurse -Force
    }
    New-Item -ItemType Directory -Path $dist -Force | Out-Null

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish exited with code $LASTEXITCODE."
    }
}

# Fresh clone has no tools/ffmpeg (gitignored). Playbook Export and
# "re-run install.bat" need ffmpeg next to the exe. Download is a no-op
# when ffmpeg.exe is already there. Network fail must not abort publish.
$ffmpegGet = Join-Path $PSScriptRoot 'download-ffmpeg.ps1'
if (Test-Path $ffmpegGet) {
    try {
        & $ffmpegGet
    } catch {
        Write-Host "[WARN] ffmpeg download: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# Bundle tools + config for portable use
$toolsSrc = Join-Path $root 'tools'
if (Test-Path $toolsSrc) {
    Invoke-WithRetry -Description 'Copying tools' -Operation {
        Copy-Item $toolsSrc (Join-Path $dist 'tools') -Recurse -Force
    }
    Write-Host "[OK] Copied tools/" -ForegroundColor Green
}

# pdf.js viewer assets (single-file publish often skips nested .mjs)
$pdfjsSrc = Join-Path $root 'src\NetieSpace\Assets\pdfjs'
$pdfjsDst = Join-Path $dist 'Assets\pdfjs'
if (Test-Path $pdfjsSrc) {
    Invoke-WithRetry -Description 'Copying pdf.js assets' -Operation {
        New-Item -ItemType Directory -Force -Path $pdfjsDst | Out-Null
        Copy-Item (Join-Path $pdfjsSrc '*') $pdfjsDst -Recurse -Force
    }
    Write-Host "[OK] Copied Assets/pdfjs/" -ForegroundColor Green
}

# highlight.js for code / SVG-adjacent WebView assets
$codeSrc = Join-Path $root 'src\NetieSpace\Assets\code-viewer'
$codeDst = Join-Path $dist 'Assets\code-viewer'
if (Test-Path $codeSrc) {
    Invoke-WithRetry -Description 'Copying code-viewer assets' -Operation {
        New-Item -ItemType Directory -Force -Path $codeDst | Out-Null
        Copy-Item (Join-Path $codeSrc '*') $codeDst -Recurse -Force
    }
    Write-Host "[OK] Copied Assets/code-viewer/" -ForegroundColor Green
}

# Bundle icon next to portable exe
$icoSrc = Join-Path $root 'src\NetieSpace\Assets\netie.ico'
if (Test-Path $icoSrc) {
    $icoDst = Join-Path $dist 'Assets'
    New-Item -ItemType Directory -Force -Path $icoDst | Out-Null
    Copy-Item $icoSrc (Join-Path $icoDst 'netie.ico') -Force
    Write-Host "[OK] Copied Assets/netie.ico" -ForegroundColor Green
}

$envSrc = Join-Path $root '.env'
# Dev-only: customer releases must NOT ship API keys. Use -IncludeEnv for local portable testing.
if ($IncludeEnv -and (Test-Path $envSrc)) {
    Invoke-WithRetry -Description 'Copying .env' -Operation {
        Copy-Item $envSrc (Join-Path $dist '.env') -Force
    }
    Write-Host "[OK] Copied .env (IncludeEnv)" -ForegroundColor Green
} elseif (Test-Path $envSrc) {
    Write-Host "[SKIP] .env not copied (omit secrets from customer builds). Use -IncludeEnv for local." -ForegroundColor Yellow
} else {
    Write-Host "[WARN] No .env found" -ForegroundColor Yellow
}

# Copy python venv if requested (large, enables MD/PDF conversion offline)
$venvSrc = Join-Path $root '.venv'
if ($IncludeVenv -and (Test-Path $venvSrc)) {
    Write-Host "Copying .venv (may take a minute)..." -ForegroundColor Gray
    Invoke-WithRetry -Description 'Copying .venv' -Operation {
        Copy-Item $venvSrc (Join-Path $dist '.venv') -Recurse -Force
    }
    Write-Host "[OK] Copied .venv" -ForegroundColor Green
}

# Launcher
Invoke-WithRetry -Description 'Writing launcher' -Operation {
    @'
@echo off
cd /d "%~dp0"
start "" "%~dp0NetieSpace.exe"
'@ | Set-Content (Join-Path $dist 'Start Netie Space.bat') -Encoding ASCII
}

$exe = Join-Path $dist 'NetieSpace.exe'
$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)

Write-Host "`nPublished to: $dist" -ForegroundColor Green
Write-Host "Exe size: ${sizeMb} MB (includes .NET runtime - no install needed)" -ForegroundColor Green
Write-Host "`nRun: dist\NetieSpace\NetieSpace.exe" -ForegroundColor Cyan
Write-Host "Or:  dist\NetieSpace\Start Netie Space.bat" -ForegroundColor Cyan

# Stage AirGPT connector next to portable build
$pluginSrc = Join-Path $root 'airgpt-plugin'
$pluginDst = Join-Path $dist 'airgpt-plugin'
if (Test-Path $pluginSrc) {
    New-Item -ItemType Directory -Force -Path $pluginDst | Out-Null
    Copy-Item (Join-Path $pluginSrc '*') $pluginDst -Recurse -Force
    Write-Host "[OK] Bundled airgpt-plugin/" -ForegroundColor Green
}

if ($RegisterStartup) {
    & (Join-Path $PSScriptRoot 'register-startup.ps1') -ExePath $exe
}
