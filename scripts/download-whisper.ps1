#Requires -Version 5.1
<#
.SYNOPSIS
  Download whisper.cpp Windows CPU binary + ggml-small model for local ZH+EN captions.
  Always installs whisper-cli.exe — never the deprecated main.exe stub (exits 1, no SRT).
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $root 'tools\whisper'
$models = Join-Path $dest 'models'
New-Item -ItemType Directory -Force -Path $models | Out-Null

Write-Host "=== whisper.cpp for Netie captions ===" -ForegroundColor Cyan

$modelUrl = 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin'
$modelPath = Join-Path $models 'ggml-small.bin'

if (-not (Test-Path $modelPath)) {
    Write-Host "Downloading ggml-small.bin (~488 MB)..." -ForegroundColor Gray
    Invoke-WebRequest -Uri $modelUrl -OutFile $modelPath -UseBasicParsing
    Write-Host "[OK] Model: $modelPath" -ForegroundColor Green
} else {
    Write-Host "[OK] Model already present" -ForegroundColor Green
}

$cliPath = Join-Path $dest 'whisper-cli.exe'
$stub = Join-Path $dest 'main.exe'
if (Test-Path $stub) {
    $len = (Get-Item $stub).Length
    if ($len -lt 100000) {
        Write-Host "Removing deprecated main.exe stub ($len bytes)..." -ForegroundColor Yellow
        Remove-Item $stub -Force -ErrorAction SilentlyContinue
    }
}

$needCli = -not (Test-Path $cliPath) -or ((Get-Item $cliPath).Length -lt 100000)
if ($needCli) {
    Write-Host "Downloading whisper.cpp Windows x64 binary (whisper-cli.exe)..." -ForegroundColor Gray
    $zipUrl = 'https://github.com/ggml-org/whisper.cpp/releases/download/v1.7.6/whisper-bin-x64.zip'
    $zipPath = Join-Path $env:TEMP "whisper-bin-x64.zip"
    $extract = Join-Path $env:TEMP "whisper-bin-x64-extract"
    try {
        Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing
        if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
        Expand-Archive -Path $zipPath -DestinationPath $extract -Force
        $found = Get-ChildItem -Path $extract -Recurse -Filter 'whisper-cli.exe' |
            Select-Object -First 1
        if ($found) {
            Copy-Item $found.FullName -Destination $cliPath -Force
            Get-ChildItem $found.Directory.FullName -Filter '*.dll' -ErrorAction SilentlyContinue |
                ForEach-Object { Copy-Item $_.FullName -Destination $dest -Force }
            Write-Host "[OK] CLI: $cliPath ($((Get-Item $cliPath).Length) bytes)" -ForegroundColor Green
        } else {
            Write-Host "Zip downloaded but whisper-cli.exe not found. Unzip manually from:" -ForegroundColor Yellow
            Write-Host "  https://github.com/ggml-org/whisper.cpp/releases"
            Write-Host "Into: $dest" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "Auto-download failed: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "Manual: https://github.com/ggml-org/whisper.cpp/releases -> whisper-bin-x64.zip -> $dest" -ForegroundColor Yellow
    }
} else {
    Write-Host "[OK] CLI: $cliPath" -ForegroundColor Green
}

Write-Host ""
Write-Host "Usage in Netie: Edit video then Captions" -ForegroundColor Cyan
