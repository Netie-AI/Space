#!/usr/bin/env bash
# Encode clip_a + clip_b with Space's wipeleft graph (offset = 1.6 - 0.5 = 1.1).
# Does not replace the Windows human Export click path.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
# shellcheck source=_ffmpeg.sh
. "$root/scripts/_ffmpeg.sh"
dir="${1:-$root/pillar1-clips}"
"$root/scripts/make-pillar1-clips.sh" "$dir"
out="$dir/wipe_ref.mp4"
ffmpeg="$(netie_ffmpeg || true)"
ffprobe="$(netie_ffprobe || true)"
if [[ -z "$ffmpeg" || -z "$ffprobe" ]]; then
  echo "ffmpeg/ffprobe not found. Run install.bat or scripts/download-ffmpeg.ps1." >&2
  exit 1
fi
# Same string FfmpegExportGraph.XfadeWithAudio(1.6s, 500ms, 320, 240)
fc='[0:v]scale=320:240:force_original_aspect_ratio=decrease,pad=320:240:(ow-iw)/2:(oh-ih)/2,setsar=1,setpts=PTS-STARTPTS,fps=30,format=yuv420p[v0];[1:v]scale=320:240:force_original_aspect_ratio=decrease,pad=320:240:(ow-iw)/2:(oh-ih)/2,setsar=1,setpts=PTS-STARTPTS,fps=30,format=yuv420p[v1];[v0][v1]xfade=transition=wipeleft:duration=0.5:offset=1.1[vout];[0:a]aformat=sample_fmts=fltp:sample_rates=44100:channel_layouts=stereo[a0];[1:a]aformat=sample_fmts=fltp:sample_rates=44100:channel_layouts=stereo[a1];[a0][a1]acrossfade=d=0.5[aout]'
"$ffmpeg" -y -hide_banner -loglevel error \
  -i "$dir/clip_a.mp4" -i "$dir/clip_b.mp4" \
  -filter_complex "$fc" -map "[vout]" -map "[aout]" \
  -c:v libx264 -preset veryfast -crf 23 -c:a aac -movflags +faststart \
  "$out"
dur="$("$ffprobe" -v error -show_entries format=duration -of csv=p=0 "$out")"
echo "wipe_ref: $out duration=$dur (expect about 3.1)"
# Same 2.8..3.4 as check-pillar1-export. Echo-only used to exit 0 on a
# hard-cut concat (~3.6s) when xfade was skipped.
"$root/scripts/check-pillar1-export.sh" "$out"
