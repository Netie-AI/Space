# Space Peek/QL stability pass

**keywords:** space, peek, quick-look, topmost, sign, save-close, flush-serialize, focus-gate, typing-passthrough  
**main_idea:** Space/Esc/arrows only swallow when Explorer or our HWND owns focus; PDF flush is single-flight and close waits for it; Sign arms a stamp instead of toggle-dead-click; HWND_TOPMOST is re-asserted without Activate.

## Shipped (2026-09-03)

1. **Focus gate** (`App.ShouldSwallowPreviewKeys`) — Chrome/Word Space types a space; preview stays floating.
2. **Save-then-close** (`PreviewWindow.RequestCloseAsync` + `_pdfFlushTask`) — no dual bake; fail keeps window open.
3. **Topmost** (`Win32Api.EnsureTopmostNoActivate` + `WM_WINDOWPOSCHANGED` hook) — no Activate steal.
4. **Sign** — `ArmSignPlacementAsync` + textLayer `pointer-events: none` in stamp-mode.
5. **Fonts/motion** — BodyFont drops Bahnschrift; PopIn on Sign/Fill panels; RootBorder stays fade-only.

## Verify

```powershell
dotnet build src/NetieSpace/NetieSpace.csproj -c Debug
powershell -File scripts/check-flush-serialize.ps1
# Manual: Explorer Space PDF -> Edit -> Sign -> place -> Space closes after save
# Manual: preview open, focus Notepad, Space types a space
```

## Parked

IPreviewHandler, Mission Control overview, Acrobat Protect/OCR/export, click-away hide.
