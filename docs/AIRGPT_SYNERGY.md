# Netie Space × AirGPT synergy

**Netie** (`c:\Users\OoiJianHong\Netie Space`) = Space-key Quick Look + multi-track NLE + Acrobat-class PDF + Office fidelity.  
**AirGPT** (`D:\AirGPT`) = local-first AI clipboard/chat, vault, KaTeX reasoning, phone tunnel.

Already aligned in `docs/ESUITE_VISION.md` and AirGPT `README.md` (Netie = control plane).

---

## 1. Overlapping features

| Area | Netie Space | AirGPT |
|------|-------------|--------|
| **Chat** | `ChatPanel` in preview — file-context Q&A, Look Up, slide screenshots | Full SPA chat, sessions, vault modes |
| **AI providers** | Gemini, Groq, SEA-LION, OpenRouter | DeepSeek → OpenRouter → Groq → Google |
| **Docs / markdown** | Markdig → WebView2; mammoth DOCX; HTML edit mode | Vault `.md`; `marked` + KaTeX |
| **LaTeX** | KaTeX in Office `WrapHtml` | KaTeX in `index.html` |
| **PDF** | pdf.js in WebView2 + PdfEditService | Attachments only |
| **Video** | LibVLC + FFmpeg multi-track timeline | Chat attachments only |
| **Memory** | SummaryCache + ConversationMemoryCache | SQLite sessions + vision_cache |

---

## 2. Netie ← AirGPT

- KaTeX render pipeline (`$$…$$` / `$…$`)
- LaTeX-aware prompts / SymPy pass
- Vault session metadata patterns
- Phone → PC tunnel (complements desktop Space)

## 3. AirGPT ← Netie

- Space preview handoff for PDF/Office/video
- PdfEditService / FfmpegService / Office fidelity
- Token discipline (local summary + truncate + small models)
- SEA-LION provider slot

**CLI handoff (shipped):** AirGPT “Open in Space” → spawn:

```text
NetieSpace.exe --preview "C:\path\to\file.pdf"
```

Also accepts a bare file path as the first argument.

---

## 4. Unify later (do not merge codebases)

- One `PreviewHtmlTemplate` + KaTeX
- Shared `providers` / env key names (`GOOGLE_*` drift)
- Vault `.md` “Open in Space”

---

## 5. Top 5 actions

1. [x] KaTeX in Netie Office `WrapHtml` (CDN; offline bundle later)
2. [x] Shared env / provider spec → `docs/PROVIDERS.env.example` (+ SEA-LION)
3. [x] AirGPT → Netie preview handoff (`--preview "path"`)
4. [ ] Vault ↔ Space bridge
5. [x] Netie token ladder already shipped (`docs/TOKEN_SAVINGS.md`) — port remainder to AirGPT vision follow-ups

*Compared: 2026-07-13. See `PARKING_LOT.md`, `docs/ESUITE_VISION.md`, `docs/TOKEN_SAVINGS.md`.*
