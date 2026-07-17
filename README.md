# Netie Space

Press **Space** in File Explorer or on the **Desktop** to preview any file — PDF, Word, PowerPoint, Excel, images, code — with optional AI summary, chat, and one-click conversion.

## Quick Start (this PC)

```powershell
cd "c:\Users\OoiJianHong\Netie Space"
.\install.bat
```

Or run without reinstall: `.\run.bat`

Keys load from `.env` and from **tray → API Keys…** (`%LOCALAPPDATA%\NetieSpace\user.env`).

## Features

| Action | How |
|--------|-----|
| Preview | Select file in Explorer **or Desktop** → **Space** |
| Close | **Space** again or **Esc** |
| Next / prev file | While preview is open → **↓ / ↑** (or **→ / ←**) — Mac Quick Look style |
| Video | Preview · **Mark In/Out** · **Trim** · **Cut Mid** · **+ Video** merge |
| PDF tools | While viewing a PDF → **PDF ▾** merge / split / extract / rotate / reverse / sign stamp |
| Convert to MD | **↓ MD** — saves next to original |
| Convert to PDF | **↓ PDF** — saves next to original |
| AI Chat | **Continue Chat** (needs API key) |
| Switch AI | Preview **AI:** button or tray → **Switch AI** (auto-fallback on quota/429) |
| Look up word | Highlight → right-click → **Look Up with AI** |
| API keys | Tray (blue dot) → **API Keys…** |
| Buy API | Shown in setup — **currently unavailable** |

Without an API key you can still preview and convert. AI summary/chat/lookup prompt you to add a key.

## Share / install on another PC

Build a zip **without your secrets**:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package-release.ps1
```

Upload `dist\NetieSpace-Setup.zip`. Recipients:

1. Unzip anywhere  
2. Run `Install.bat`  
3. Select a file → Space  
4. Optional: tray → **API Keys…** and paste Groq/OpenRouter  

No separate .NET install needed (self-contained). WebView2 Runtime is usually already on Windows 10/11.

↓ MD / ↓ PDF need Python + `requirements.txt` (or build with `-IncludeVenv` for a larger bundle).

Video crop / merge / cut-middle need FFmpeg:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\download-ffmpeg.ps1
.\install.bat
```

FFmpeg is copied into `dist\NetieSpace\tools\ffmpeg\` on publish.

## Setup Scripts

### .NET (SDK vs Runtime)

| Component | Purpose |
|-----------|---------|
| **SDK 8.0** | `dotnet build` |
| **Desktop Runtime 8.0** | Run non-published WPF builds |

Self-contained `dist\NetieSpace\NetieSpace.exe` does **not** need a global runtime.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-dotnet.ps1 -CheckOnly
```

Correct install script URL is `https://dot.net/v1/dotnet-install.ps1` — not `https://dot.net` (that downloads HTML).

### SF Pro fonts

`.dmg` files are macOS disk images. On Windows: extract `.otf` with 7-Zip or copy from a Mac, then `.\install-sfpro.bat`.

## Supported Conversions

| Input | To MD | To PDF |
|-------|-------|--------|
| PDF | pdfmd → pymupdf4llm → markitdown | copy |
| PPTX/PPT | pptx2md (with images) → markitdown | via MD |
| DOCX/XLSX | markitdown + enhance | via MD |
| Images | Groq Vision OCR | pymupdf |
| MD/TXT | pass-through / wrap | pymupdf Story |

## .env Keys

```env
GROQ_API_KEY=...          # Primary (fast)
OPENROUTER_API_KEY=...    # Fallback
SEA_LION_API_KEY=...      # Optional SEA model
```

Or use the in-app API Keys window (preferred for other users).

## Architecture

```
Explorer/Desktop Space → PreviewWindow (Space again closes; ↓/↑ next file)
  ├─ ExplorerFileService (Shell + desktop UIA + adjacent path)
  ├─ ContentParserService (DOCX mammoth HTML, WebView2 PDF, images, shell-info)
  ├─ ConversionService → tools/convert_engine.py (↓ MD / ↓ PDF only)
  └─ AiService (optional Gemini → Groq → SEA-LION → OpenRouter)
```

## Roadmap / parking lot

See [PARKING_LOT.md](PARKING_LOT.md) for deferred work (AI API optimize, Acrobat suite, rembg, etc.).

## Optional tooling

Heavy / research-only trees live under `optional/` (e.g. `optional/benchmark`, `optional/.venv312`) so the repo root stays lean.

## Python Setup

```powershell
.\.venv\Scripts\pip install -r requirements.txt
```

## Benchmark Reference

Based on `optional/benchmark/COMPARISON.md` (and DOCX bake-off under `optional/benchmark/outputs/docx_preview/`):
- **mammoth_html** — Space DOCX preview (images kept)
- **pdfmd** — fastest PDF→MD with headings + LaTeX
- **pymupdf4llm** — best heading/bullet structure for MD export
- **pptx2md** — best for native PowerPoint with images
- **markitdown** — universal Office fallback for **↓ MD** only
