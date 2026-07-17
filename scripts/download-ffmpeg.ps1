#Requires -Version 5.1
<#
.SYNOPSIS
  Download FFmpeg essentials into tools/ffmpeg for video crop/merge/cut.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $root 'tools\ffmpeg'
$zip = Join-Path $env:TEMP 'ffmpeg-essentials.zip'
$url = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'

if (Test-Path (Join-Path $dest 'ffmpeg.exe')) {
    Write-Host "[OK] FFmpeg already at $dest\ffmpeg.exe" -ForegroundColor Green
    exit 0
}

Write-Host "Downloading FFmpeg essentials..." -ForegroundColor Cyan
Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing

$extract = Join-Path $env:TEMP 'ffmpeg-extract'
if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
Expand-Archive -Path $zip -DestinationPath $extract -Force

$ffmpeg = Get-ChildItem $extract -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
if (-not $ffmpeg) { throw 'ffmpeg.exe not found in archive' }

New-Item -ItemType Directory -Path $dest -Force | Out-Null
Copy-Item $ffmpeg.FullName (Join-Path $dest 'ffmpeg.exe') -Force
$ffprobe = Get-ChildItem $ffmpeg.DirectoryName -Filter 'ffprobe.exe' | Select-Object -First 1
if ($ffprobe) { Copy-Item $ffprobe.FullName (Join-Path $dest 'ffprobe.exe') -Force }

Write-Host "[OK] Installed to $dest" -ForegroundColor Green
