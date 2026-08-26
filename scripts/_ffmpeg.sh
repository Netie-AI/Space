# Shared locate for playbook scripts. install.bat puts ffmpeg in tools/ffmpeg
# (gitignored) and does not add it to PATH.
_netie_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

_netie_tool() {
  local name="$1"
  local dir="$_netie_root/tools/ffmpeg"
  if [[ -x "$dir/${name}.exe" ]]; then
    echo "$dir/${name}.exe"
    return 0
  fi
  if [[ -x "$dir/$name" ]]; then
    echo "$dir/$name"
    return 0
  fi
  command -v "$name" 2>/dev/null
}

netie_ffmpeg() { _netie_tool ffmpeg; }
netie_ffprobe() { _netie_tool ffprobe; }
