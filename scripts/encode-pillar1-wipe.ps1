# Encode clip_a + clip_b with Space's wipeleft graph (offset = 1.6 - 0.5 = 1.1).
# Does not replace the Windows human Export click path.
param([string]$Out = ".\pillar1-clips")
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot '_ffmpeg.ps1')
& (Join-Path $PSScriptRoot 'make-pillar1-clips.ps1') -Out $Out
$ffmpeg = Get-NetieFfmpeg
$ffprobe = Get-NetieFfprobe
if (-not $ffmpeg -or -not $ffprobe) {
    throw "ffmpeg/ffprobe not found. Run install.bat or scripts/download-ffmpeg.ps1."
}
$a = Join-Path $Out "clip_a.mp4"
$b = Join-Path $Out "clip_b.mp4"
$wipe = Join-Path $Out "wipe_ref.mp4"
# Same string FfmpegExportGraph.XfadeWithAudio(1.6s, 500ms, 320, 240)
$fc = '[0:v]scale=320:240:force_original_aspect_ratio=decrease,pad=320:240:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p,setpts=PTS-STARTPTS[v0];[1:v]scale=320:240:force_original_aspect_ratio=decrease,pad=320:240:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p,setpts=PTS-STARTPTS[v1];[v0][v1]xfade=transition=wipeleft:duration=0.5:offset=1.1[vout];[0:a]aformat=sample_fmts=fltp:sample_rates=44100:channel_layouts=stereo[a0];[1:a]aformat=sample_fmts=fltp:sample_rates=44100:channel_layouts=stereo[a1];[a0][a1]acrossfade=d=0.5[aout]'
& $ffmpeg -y -hide_banner -loglevel error `
  -i $a -i $b `
  -filter_complex $fc -map "[vout]" -map "[aout]" `
  -c:v libx264 -preset veryfast -crf 23 -c:a aac -movflags +faststart `
  $wipe
if ($LASTEXITCODE -ne 0) { throw "ffmpeg wipe_ref failed" }
$dur = & $ffprobe -v error -show_entries format=duration -of csv=p=0 -- $wipe
Write-Host "wipe_ref: $wipe duration=$dur (expect about 3.1)"
& (Join-Path $PSScriptRoot 'check-pillar1-export.ps1') -Path $wipe
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
