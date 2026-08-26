#Requires -Version 5.1
<#
.SYNOPSIS
  Ensure whisper.cpp captions + rembg Remove BG are ready for Netie Space.
  Safe to re-run; skips work when already present.
#>
$ErrorActionPreference = 'Continue'
$root = Split-Path $PSScriptRoot -Parent
Write-Host "=== Ensure AI tools (ffmpeg + whisper + rembg) ===" -ForegroundColor Cyan

# 0) ffmpeg for Export (gitignored tools/ is empty on a fresh clone)
$ffmpegScript = Join-Path $PSScriptRoot 'download-ffmpeg.ps1'
if (Test-Path $ffmpegScript) {
    try {
        & powershell -ExecutionPolicy Bypass -File $ffmpegScript
    } catch {
        Write-Host "[WARN] ffmpeg download: $($_.Exception.Message)" -ForegroundColor Yellow
    }
} else {
    Write-Host "[WARN] missing scripts\download-ffmpeg.ps1" -ForegroundColor Yellow
}

$ffmpegSrc = Join-Path $root 'tools\ffmpeg'
$ffmpegDst = Join-Path $root 'dist\NetieSpace\tools\ffmpeg'
if ((Test-Path (Join-Path $ffmpegSrc 'ffmpeg.exe')) -and (Test-Path (Join-Path $root 'dist\NetieSpace'))) {
    New-Item -ItemType Directory -Force -Path $ffmpegDst | Out-Null
    Copy-Item (Join-Path $ffmpegSrc '*') $ffmpegDst -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "[OK] ffmpeg copied to dist" -ForegroundColor Green
}

# 1) Whisper captions
$whisperScript = Join-Path $PSScriptRoot 'download-whisper.ps1'
if (Test-Path $whisperScript) {
    try {
        & powershell -ExecutionPolicy Bypass -File $whisperScript
    } catch {
        Write-Host "[WARN] whisper download: $($_.Exception.Message)" -ForegroundColor Yellow
    }
} else {
    Write-Host "[WARN] missing scripts\download-whisper.ps1" -ForegroundColor Yellow
}

# Copy whisper into dist if present
$whisperSrc = Join-Path $root 'tools\whisper'
$whisperDst = Join-Path $root 'dist\NetieSpace\tools\whisper'
if ((Test-Path $whisperSrc) -and (Test-Path (Join-Path $root 'dist\NetieSpace'))) {
    New-Item -ItemType Directory -Force -Path $whisperDst | Out-Null
    Copy-Item (Join-Path $whisperSrc '*') $whisperDst -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "[OK] whisper copied to dist" -ForegroundColor Green
}

# 2) rembg in project .venv
$py = Join-Path $root '.venv\Scripts\python.exe'
if (-not (Test-Path $py)) {
    $py = Join-Path $root 'optional\.venv312\Scripts\python.exe'
}
if (Test-Path $py) {
    Write-Host "Checking rembg via $py ..." -ForegroundColor Gray
    & $py -c "import rembg" 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Installing rembg (one-time, may take a few minutes)..." -ForegroundColor Yellow
        $req = Join-Path $root 'optional\requirements-rembg.txt'
        if (Test-Path $req) {
            & $py -m pip install -r $req
        } else {
            & $py -m pip install rembg
        }
    } else {
        Write-Host "[OK] rembg already installed" -ForegroundColor Green
    }
} else {
    Write-Host "[SKIP] No .venv found. Remove BG will auto-pip on first use if Python is available." -ForegroundColor Yellow
}

Write-Host "=== AI tools check done ===" -ForegroundColor Cyan
