# Netie Space — Product Understanding & Landing Brief

*Written 2026-07-17 for the netie.ai scroll-world landing page. Sources: README.md,
PARKING_LOT.md, docs/* (eSuite vision, AirGPT synergy, Quick Look principles, token
savings, vault, license API), and netie.ai live site + CSS.*

---

## 1. What Netie Space is

**One key. Every file.** Netie Space brings the Mac "Quick Look" moment to Windows —
press **Space** on any file in File Explorer or the Desktop and it opens instantly in a
floating, ephemeral preview. Press Space or Esc and it's gone. Arrow keys travel between
siblings without closing. No app launch, no wait, no context switch.

But unlike Quick Look, the preview is a **workbench**:

| Pillar | What ships today |
|---|---|
| **Instant preview** | PDF (pdf.js, Acrobat-class), Word (mammoth HTML + KaTeX math), PowerPoint (real slides via COM/LibreOffice), Excel, images, code, video, folders/exe metadata. Faithful pages — never a text dump. |
| **AI on tap** | Optional AI summary, full file-context chat ("Continue Chat"), highlight → **Look Up with AI**. Multi-provider (Gemini → Groq → SEA-LION → OpenRouter) with auto-fallback on quota. Works fully offline without a key — AI is opt-in. |
| **Acrobat-class PDF** | Merge, split, extract, organize (page thumbs), rotate, reverse, sign-stamp with drag placement — inside the preview. |
| **CapCut-light video** | Multi-track timeline (V1/A1/A2/overlay): trim, split, drag-reorder, delete, fade, speed, text overlays, Whisper auto-captions with SRT burn-in, LUT, background export to Match/1080p/4K with progress toast. |
| **One-click convert** | ↓ MD and ↓ PDF for any document (best-in-class converters chosen by benchmark: pdfmd, pymupdf4llm, pptx2md, markitdown), Groq Vision OCR for images. |
| **Image tools** | rembg background removal, zoom. |
| **Private by design** | Local-first: previews, edits, PDF and video tools all work offline. Usage footprint stores extension-only telemetry, never paths/filenames/content. DPAPI-encrypted local vault. License entitlement sealed on-device. |

**Token discipline (ponytail-inspired):** *"the best tokens are the ones you never
sent"* — local extractive summaries first, hash-based summary cache, small-model routing,
aggressive truncation, conversation fingerprints. AI that's cheap enough to leave on.

**Product family context (eSuite):** Netie Space is the control-plane / daily-driver of a
local-first toolkit; **AirGPT** (deep AI workspace/vault) hands files to Space via
`NetieSpace.exe --preview "path"`. Distribution: self-contained win-x64, Inno installer,
Cloudflare Workers license API (register → activate → device-bind), Stripe-ready.

**Positioning in one line:** *macOS Quick Look, rebuilt for Windows, with an AI brain,
an edit bay, and a print shop inside — private, instant, one key.*

## 2. Audience & why they care

Windows knowledge workers, students, creators drowning in files. The pain: every glance
at a file costs an app launch (Acrobat 8s, PowerPoint 12s, Premiere for a 10s trim).
Netie Space collapses glance→understand→edit→share into one keystroke. The AI angle:
your files can answer questions — without uploading your life to a SaaS.

## 3. Brand kit (extracted from netie.ai)

| Token | Value | Use |
|---|---|---|
| `ink-black` | `#0a0a0a` (range #050505–#1a1a1a) | page background |
| `netie-mint` | `#6efbcb` (alt `#6aefcb`) | signature accent, CTA, glow |
| `violet` | `#a78bfa` | secondary accent |
| `sky` | `#38bdf8` | tertiary accent |
| `paper` | `#ffffff` @ alpha steps | text/ink on dark |

Type: **Syne** (display), **Inter / DM Sans** (body), **JetBrains Mono** (labels/eyebrows).
Tone: `PRIVATE. SECURE. EFFECTIVE.` — confident, technical, a little cinematic
(netie.ai names agents "The Hunter / The Diplomat / The Brain"). Netie Space slots in as
the desktop product: quieter, more human, still precise.

## 4. netie.ai context (where this lands)

Current site: dark, enterprise AI-agent suite — Vanguard (signal intelligence), Closer
(autonomous negotiation), Cortex (orchestration) — plus coming-soon consumer hardware
(Netie Talkie plushie, Netie AIM). The Netie Space landing must read as the same brand
(dark + mint + mono eyebrows) while feeling like a *product you can touch* — hence the
scroll-world: a miniature neon world you fly through, one keystroke wide.

## 5. The scroll-world journey (proposed — 6 scenes)

Art direction: **neon-night isometric miniature** (dark ground, mint/violet/sky glow) —
architecture B (dive into each island, aerial hop to the next), matching the dark site.

| # | id | Diorama subject | Eyebrow | Title | Body | Tags |
|---|---|---|---|---|---|---|
| 1 | `desk` | Late-night workstation island: desk, monitor showing a wall of file icons, an oversized mint-glowing **Space bar** on the keyboard | ONE KEY | Press Space. See everything. | Instant Quick Look-style preview for every file on Windows — no app launch, no wait. | Instant · Windows · Offline-first |
| 2 | `preview` | Neon gallery hall: PDF pages, slides, spreadsheets, photos, code panels floating as lit exhibits | PREVIEW ANYTHING | Every format, faithfully. | PDF, Word, PowerPoint, Excel, images, code, video — real pages, not text dumps. | PDF · Office · Video |
| 3 | `ai` | Glowing core-room: a mint brain-core over a console, chat bubbles and summary cards orbiting | ASK YOUR FILES | AI that reads with you. | Summaries, chat and instant Look Up — private, token-thrifty, and optional. | Summary · Chat · Look Up |
| 4 | `video` | Miniature edit bay: timeline desk with glowing clip blocks, preview wall, faders | THE EDIT BAY | Cut video in the preview. | Trim, split, caption and export to 4K — without opening an editor. | Trim · Captions · 4K export |
| 5 | `pdf` | Print-workshop island: robotic arms merging/splitting page stacks, a big stamp arm signing | THE PDF WORKSHOP | Acrobat-class, one key away. | Merge, split, organize, sign — right where you preview. | Merge · Split · Sign |
| 6 | `finale` | One giant floating mint Space keycap, tiny docs/filmstrip/pages orbiting like moons | NETIE SPACE | One key. Every file. | Free to preview. Ready in seconds. | — CTA: **Download for Windows** |

## 6. Assets & budget shape

6 stills (gpt_image_2, 3:2, 2k) + 6 dive clips + 5 connectors (chain model, 16:9).
Desktop-only ≈ 6 image + 11 video generations (+~15% re-roll headroom). Mobile opt-in
doubles the video count with a native 9:16 chain. Costs calibrated with one probe still +
one probe video before the full run.
