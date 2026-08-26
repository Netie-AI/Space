#!/usr/bin/env bash
# Cursor-origin bootstrap. Idempotent. No servers. No tests.
set -euo pipefail
cd "$(dirname "$0")/.."

if ! command -v ffmpeg >/dev/null || ! command -v ffprobe >/dev/null; then
  sudo apt-get update -qq
  sudo apt-get install -y -qq ffmpeg
fi

if [[ -f license-api/package-lock.json ]]; then
  (cd license-api && npm ci --legacy-peer-deps)
fi
