# Preview sharpness + Explorer rename + Organize

**keywords:** blur, fit, zoom-pref, explorer-f2, rename, organize, checkbox, peek, aspect  
**main_idea:** Space ignores Explorer F2 label-edit; PDF window sizes to page aspect and remembers zoom %; no Edit height jump; Organize click/type ticks + peeks with batch ops.

## Shipped (2026-09-04)

1. `Win32Api.IsExplorerLabelEdit` — Edit caret under SysListView32/DirectUIHWND/Desktop; Space types a space.
2. `FileNameEditBox` counts as typing in `IsTypingOrEditCapture`.
3. `PositionOnOwnerMonitor(pageW, pageH)` — aspect chrome, not 70% ultrawide; Edit no longer bumps Height.
4. `UserPrefs.PdfZoomPercent` (0 = Fit); viewer posts `{type:zoom}`; host restores on ready.
5. PDF `ZoomFactor=1`, pinch off; canvas `crisp-edges`; one status line (`ConvertStatusText`).
6. Organize: `SelectionMode=Extended`, checkbox `IsTicked`, Go-to box, click peek+tick, batch Up/Down/Rotate/Delete.

## Verify

```powershell
dotnet build src/NetieSpace/NetieSpace.csproj -c Debug
# F2 rename PDF in Explorer, type Space — character only
# Space portrait PDF — letterbox, not ultrawide; zoom +/-; reopen keeps %
# Edit — ribbon, no delayed size jump
# Organize — click page peek; type 4 Enter tick; Ctrl multi delete
```
