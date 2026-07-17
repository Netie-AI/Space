# Netie Space — learning footprint & vault (local-first)

## Usage footprint
- File: `%LOCALAPPDATA%\NetieSpace\data\usage.jsonl`
- Stores: UTC time, action kind, **file extension only**, optional size
- Never stores: full paths, filenames, passwords, chat text, API keys
- Tray → **Usage footprint…** creates DPAPI-encrypted `learning-export.bin` (CurrentUser) for optional Netie learning sync

## Password Vault
- File: `%LOCALAPPDATA%\NetieSpace\vault\vault.bin` (DPAPI CurrentUser)
- Tray → **Password Vault…**
- Cross-device sync (iCloud-like) planned via Space bridge — local vault ships first

## Install tooling
- `install.bat` stops NetieSpace safely, publishes, then `scripts\ensure-ai-tools.ps1`
- Whisper captions + rembg Remove BG are pulled when missing
