#!/usr/bin/env bash
# Cursor-origin CI gate. Linux can run python + license-api + ffmpeg smoke.
# WPF WinExe cannot build here; Windows xunit already landed on main via #25.
set -euo pipefail
cd "$(dirname "$0")/.."

echo "== ffmpeg"
command -v ffmpeg
command -v ffprobe

echo "== python compileall"
python3 -m compileall -q -f tests scripts

echo "== python contracts"
for f in tests/test_*.py; do
  echo "== $f"
  python3 "$f"
done

echo "== license-api typecheck"
(
  cd license-api
  if [[ ! -d node_modules ]]; then
    npm ci --legacy-peer-deps
  fi
  npm run typecheck
)

echo "PASS scripts/ci-cursor.sh"
