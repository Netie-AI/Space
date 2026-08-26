#!/usr/bin/env bash
# Two short lavfi clips for the Windows Pillar 1 Export click path.
# Expected wipe export duration: 1.6 + 2.0 - 0.5 = about 3.1s.
set -euo pipefail
# shellcheck source=_ffmpeg.sh
. "$(cd "$(dirname "$0")" && pwd)/_ffmpeg.sh"
out="${1:-./pillar1-clips}"
mkdir -p "$out"
ffmpeg="$(netie_ffmpeg || true)"
if [[ -z "$ffmpeg" ]]; then
  echo "ffmpeg not found. Run install.bat (downloads tools/ffmpeg) or put ffmpeg on PATH." >&2
  exit 1
fi

"$ffmpeg" -y -hide_banner -loglevel error \
  -f lavfi -i color=c=blue:s=320x240:d=1.6 \
  -f lavfi -i sine=f=440:d=1.6 \
  -shortest -c:v libx264 -pix_fmt yuv420p -c:a aac \
  "$out/clip_a.mp4"

"$ffmpeg" -y -hide_banner -loglevel error \
  -f lavfi -i color=c=red:s=320x240:d=2.0 \
  -f lavfi -i sine=f=660:d=2.0 \
  -shortest -c:v libx264 -pix_fmt yuv420p -c:a aac \
  "$out/clip_b.mp4"

echo "clips: $out/clip_a.mp4 $out/clip_b.mp4"
echo "expected wipe export: about 3.1s (1.6 + 2.0 - 0.5)"
echo "next: docs/HUMAN_PILLAR1_TEST.md"
