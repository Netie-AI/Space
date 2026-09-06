---
keywords: pdfspace, acrobat, fill-sign, p7, rust, golang, openedit, packet-2, prd-amendment, netie-space
main_idea: Founder PDFSpace/Acrobat ask is a PRD amendment, not a slice. Keep P7 parked. Fill v1 exists; Packet 2 shell and AcroForm /V do not. Do not implement.
date: 2026-09-04
kind: prd_intake
product: Space
---

# PRD intake - PDFSpace / Acrobat alternative (Mode B)

PREFLIGHT: MISS (no Space `docs/subagents_findings` yet; global subagents INDEX had no PDF/Acrobat HIT).

Analog: TAS-SPACE live lane (`D:\Space`). No frozen Acrobat clone. Prefer extend-live. BAN/SKIP/PARK list not triggered. Do not redesign layout DNA.

## Nearness (artifacts checked, no %)

Customer-facing artifact: `NetieSpace.exe` preview chrome in `src/NetieSpace/Views/PreviewWindow.xaml`.

What a person can open today:

- Space-key Quick Look. Header brand text `NETIE SPACE`. Top-right `EditModeButton` ("Edit") morphs the in-window PDF/video ribbon. Separate `TextEditButton` for text files.
- PDF ribbon chips: Organize, Sign, Fill (`PdfModeFillBtn` -> `enterFillMode`). Fill v1 click-cell / AcroForm-snap / type / A-A-delete / `DrawFillTexts` flatten bake.
- Video NLE + Stripe pay-first + cert-sign code are what `STATUS.md` Now actually describes.

What they cannot open:

- Packet 2 OpenEdit shell (Home / Tools / right rail / "Edit in app"). Grep for `OpenEdit` / `Edit in app` is empty. Audit `docs/research/findings/OPENEDIT_ACROBAT_AUDIT.md` still says the shell is not implemented.
- True AcroForm `/V` write-back. `GetFormFieldRects` is PdfPig read-only. `DrawFillTexts` appends page content.
- Press-to-autofill from personal memory. `ConversationMemoryCache` caches chat/lookup answers for token savings. `LocalEmbeddingBrain` stores consent-gated local summary embeddings (optional Rust `native/netie-embed`, else C# `ManagedSparseEmbed`).
- PDFSpace brand. STATUS Next: "Product identity later."
- Any Go hot path. Zero `.go` files. Rust sidecar is embeddings only.

Landing: `STATUS.md` cites netie.space for Buy. This intake did not fetch the live landing (Marketing owns building it).

GitHub: `Netie-AI/Space` origin/main at `b57dc5b` (merge PR #280). One open issue, `Netie-AI/Space#3` SEC-01 (leave-machine), not an epic. PRD status: draft, not sliced. `D:\Netie Space` path does not exist.

## Ledger rows added (PRD-001)

Appended rows 3-7 in `D:\Netie\Software Blueprint\Space\PRD-001-document-workspace-governed.md` (rows 1-2 unchanged).

| # | Maps to | Call |
|---|---|---|
| 3 | no epic (operational) | Execute: work in `D:\Space` only |
| 4 | no epic | PRD amendment: Acrobat workbench + PDFSpace brand |
| 5 | split | Fill snap polish is live P3; panels/animation = row 4 |
| 6 | no epic | PRD amendment: targeted Rust/Go sidecars. Do not reopen P7 |
| 7 | no epic; blocked on Space#3 | PRD amendment: AI fill / personal KB. Cortex already owns memory |

## Per-point routing

1. Header keep light Edit + add "Edit in app" Acrobat workbench -> **PRD amendment**. Packet 2 missing. Existing Edit is in-window ribbon morph, not a second app. AGENT_SYSTEM refuses a UI/IA redesign unless the PRD says replace surface. It does not.
2. Smoother drag/snap/fonts/panels/animation -> **split**. Snap/cell-detect already in `netie-viewer.html`. Workbench panels = amendment with (1).
3. Rust AND Go this phase, not whole-app rewrite -> **amendment, do not reopen P7**. Ledger row 1 already parked the full rewrite. Targeted sidecars are a new clause. Founder must assign Go a real job or drop the quota.
4. Fill bind / answer space / autofill / AI boxes / memory wording -> **amendment + SEC-01 block**. Fill v1 is flatten-only. TAS analog: SKIP a second memory product (live Cortex `/api/memory`).
5. Continue on D:\Space -> **execute now** (operational).

## Rust/Go snippet vs full rewrite

**Keep P7 parked.** `PARKING_LOT.md` / `docs/HUMAN_PARKED.md` say do not start a Rust/Go rewrite.

If founder signs a *narrow* native clause (not P7):

- Rust: geometry/snap/bind-to-box hot path (today `detectCellAt` does sync `getImageData` on click). Sibling of `native/netie-embed`, frozen stdin JSON. Contract additive.
- Go: only if founder assigns a job. Honest candidate is AcroForm `/V` mutate as a sidecar (MIT stack, no iText AGPL). Not "a Go file so the phase counts."
- C# WPF + pdf.js stay the app. No rewrite of `NetieSpace.exe`.

Wire vs consumer: sidecar protocol epic must not also switch the viewer. Split engine-sidecar -> contract bump -> `netie-viewer.html` adopts.

## PDFSpace brand vs Netie Space

**Keep Netie Space product identity until founder rewrites the press release.**

NETIE.md plane 4 still names the desktop app as Space (Quick Look). PDFSpace-as-replacement is a new PRD (or a signed rewrite of PRD-001 section 1), not a slice. A sku/subtitle ("PDF tools in Space") can wait; a rename of the header, installer, Stripe product, and landing is irreversible packaging.

## What may run now vs what needs founder

Execute now (no new epic):

- Stay on `D:\Space`. Migrate leftover C: patches into this tree.
- Do not implement Packet 2, PDFSpace rename, P7, or AI autofill.
- STATUS Next human checks (Export playbook, Stripe test card, Adobe Reader on cert-sign) stay human-parked.

Needs founder before any slice:

1. Identity: keep Netie Space vs PDFSpace sku vs replace the product.
2. Surface: authorize a second "Edit in app" shell, or keep Fill/Sign on the existing ribbon.
3. Native: keep P7 parked; allow targeted Rust snap sidecar; yes/no Go this phase and what it does.
4. Memory: Space consumes Cortex `/api/memory` vs local-only `LocalEmbeddingBrain` vs (refused) a third KB.
5. SEC-01 (`Netie-AI/Space#3`): OpenVault gate vs disclosure before any AI fill.
6. WIP: STATUS Now is already a video + Stripe + cert train. Acrobat cannot become a third in-flight lane until one of those moves to Later.

## Ordered next 2 epics (only if founder signs the narrow path)

Refuse to slice until the amendments above are signed. Conditional proposal if founder says: keep Netie Space, keep P7 parked, no Packet 2 this wave, Fill bind on the live ribbon, optional Rust snap sidecar:

1. **EPIC (foundation)** Native snap sidecar JSON protocol (Rust). Repo `Netie-AI/Space`. Contract impact: additive. Depends on: founder native yes. Not human-inspectable alone.
2. **EPIC (capability, inspectable)** Fill & Sign bind-to-box + answer field on the existing ribbon (`PdfModeFillBtn` / `enterFillMode`). Repo `Netie-AI/Space`. Contract impact: none. Depends on: epic 1 if sidecar is in; else none.

Pair them (WIP 2, one visible). Do not file GitHub issues until PRD-001 is sliced after sign-off.

Contention if Packet 2 is also authorized: `PreviewWindow.xaml`, `PreviewWindow.xaml.cs`, `netie-viewer.html`. Ask for a DR before two epics touch those files.

## Refuse-and-why (Mode A)

- PRD is still draft, not sliced.
- Acrobat-alternative / PDFSpace / Packet 2 / AI personal-KB fill have no authorizing clause.
- Packet 2 is an IA redesign with no "replace surface" line.
- Success assertion today is Space-key preview + OpenVault keys, not Acrobat Pro.

## Verified files

- `src/NetieSpace/Views/PreviewWindow.xaml` - Edit vs TextEdit vs Fill/Sign/Organize
- `src/NetieSpace/Views/PreviewWindow.xaml.cs` - `enterFillMode`, `GetFormFieldRects` push, `DrawFillTexts`
- `src/NetieSpace/Services/PdfEditService.cs` - read-only rects; flatten bake
- `src/NetieSpace/Assets/pdfjs/netie-viewer.html` - `enterFillMode`, `fillFieldRectAt`, `detectCellAt`
- `src/NetieSpace/Services/ConversationMemoryCache.cs` - token cache
- `src/NetieSpace/Services/LocalEmbeddingBrain.cs` + `native/netie-embed` - embeddings
- `STATUS.md`, `PARKING_LOT.md` P3/P7, `docs/HUMAN_PARKED.md`, `OPENEDIT_ACROBAT_AUDIT.md`
