# Netie Space — Parking Lot

Undone / deferred work. Check items off when shipped. **Last updated: 2026-09-07** (Space-25 SIGNED; ledger 33 multi-editor intake).

Loop gates: **Grok 4.5 + Composer**. State: [`docs/LOOP_STATE.md`](docs/LOOP_STATE.md). Cortex handoff: [`docs/CORTEX_RUNTIME_BEST_FINAL.md`](docs/CORTEX_RUNTIME_BEST_FINAL.md). Editing: [`docs/EDITING_ROADMAP.md`](docs/EDITING_ROADMAP.md).

---

## Done this week (1A core)

- [x] Stop DOCX → Markdown auto-convert on Space (mammoth HTML + OpenXML HTML fallback)
- [x] Image / PDF zoom controls
- [x] Folder / exe metadata “Quick Look intro”
- [x] Sticky multi-monitor selection harden
- [x] Liquid Glass chrome polish
- [x] Mac-style sibling travel while preview open
- [x] Desktop selection strengthened
- [x] Self-contained win-x64 publish (`scripts\publish.ps1` / `install.bat` / `run.bat`)

## Stability (2026-07-14)

- [x] PreviewWindow XAML crash: single-file `Icon=pack://` → ContentFilePart null — load icon from disk
- [x] Export ComboBox removed from always-loaded tree (Match/1080/4K buttons)
- [x] pdf.js WebView bridge: `JSON.stringify` + `WebMessageAsJson` fallback
- [x] No DropShadowEffect on timeline clips / window chrome (stack overflow `0xc00000fd`)
- [x] Timeline layout always coalesced via `ScheduleTimelineLayout`
- [x] Skip invalid Min/Max working-set clamp; Interactive GC
- [x] UI MessageBox debounce; no popup on TypeConverter / layout errors
- [x] WebView2 opaque background (was alpha=16 bug) — PDF slight-interaction compositor crash
- [x] Fade-only open/close (no ScaleTransform over WebView2 HWND)
- [x] pdf.js render generation guard + thumb cap 24 + canvas pixel cap
- [x] PDF zoom via `setZoom` only (no ZoomFactor × pdf.js scale stack)
- [x] `scripts/stress-pdf.ps1` + stress-edit chains PDF matrix
- [x] PDF sign: correct page-point coords; Place signature hidden until Sign flow
- [x] Video packed-timeline scrub (slider ↔ playhead sync in edit mode)
- [x] Delete next to Split; glass Merge vs Black choice dialog
- [x] Export progress bar + background export survives Space close + ToastHud open folder
- [x] Word/DOCX KaTeX: OMML extract + DOMContentLoaded render pass

---

## P1 — AI API optimize

- [x] Per-provider request shapes: Gemini / Groq / SEA-LION / OpenRouter
- [x] SEA-LION default + overrides
- [x] Groq 413 TPM truncate + small default
- [x] Friendlier chat/summary errors
- [x] SEA-LION in API Keys UI + `user.env`
- [x] Token savings — see `docs/TOKEN_SAVINGS.md`
- [x] Port AirGPT `ditch_context` tiers into Netie conversation cache (`DitchContext.cs`)

## CI

- [ ] Path-split CI (stack commit `07d9fbc`: python / windows / license-api as sibling workflows with `paths:` filters) was not adopted. Unlock: decide how required checks behave when a path filter skips them (branch protection waits forever on a check that never reports), then add the Windows exe-level steps to the split workflow before moving the job out of `ci.yml`.

## P2 — API product

- [ ] Buy API flow (Setup + login gate call `/v1/checkout`; register is `pending` until pay; live after Stripe secrets)
- [x] Safer default models per provider
- [ ] Stripe Checkout (test Product `netie_space` / Price `price_1U8ckCFV5wcFod2faXiUCsaV` in Worker vars; secret key + webhook + live pay still human — `docs/HUMAN_STRIPE_CHECKOUT.md`)

## P5 — Video (multi-track NLE — ACTIVE · Loop Pillar 1)

- [x] Phase 0: Trim rename, In/Out tint, FFmpeg CTA
- [x] Phase 1: Export SaveFileDialog + keyframe-safe trim
- [x] Smooth scrub (mute+pause+throttle)
- [x] DPI-safe window on owner monitor
- [x] CapCut-light packed timeline: split / select / delete / drag reorder
- [x] Multi-track model V1 / A1 / A2 / OV (`TimelineProject`) — MediaElement preview (LibVLC parked)
- [x] Export presets Match / 1080p / 4K + LUT + fade
- [x] Fade chips; speed / black / white / text overlays (basic)
- [x] Whisper captions path + **SRT burn-in on export**
- [x] A1 ripple pack with V1 drag/delete
- [x] Export progress + background toast (Space-safe)
- [x] Delete UX: timeline toolbar + merge/black dialog
- [ ] User-validated Select->Split->Drag->Delete->Fade->Export (Windows human; playbook `docs/HUMAN_PILLAR1_TEST.md`. Playwright cannot open WPF.)
- [x] Multi-source V1 polish (+ Clip)
- [x] Transitions beyond fade; overlay text on canvas
- [x] Phase 3: richer ordered multi-clip merge UX

## eSuite / AirGPT / Ponytail

- [x] `docs/ESUITE_VISION.md`
- [x] `docs/AIRGPT_SYNERGY.md` + KaTeX
- [x] `docs/TOKEN_SAVINGS.md`
- [x] rembg Remove BG
- [x] PDF toolkit menu
- [x] Acrobat-class pdf.js viewer in WebView2 (ACTIVE — see P3)
- [x] AirGPT `--preview "path"` CLI handoff
- [x] Vault ↔ Space bridge (`OpenVaultKeySync`)
- [x] Cortex sidecar ditch tiers + WASM stubs (parallel track)

## P3 — Acrobat-class PDF (ACTIVE · Loop Pillar 2 next)

- [x] Instant Space PDF = WebView2 + zoom; PdfPig; PdfSharp
- [x] Failed load → friendly status + Open externally
- [x] PDF toolkit: merge, split, extract, organize, reverse, sign-stamp
- [x] pdf.js viewer + C# message bridge (stringify fixed 2026-07-14)
- [x] Page thumb strip + stamp drag placement
- [x] Merge arrange UI; Organize page thumbs
- [x] Annotate / highlight (pdf.js overlay; PdfSharpCore appearance, no iText AGPL)
- [x] Form fill / OCR / in-place text — Tier B
- [ ] Cryptographic digital signature — PKCS#7 incremental (`PdfCmsSigner`, no iText). Adobe Reader Signature panel is still human (`docs/HUMAN_PARKED.md`)
- [x] Toolbar discipline polish (primary chips + More...)

## P8 — Office fidelity

- [x] PPTX COM / HTML scroll-snap / mammoth DOCX / Shell thumb / LibreOffice / XLSX
- [x] Never MarkItDown for Space visual
- [x] Word/HTML contenteditable editor
- [x] Windows IPreviewHandler host (optional)

## P4 — Image tools

- [x] rembg Remove BG
- [x] Copy / drag-out cleaned image
- [ ] ComfyUI / Windows AI -- code-ready local client; live GPU still human (`docs/HUMAN_PARKED.md`)

## P6 — Product / Mac Space (Loop Pillar 3)

- [x] `docs/QUICK_LOOK_PRINCIPLES.md`
- [x] Esc / Space / Home / End parity
- [x] AirGPT `--preview` CLI
- [x] Slide screenshot → chat attach (visible slide PNG via Send slide / Add to Chat)
- [x] Cite / First Principles / Mindmap chat modes
- [x] Marketing assets (Pillar 4 optional)

## P7 — Deferred architecture (do not start)

- [ ] PDF draw-app / stroke smoothing / add-image -- unlock: founder unparks after FILL_ONLY (ledger 41/48). Mark/highlight already exist. Do not steal UserChoice.
- [ ] Rust/Go **rewrite of NetieSpace.exe** -- unlock: never, unless PRD press release is rewritten. Targeted sidecars are allowed: `native/netie-pdf-detect` (Rust snap-cell) and `native/netie-pdf-form` (Go AcroForm list/fill).
- [ ] Unbounded "AI remembers me" / harvest every form field / Cortex `/api/memory` — unlock: founder signs PRD amendment + Space#3 SEC-01 (leave-machine). Local FillProfileStore (name/email/phone/address/DOB/nationality/date format) is the live cap.
- [ ] Named objects / save-from-utterance / AI fill of the open PDF -- unlock: `Netie-AI/Space#3` (ledger 25/31/34/38 SIGNED propose-then-confirm + enquire-then-remember). Consume Cortex `/api/memory`; ASK then upsert after human answer; empty memory abstain, never silent fill. HITL type-in of founder-supplied facts on the live Fill rail is allowed now (no tickets, no ChatPanel writes). Do not grow a second KB in Space. OS/web clicks are Pointer, not ChatPanel.
- [ ] Space as Cortex `/api/memory` consumer (AI-detect objects; no typed extra cards) -- unlock: Space#3 gate-or-disclose + Cortex object-schema PRD sign (ledger 22-23). FillProfileStore stays the row-16 cap. Do not grow fill-profile.bin. Do not silent-upsert facts.
- [ ] Google Forms / shared fill across surfaces -- unlock: not Space. Pointer/Crew/browser after Cortex `/api/memory` retrieve. Space PDF fill from shared memory is a later consumer after Space#3.
- [ ] AcroForm widget authoring (insert `/FT` fields) -- unlock: founder names overlay fill boxes vs PDF form widgets. Overlay click-add is live; drag/resize is signed row 8 execute-now. Go sidecar stays list/fill of existing widgets.
- [ ] Full iLovePDF / iLoveIMG cloud parity -- unlock: founder names which remaining *local* PDF tools unpark (ledger 36; compress / page-numbers / crop vs repair/compare/redact). Cloud upload stays refused. Distill research is parent findings, not a product epic.
- [ ] Full image / PPTX / Word / Excel clone editors (OnlyOffice-class) -- unlock: separate product PRD, not Space (ledger 35 SIGNED stop packing). P8 preview + light HTML/text edit may remain until press-release rewrite. Light CSV grid + MD dual-pane shipped under Assets/markdown. P5 video NLE already shipped. OnlyOffice AGPL stays BAN. LibreOffice stays installed soffice fallback, not a vendored tree.
- [ ] themarkdowneditor.app clone / GitHub-sync / Codex-in-editor -- unlock: never for clone. DISTILL UX lives in Markdown PRD-001 (`D:\Netie\Software Blueprint\Markdown\PRD-001-local-markdown-editor.md`; Space ledger 46). Engine default keep Markdig+marked. GitHub-sync + AI-in-editor stay PARK / FreeIDE. Auto-open `.md` waits Acrobat row 45 (`PreviewWindow`). Insights/ontology = Cortex `/api/memory` after Space#3, not Space.
- [ ] Personal knowledge graph / user digital clone inside Space -- unlock: founder rewrites the Space press release. Default refuse. WP-004 is company clone (Crew+Cortex+Control), not law. Palantir P1 stays parked. TAS lanes 6/7/12/17/18.
- [ ] Code view/edit/Run (npm/python stack, no commands) — unlock: FreeIDE product, not Space. TAS lane 19 SKIP.
- [ ] Google AI Overview scrape
- [ ] Higgsfield / Comfy generative video

## Housekeeping

- [x] Optional tooling under `optional/`
- [x] Root lean: `src/`, `tools/`, `scripts/`, `dist/`, `.venv/`
- [x] Rotate loop armed (`AGENT_LOOP_TICK_netie_rotate` every 30m)
