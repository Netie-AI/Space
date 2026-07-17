# Local dual-brain (proprietary)

Consent-gated local memory for Netie Space. **Never uploaded.**

## Location

`%LOCALAPPDATA%\NetieSpace\.netie-brain\` (Hidden + System)

| Path | Role |
|------|------|
| `embeddings\*.emb.json` | Compact 256-d sparse vectors + top tokens |
| `redis\*.rdb.json` | Local Redis-like GET/SET digests (TTL) |
| `file-graph.jsonl` | File nodes + folder sibling edges → dual-brain graph |
| `MANIFEST.proprietary` | User-facing ownership notice |

## Consent

- First summary with Summary bar on → consent dialog
- Tray → **Local memory** → Allow / Wipe & revoke
- Prefs: `EmbeddingConsentAccepted`, `EmbeddingConsentPrompted` in `prefs.env`

## Encoder

1. Optional Rust sidecar: `tools\netie-embed\netie-embed.exe` (`scripts\build-netie-embed.ps1`)
2. Fallback: `ManagedSparseEmbed` (C#)

## AirGPT

`airgpt-plugin\manifest.json` + `scripts\register-airgpt-plugin.ps1` — connector for `--preview` handoff. Embeddings stay on the Netie host PC only.
