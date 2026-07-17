# Interaction & architecture audit — Jul 2026

## Latency budgets (ship targets)

| Milestone | Target |
|-----------|--------|
| Space acknowledge | ≤100ms |
| First paint (thumb/stub) | ≤300ms local |
| Useful preview | ≤1s light files |
| Esc / arrow UI reaction | ≤100ms |
| Cancel previous on arrow | immediate; never paint stale |

## Best architecture (target)

```
Shell host (tray + LL hook + STA COM helper)
  ├─ QuickLookSurface (ephemeral Space/Esc/arrows, cancel+generation)
  └─ WorkbenchSession (Edit PDF/Video — no sibling travel; dirty Esc confirm)
         └─ ContentRenderer (WV2 / Image / Media) + Jobs (COM/ffmpeg off UI)
```

## Fixed this pass

1. **WebView typing probe** — Space/Esc/arrows skip when focus is input/textarea in WV2
2. **Esc respects typing** — chat / dialogs / WV inputs sacred
3. **Travel serialized** — `_travelInFlight` + generation token
4. **Edit mode** — no sibling travel while Edit
5. **OpenPreviewAsync reload** — no close-and-bail when already open
6. **Real WebView2 warmup** — idle EnsureCoreWebView2
7. **AreBrowserAcceleratorKeysEnabled=false** — Peek #27534 class
8. **pdf.js renderGen abort** — stale zoom/travel renders exit

## Stress scripts

```powershell
.\scripts\stress-preview-matrix.ps1
.\scripts\stress-interactions.ps1
```

## Still open (next)

- Progressive first paint for PDF/DOCX (not only PPTX)
- Hard kill PowerPoint/LO on cancel
- Split PreviewWindow into QL / PdfWorkbench / VideoWorkbench
- DropShadow removal on Login/ApiSetup glass dialogs
- Stamp autosave vs WV file lock

## Online cases (browser MCP / cursor-ide-browser)

| Case | Result |
|------|--------|
| PowerToys Peek [#27534](https://github.com/microsoft/PowerToys/issues/27534) | Confirmed: WebView2 steals arrows after click — Netie owns keys via LL hook + typing probe |
| QL-Win QuickLook [#1757](https://github.com/QL-Win/QuickLook/issues/1757) | Unobserved WebView2 init crash — we catch warmup + Ensure; unique UserDataFolder |
| License Worker | MCP chrome-error (network); app uses `SkipGate: true` locally |
| pdf.js viewer @ `:8877` | Left-click zoom/fit/highlight OK; Search open + type `report` + Enter OK; zoom→155% |

Note: no separate Playwright MCP — used **cursor-ide-browser** (CDP). Port 8765 was occupied by another local app.
