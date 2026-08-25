# Quick Look principles → Netie Space

Short Mac Quick Look ideas and how they map to Netie.

## Principles

1. **Instant** — show something useful in under 100ms (icon/thumbnail), then refine.
2. **Ephemeral** — Space opens, Space/Esc closes; no modal “document app” session.
3. **Travel** — arrows move the selection and the preview together.
4. **Faithful** — prefer real pages/slides over text dumps when possible.
5. **Stay in place** — preview floats over Explorer/Desktop; don’t steal the folder.

## Netie roadmap (from this)

| Principle | Status / next |
|-----------|----------------|
| Instant first paint | PPTX Shell thumbnail while COM/LO loads |
| Space toggle / Esc close | Done (global hook; Esc WebView-safe) |
| Arrow sibling travel | Done (Up/Down/Left/Right) |
| Home / End extremes | Done (first/last sibling) |
| Faithful Office | PPTX: COM → LibreOffice → HTML; DOCX: mammoth + media/ |
| IPreviewHandler host | Opaque overlay over ContentHost (not inside glass HWND) |
| Mission Control–style overview | Parked |

Keep Space feel light: no heavy chrome in the first viewport, no convert-to-markdown for visual preview.
