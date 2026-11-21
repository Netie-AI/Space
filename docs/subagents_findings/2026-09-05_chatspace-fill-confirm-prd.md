---
keywords: chatspace, chatpanel, fill-sign, acroform, double-confirm, api-memory, sec-01, pointer-clicks, prd-amendment
main_idea: ChatPanel fill of the open PDF is still an unsigned Space PRD amendment plus Space#3. Cortex owns understanding and /api/memory (abstain when empty). Space may only confirm-then-apply after sign. OS/web button clicks are Pointer. Do not grow fill-profile.bin. Do not silent-fill EP FORM or offer.
date: 2026-09-05
kind: prd_intake
product: Space
---

# PRD intake - chatspace fill + double-confirm (Mode B)

PREFLIGHT: PARTIAL. Reused `2026-09-05_object-memory-prd-followup.md`, `2026-09-05_widget-align-execute.md`, `2026-09-04_pdfspace-prd-intake.md`.

Analog: TAS-SPACE live ChatPanel + Fill rail. TAS-CORTEX `/api/memory` query/assemble (lanes 12/17/18 SKIP second KB). TAS-POINTER `reviewPlan` for OS/web clicks. Do not unpark Palantir P1. Prefer consume Cortex then Space confirm UI. No Cortex PDF writer.

## Nearness (artifacts checked, no %)

Customer-facing artifacts: `PreviewWindow` ChatPanel, Fill & Sign Autofill on `netie-viewer.html`.

What a person can do today:

- Chat about the open file (ChatPanel -> `AiService.ChatAsync`; Cortex RAG/document_qa soft-fail).
- Manual AcroForm typing + type-aware Autofill from local DPAPI `FillProfileStore`.

What they cannot do:

- Ask chat to fill widgets or click PDF buttons.
- Pull facts from Cortex `/api/memory` into fill.
- See a confirm list before AI apply (no confirm rail in ChatPanel).

Landing: not fetched (Marketing owns building it). PRD still draft, not sliced. Open GitHub issues: only `Netie-AI/Space#3` SEC-01.

## Confirm / correct (live tree 2026-09-05)

- `OnChatSendRequested` only appends assistant text. No `PostWebMessage` from chat.
- Autofill is `PdfAutofill_Click` -> `autofillProfile`. Unmatched boxes stay empty.
- `CortexClient.cs`: `/health`, `/api/engine/run`, `/api/workflows/run`. No `/api/memory`.
- Cortex `memory_routes.py`: upsert/query/assemble/stats. Query requires a vector; empty store cannot invent facts.
- Space#3 still OPEN. `AiService` has no leave-machine gate.
- Founder: will not fill the offer or EP FORM from this session. Never silent clone.

## Ledger rows added

Space PRD-001 appended 25-26. Rows 1-6 and 8-24 unchanged. Row 7 still absent.

Cortex PRD-001 F23 ack only. No OpenVault row (F21 already keys-only). No tickets.

| # | Maps to | Call |
|---|---|---|
| Space 25 | no epic (refines 20) | NEEDS-YOU sign ChatPanel propose-then-confirm. Then ticket-later after Space#3. |
| Space 26 | split | PDF widget clicks = row 25. OS/web = Pointer. |
| Cortex F23 | ack | Memory + abstain already Cortex. Confirm UI is Space. No Cortex ticket. |

## Execute now

None. Do not touch ChatPanel, PreviewWindow, netie-viewer.html, FillProfileStore, CortexClient. Do not fill any live form.

## What Space may ship vs what waits

Ship now (already signed): Fill & Sign manual + bounded Autofill. Chat-over-file without widget writes.

Waits: founder sign of row 25; Space#3 gate-or-disclose; Cortex facts existing (abstain if empty); Cortex object schema (F22); then Space consumer of `/api/memory` with double-confirm UI. OS/web clicks wait on Pointer.

## NEEDS-YOU

Sign this Space clause or refuse it:

ChatPanel may propose AcroForm values and in-PDF checkbox/radio/push from Cortex `/api/memory`. Show the list. Apply only after human confirm. Extra confirm when uncertain or memory miss. Empty memory -> abstain, never guess. FillProfileStore stays the row-16 cap. OpenVault stays keys/passwords/gate.

## Refuse-and-why

Silent fill. Growing fill-profile.bin. Facts in OpenVault. ChatPanel as Pointer. Filling EP FORM / offer in this session. Unpark P1.
