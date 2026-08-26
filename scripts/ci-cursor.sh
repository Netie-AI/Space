#!/usr/bin/env bash
# Cursor-origin CI gate. Linux: python + license-api + ffmpeg + net8 xunit
# + WinExe compile (`EnableWindowsTargeting`; no run). Same xunit as Windows CI.
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

echo "== xunit (net8, no WPF ProjectReference)"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK missing. Run scripts/ci-cursor-install.sh"
  exit 1
fi
export CI=1
dotnet test tests/NetieSpace.Tests/NetieSpace.Tests.csproj -c Debug --nologo --blame-hang-timeout 2m

echo "== winexe (EnableWindowsTargeting; no run)"
dotnet build src/NetieSpace/NetieSpace.csproj -c Debug --nologo -p:EnableWindowsTargeting=true

echo "== pillar1 wipe reference encode"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
./scripts/encode-pillar1-wipe.sh "$tmp"

echo "PASS scripts/ci-cursor.sh"
