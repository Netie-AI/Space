---
keywords: chatfill, split-apps, space-25, space#3, api-memory, web-container, enquire-then-remember, pdf-toolkit, markdown-product, themarkdowneditor, hitl-fill
main_idea: Chat cannot fill forms today. Blocker Space#3. HITL help-fill with founder-supplied facts is execute-now. Suite packing PARK. PDF stays the Space product (cloud PARK). Markdown is its own product NEEDS-YOU. Web container is live WebView2; Cortex /api/memory consume waits Space#3.
date: 2026-09-07
kind: prd_intake
product: Space
---

# PRD intake - chat fill + split apps (Mode B)

PREFLIGHT: HIT. Reused `2026-09-07_space25-signed-lag.md`, `2026-09-05_chatspace-fill-confirm-prd.md`, `2026-09-05_object-memory-prd-followup.md`, `2026-09-07_multieditor-goal-prd.md`, `2026-09-07_multieditor-goal-pdf-bbox.md`.

Analog: TAS-SPACE live ChatPanel + Fill rail + `FillProfileHarvest` confirm-list. TAS-CORTEX `/api/memory` (lanes 12/17/18 SKIP second KB). TAS-POINTER `reviewPlan` for OS/web clicks. TAS-AIRGPT is the host shell. Prefer extend-live WebView2 as the web container. Do not unpark Palantir P1. BAN n8n / grok-bot / OnlyOffice AGPL / GPL / leaked trees. P7 PARK. Empty Cortex memory = abstain. Do not silent-fill EP FORM.

## Nearness (artifacts checked, no %)

Customer-facing artifacts: `PreviewWindow` ChatPanel, Fill & Sign on `netie-viewer.html`, `Assets/markdown/md-editor.html`.

What a person can do today:

- Chat about the open file (`OnChatSendRequested` -> `AiService.ChatAsync`; text only).
- Manual AcroForm + local DPAPI Autofill (`PdfAutofill_Click` -> `autofillProfile`).
- Confirm-before-write analog for profile harvest (`FillProfileHarvest.Propose`).
- MD dual-pane Split/Source/Preview (`md-editor.html` + marked MIT). CSV grid exists but is not the PDF product.

What they cannot do:

- Ask chat to write widgets or show a confirm-apply list.
- Pull or upsert Cortex `/api/memory` from Space (`CortexClient.cs` has no `/api/memory`).
- Rely on leave-machine gating (`AiService` still ungated; TAS-SPACE section 4; `Netie-AI/Space#3` OPEN).
- Match https://themarkdowneditor.app/ (paid macOS; chrome-hide, LaTeX, GitHub sync, AI-in-editor).

Landing: not fetched (Marketing owns building it). PRD still draft, not sliced. Open GitHub: only `Netie-AI/Space#3` SEC-01.

## Verify (this turn)

| Claim | File | Result |
|---|---|---|
| Chat fill | `PreviewWindow.xaml.cs` `OnChatSendRequested` | Text only. `ChatAsync` then `AddAssistantMessage`. No widget `PostWebMessage`. |
| Cortex memory client | `CortexClient.cs` | `/health`, `/api/engine/run`, `/api/workflows/run` only. |
| Cortex memory engine | `D:\Cortex\CortexOS\api\memory_routes.py` | upsert/query/assemble/stats live. |
| Space#3 | `gh issue view 3 --repo Netie-AI/Space` | OPEN. Only open Space issue. |
| Fill store | `FillProfileStore.cs` | DPAPI CurrentUser `fill-profile.bin`. Row-16 cap. Not Cortex. |
| MD v1 | `Assets/markdown/md-editor.html` | Split/Source/Preview + marked GFM + autosave. |
| Online host | TAS-AIRGPT + ledger 32 | AirGPT owns host shell. Hosted Space still NEEDS-YOU. |

## Per-ask CALL

| # | Ask | Maps to | CALL |
|---|---|---|---|
| 1 | Chat inside Space then AI fills ALL forms; else HITL help fill now | signed 25/31; no open epic (PRD unsliced) | **maps signed clause; ticket-later after Space#3.** HITL help-fill = **execute-now** (no tickets): founder-supplied facts only. Chat widget writes = **BLOCKED Space#3**. Silent EP FORM = **refuse**. |
| 2 | Multi-format is a different app; stop packing suite into Space | rows 10/33; P4/P5/P8 | **amendment SIGNED / PARK packing.** Do not rip shipped rembg/NLE/MD-CSV v1. Press-release rewrite = **NEEDS-YOU**. |
| 3 | PDF product = ALL local Acrobat/iLovePDF-class tools; iLove cloud PARK | rows 8/28/29; distill 2026-09-06 | **maps signed Fill.** Remaining *local* gaps = **NEEDS-YOU named tools**. Cloud / e-Sign / repair / PDF-A / compare / redact = **PARK**. P7 rewrite **PARK**. |
| 4 | Markdown = ALL themarkdowneditor.app UX as own product, not PDF side feature | row 33 engine; PARKING_LOT markdown | **amendment NEEDS-YOU** (new Markdown PRD vs Space-only). DISTILL UX. Do not clone paid macOS. Milkdown/TipTap still unsigned. GitHub-sync / AI-in-editor **PARK**. Do not open as second WIP while PDF is inspectable. |
| 5 | Web container + Cortex memory API + enquire-then-remember | signed 25; Cortex F23/F27; row 32 | **extend-live WebView2** (not a new host). Cortex API already live -- Space consume is contract none, **Depends on Space#3**. Enquire-then-upsert = signed 25 + F28 ack. Hosted upload container = row 32 **NEEDS-YOU**. |

## Honest fill answer

No. They cannot chat in Space and have the AI build and fill all forms.

Blocker: `Netie-AI/Space#3` SEC-01 (gate-or-disclose). Secondary: `CortexClient` has no `/api/memory`; ChatPanel never writes widgets.

## Seated writer NOW (no tickets)

Allowed:

1. Sit with the founder on the live Fill & Sign rail.
2. Ask each unknown field; type only facts the founder just gave.
3. Use Autofill from `FillProfileStore` for bounded keys already saved.
4. Continue local PDF smoothness already signed (rows 8/14/28/32) on `netie-viewer.html`.

Not allowed:

- Invent PII / silent-fill EP FORM / offer.
- ChatPanel `PostWebMessage` widget applies.
- `CortexClient` `/api/memory` (consumer before Space#3).
- Grow `fill-profile.bin` into an object store.
- Pack image/video/pptx/word/csv further into Space.
- Vendor Milkdown/TipTap or clone themarkdowneditor.app.
- Unpark P7 / P1 / iLove cloud / OnlyOffice AGPL.

## WIP

At most two in-flight. One must be human-inspectable.

- Inspectable now: Space PDF Fill workbench (pageCssBox shipped; human still needed on EP FORM).
- Do not open markdown-product or chat-fill-consumer as a second in-flight lane.
- Widget-from-chat still Depends on `Netie-AI/Space#3`.
- Never engine-change + consumer in one epic. Memory engine already exists.

```
Repo:            Netie-AI/Space
Contract impact: none (consume live Cortex /api/memory)
Depends on:      Netie-AI/Space#3
```

No tickets this session (EPIC Agent later, only after Space#3 + founder press-release calls).

## Ledger

Appended Space PRD-001 rows 34-38. Rows 1-33 unchanged. Row 7 still absent.

Cortex PRD-001 F28 ack only. No OpenVault row (keys only already F21).

## NEEDS-YOU (does not override Space#3 block)

1. Space#3: gate through OpenVault, or disclose, before chat fill / memory upsert from Space.
2. Rewrite Space press release: PDF-local toolkit vs still Quick Look + video/image.
3. Name remaining local PDF tools to unpark (compress / page-numbers / crop?) vs stay PARK.
4. Markdown: new product PRD vs keep Space text-edit v1; then engine (Markdig+marked / Milkdown / TipTap core).
5. Row 32: hosted Space vs refuse (AirGPT already owns host shell).
