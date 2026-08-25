# Claude Code / Fable — Netie Space exec plan

**Date:** 2026-07-22  
**Repo:** `D:\Netie Space`  
**Context:** Cursor (Grok) shipped v1 of doc-chat persist, Setup funnel, JPEG compress. You own hard polish + crop/annotate + Ult tiers.

---

## Fixes applied — debug pass 2026-07-22 (Fable)

### 🐞 Doc-chat history was orphaned by any file edit (shipped bug, fixed)

**Symptom the user would hit:** "reopen the same file → chat/notes still there"
silently fails the moment the file is edited — including edits **the app itself
makes**: Fill & Sign autosave rewrites the PDF, compress-overwrite, Organize/Merge,
or any cloud-sync touch.

**Root cause:** `DocumentChatStore.KeyForPath` reused
`SummaryCache.FileMetaFingerprint` = `SHA256(path | length | lastWriteTimeUtc.Ticks)`.
That fingerprint is *correct for the summary cache* (you want it to invalidate when
content changes) but *wrong for chat*, which is attached to the document's **identity**.
Every byte change → new key → `Load()` looks under a different filename → empty thread.

**Fix (`Services/DocumentChatStore.cs`):** key by **normalized absolute path only**
(`GetFullPath` → backslashes → trim → lowercase for the case-insensitive Windows FS
→ `SHA256("path:" + p)`). Added `TryMigrateLegacyThread` — best-effort: if the file is
still byte-identical to its last save, the old metadata key still resolves and the
thread is copied onto the stable key once, so existing users don't lose history.

**Proof (no .NET SDK on this box):** `scripts/pdf-stress/prove_chat_key.py` replicates
both formulas and edits a real temp file three times (open → sign → fill):
old key changes every edit (orphaned), new key is identical across all three. `RESULT: PASS`.

**Not touched:** `SummaryCache.FileMetaFingerprint` stays as-is — content caching
*should* invalidate on change. Only the chat store changed.

**Note for Packet A (archives/restore):** `Clear` still leaves the `_archive/` dir
(archives aren't surfaced yet); when you add Restore, clear archives too or expose them.
`ApiSetupWindow` audited — respects the BYO-key / no-auto-account boundary (opens
netie.space + AI Studio for the user; no account creation). No change needed.

---

## Already shipped (do not redo — extend)

| Feature | Where |
|--------|--------|
| Side chat column open/close | `PreviewWindow` `OpenChatPanel` / `CloseChatPanel` |
| Per-file chat JSON persist | `Services/DocumentChatStore.cs` → `%LOCALAPPDATA%\NetieSpace\doc-chats\` |
| New / Clear / Notes / Pin note | `Controls/ChatPanel.xaml(.cs)` |
| Bind on `LoadFileAsync` (fixes singleton bleed) | `PreviewWindow.xaml.cs` |
| JPEG compress quality picker | `Services/ImageEditService.cs` + Compress toolbar btn |
| Setup: Netie plan first → BYO/OpenFree keys → Vault | `Views/ApiSetupWindow.xaml(.cs)` + Setup button |

---

## Packet A — Chat polish (medium)

```
Netie Space WPF. Extend DocumentChatStore + ChatPanel.

Goals:
1) List archived threads (New chat archives) and Restore.
2) Optional sidecar next to file: .<name>.netie-chat.json when user toggles "Attach beside file" (default stays LocalAppData).
3) Export thread → Markdown.
4) Drag-drop images into chat panel as notes (no AI required).
5) When AI fails, auto-suggest Pin note; never lose the user text.
6) Unit/smoke: open file A, chat, open file B (history swaps), reopen A (history restored).

Do not break Notes-without-AI path.
```

---

## Packet B — Image crop + annotate (hard)

```
Netie Space image preview (PreviewWindow ImageScroll / PreviewImage).

Implement Edit mode for images (mirror PDF Edit button):
1) Compress (already exists — keep).
2) Crop: drag rectangle overlay, map through ScaleTransform + DPI, save copy or overwrite with confirm.
3) Annotate: InkCanvas or vector layer (pen, highlighter, arrow, text). Undo stack. Flatten to PNG/JPEG on Save.
4) Rotate 90 / flip.
5) Smooth UX: secondary ribbon like PDF modes (Crop | Annotate | Compress | Remove BG).

Research: Windows Photos / Snipping Tool patterns. No Electron rewrite.
Deliver FILLSIGN-style IMAGE_EDIT_TEST.md + screenshots if possible.
```

---

## Packet C — Ult / OpenFree / BYO tiers (hard, client + license-api)

```
Product framing (user intent):
1) Convince Netie subscription FIRST (device license).
2) OpenFree = free BYO keys (Gemini/Groq…) while Ult rolls out.
3) Ult = Netie-hosted AI (server-provided key / proxy) for subscribers.
4) BYO always available as override.
5) Password vault helps users store the accounts they create.

Work:
- Add PlanTier enum to LicenseEntitlement (FreeDevice | OpenFree | Ult | …).
- license-api Worker: emit tier on /v1/activate; wire Stripe Checkout using reserved D1 columns (docs/LICENSE_SETUP.md).
- ApiSetupWindow: if Ult entitled, hide need for BYO or show "Ult active"; else show OpenFree steps.
- AiService: Ult path uses Netie proxy endpoint when entitled; else BYO.
- Keep SkipGate for dev.

Do not ship fake Ult that calls unpaid APIs.
```

---

## Packet D — PDF leftover (from prior audit)

```
See docs/research/findings/OPENEDIT_ACROBAT_AUDIT.md Packets 2–3:
- OpenEdit full Acrobat shell
- Edit text / OCR
- Persist highlights
- Comb fields

Stress with scripts/pdf-stress.
```

---

## Verify after your pass

```powershell
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
cd "D:\Netie Space"
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 -NoSingleFile
cmd /c run.bat
```

Manual:
1. Space → any file → Chat → Pin note → close → reopen → note still there.
2. New chat → old archived under doc-chats\*_archive.
3. Image → Compress → open smaller file.
4. Setup → Netie plan CTA → Gemini steps → Vault opens.

---

## Priority order for Claude

1. Packet A (chat polish)  
2. Packet B (crop/annotate)  
3. Packet D (PDF shell) if OpenEdit still priority  
4. Packet C (Ult billing) last — needs product/Stripe decisions
