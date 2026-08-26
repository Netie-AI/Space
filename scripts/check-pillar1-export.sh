#!/usr/bin/env bash
# ffprobe a Windows Export. Expect ~3.1s (1.6 + 2.0 - 0.5 wipe).
# Does not replace the human Select->Split->Drag->Delete->Fade->Export clicks.
set -euo pipefail
# shellcheck source=_ffmpeg.sh
. "$(cd "$(dirname "$0")" && pwd)/_ffmpeg.sh"
file="${1:-}"
if [[ -z "$file" || ! -f "$file" ]]; then
  echo "usage: $0 <exported.mp4>"
  exit 2
fi
ffprobe="$(netie_ffprobe || true)"
if [[ -z "$ffprobe" ]]; then
  echo "ffprobe not found. Run install.bat (downloads tools/ffmpeg) or put ffprobe on PATH." >&2
  exit 2
fi
dur="$("$ffprobe" -v error -show_entries format=duration -of csv=p=0 "$file")"
python3 - "$dur" <<'PY'
import sys
d = float(sys.argv[1])
lo, hi = 2.8, 3.4
ok = lo <= d <= hi
print(f"duration={d:.3f}s expect {lo}..{hi} ({'OK' if ok else 'FAIL'})")
raise SystemExit(0 if ok else 1)
PY
