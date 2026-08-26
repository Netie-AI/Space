# Two short lavfi clips for the Windows Pillar 1 Export click path.
# Expected wipe export duration: 1.6 + 2.0 - 0.5 = about 3.1s.
param([string]$Out = ".\pillar1-clips")
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $Out | Out-Null
if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
    throw "ffmpeg not on PATH. Run install.bat first or install ffmpeg."
}

& ffmpeg -y -hide_banner -loglevel error `
  -f lavfi -i "color=c=blue:s=320x240:d=1.6" `
  -f lavfi -i "sine=f=440:d=1.6" `
  -shortest -c:v libx264 -pix_fmt yuv420p -c:a aac `
  (Join-Path $Out "clip_a.mp4")
if ($LASTEXITCODE -ne 0) { throw "ffmpeg clip_a failed" }

& ffmpeg -y -hide_banner -loglevel error `
  -f lavfi -i "color=c=red:s=320x240:d=2.0" `
  -f lavfi -i "sine=f=660:d=2.0" `
  -shortest -c:v libx264 -pix_fmt yuv420p -c:a aac `
  (Join-Path $Out "clip_b.mp4")
if ($LASTEXITCODE -ne 0) { throw "ffmpeg clip_b failed" }

Write-Host "clips: $Out\clip_a.mp4 $Out\clip_b.mp4"
Write-Host "expected wipe export: about 3.1s (1.6 + 2.0 - 0.5)"
Write-Host "next: docs/HUMAN_PILLAR1_TEST.md"
