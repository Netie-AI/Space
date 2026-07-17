# Editing roadmap (CapCut-light + local AI)

**Last updated:** 2026-07-14 · Loop gates: Grok 4.5 + Composer · See `docs/LOOP_STATE.md`

## Shipped

| Area | Item |
|------|------|
| Product | App icon, install + HKCU Run startup |
| Timeline | Packed V1 clips, drag reorder, black gap fill |
| Edit | Split / select / delete / fade chips |
| Audio | A1 mirrors V1 (`PackLinkedVideoAudio`) |
| Export | Match / 1080p / 4K buttons, LUT, fade on export |
| Captions | whisper.cpp path + **SRT burn-in** (`SubtitleBurnPath`) |
| Stability | No DropShadow on timeline/chrome; coalesced timeline layout; no working-set clamp |
| Preview | Icon from disk (single-file safe); pdf.js JSON bridge |

## Park

- Higgsfield / ComfyUI generative video
- LibVLC preview (crashed open path — MediaElement stays)
- Cert PDF signatures; full Acrobat forms

## Next (loop order)

1. **Pillar 1 gate:** user stress Select→Split→Drag→Delete→Fade→Export
2. Multi-source V1 polish
3. **Pillar 2:** PDF OCR + in-place text; annotate/highlight
4. **Pillar 3:** Cite / First Principles / Mindmap; ditch_context in Netie
5. **Pillar 4 (optional):** marketing screenshots / landing
