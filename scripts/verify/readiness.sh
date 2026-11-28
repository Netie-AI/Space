#!/usr/bin/env bash
# Readiness matrix + scale checks on this machine (Linux shape of the test project).
# Usage: NETIE_READINESS_FLOOR=59 ./scripts/verify/readiness.sh
# Writes tool-matrix.md/json and scale.md to NETIE_READINESS_OUT (default ./readiness-report).
set -euo pipefail
cd "$(dirname "$0")/../.."
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
export NETIE_READINESS=1
export NETIE_READINESS_OUT="${NETIE_READINESS_OUT:-$PWD/readiness-report}"
command -v ffmpeg >/dev/null || { echo "ffmpeg missing"; exit 1; }
command -v dotnet >/dev/null || { echo "dotnet SDK missing. Run scripts/ci-cursor-install.sh"; exit 1; }
echo "ffmpeg: $(ffmpeg -hide_banner -version | head -1)"
dotnet test tests/NetieSpace.Tests/NetieSpace.Tests.csproj -c Debug --nologo \
  --filter "FullyQualifiedName~NetieSpace.Tests.Readiness" \
  --logger "console;verbosity=normal" --blame-hang-timeout 10m
echo "== report: $NETIE_READINESS_OUT"
sed -n 1,3p "$NETIE_READINESS_OUT/tool-matrix.md"
echo "PASS scripts/verify/readiness.sh"
