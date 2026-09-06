---
keywords: fill-profile, object-memory, ontology, digital-clone, api-memory, sec-01, graphiti, named-objects, ai-fill, prd-amendment
main_idea: Extra My-details cards need a named field list; named objects, AI fill, and a user clone are not a Space product. SKIP a second KB. Cortex /api/memory already owns memory. Palantir P1 stays parked. Space#3 blocks any utterance or form that would leave the machine.
date: 2026-09-05
kind: prd_intake
product: Space
---

# PRD intake - object memory / clone (Mode B)

PREFLIGHT: HIT. Reused `2026-09-05_widget-align-memory-prd.md` and `2026-09-05_widget-align-execute.md`.

Analog: TAS lanes 6/12/17/18 live Cortex `/api/memory` + `/api/context` assemble (`netie-context-graph`). SKIP second memory product in Space. SKIP Graphiti/Zep/Letta/Mem0/Mempalace vendors. Lane 7 Palantir P1 PARK. Do not rewrite Space into a personal-KB product. WP-004 company clone is DRAFT, not law.

## Nearness (artifacts checked, no %)

Customer-facing artifacts: `FillProfileWindow` (My details), Fill-rail Autofill on `netie-viewer.html`, `PreviewWindow` ChatPanel.

What a person can do today:

- Open My details from Settings, tray, or Fill rail. Store FullName, Email, Phone, Address, DateOfBirth, Nationality, DateFormat in DPAPI `fill-profile.bin`.
- Autofill empty AcroForm widgets by type (name/email/phone/address/DOB/nationality). Unmatched boxes stay empty. Bounded harvest on Save for those keys only.
- Chat about the open file (ChatPanel -> AiService). Optional Cortex RAG / document_qa soft-fail.

What they cannot do:

- Store parents / company / previous-work cards.
- Named objects ("this is my friends details, save the object").
- Ask chat to fill or edit the open PDF.
- A person ontology, knowledge graph of people, or digital clone of the user.

Landing: not fetched (Marketing owns building it). PRD still draft, not sliced. No open Space epics. Ledger row 7 still missing (append-only; not inserted).

## Confirm / correct (live tree 2026-09-05)

- `FillProfileStore.Profile` = FullName, Email, Phone, Address, DateOfBirth, Nationality, DateFormat, DateToday (computed). `MergeHarvest` keys: name, email, phone, address, dob, nationality, dateFormat. DPAPI CurrentUser. No upload.
- `FillProfileWindow.xaml` is one My details dialog (not PromptDialog). Fields match the Profile record above.
- ChatPanel cannot fill AcroForm. No PostWebMessage / autofillProfile from chat. Autofill is PreviewWindow `PdfAutofill_Click` only.
- Cortex `/api/memory` is not imported in D:\Space. `CortexClient.cs` hits `/health`, `/api/engine/run`, `/api/workflows/run` only. Live engine: `D:\Cortex\CortexOS\api\memory_routes.py`.
- `ConversationMemoryCache` = token cache. `LocalEmbeddingBrain` = consent-gated embeddings + `file-graph.jsonl` (files, not people).
- `Netie-AI/Space#3` SEC-01 still OPEN (leave-machine on AiService, Baidu OCR, plaintext keys, vault fallback).

## Ledger rows added (PRD-001 Space)

Appended 18-21. Rows 1-6 and 8-17 unchanged. Row 7 still absent.

| # | Maps to | Call |
|---|---|---|
| 18 | split vs morning cap | Extra local cards NEEDS-YOU (named keys or none) |
| 19 | no epic | Named objects + save-from-utterance = amendment + Space#3. Consume Cortex memory if ever signed. |
| 20 | no epic | AI fill of open PDF = amendment + Space#3. Chat cannot write widgets. |
| 21 | no epic | Ontology / KG / user clone = refuse in Space. P1 parked. WP-004 is company clone. |

Cortex PRD-001 F21 appended as ack only (no Cortex ticket).

## Execute now

None. Do not touch FillProfileStore.cs, FillProfileWindow.xaml, PreviewWindow.xaml.cs, ChatPanel, netie-viewer.html for this ask.

CAP if row 18 is later signed: extra string keys on the same one-self Profile, DPAPI only, no objects, no AI parse, no Cortex client, Autofill matchers only for typed keys.

## Analog verdict

SKIP a second memory product in Space. Prefer consume Cortex `/api/memory` + `/api/context` assemble when/if the founder signs Space as a thin consumer after Space#3. Do not rewrite the Space press release into a personal-KB product. Do not unpark Palantir P1. Do not vendor Graphiti.

## NEEDS-YOU

1. Extra DPAPI cards: name the keys (or keep the morning cap).
2. Named objects: Space-as-Cortex-memory-consumer vs refuse. Default analog: consume Cortex, never a Space store.
3. AI fill: after Space#3 gate or disclose, still a Space PRD amendment (success assertion is Space-key preview, not an AI form-filler).
4. User clone / ontology in Space: refuse vs rewrite press release. Default: refuse. WP-004 stays company clone and DRAFT.

## Refuse-and-why

Ontology + knowledge graph + digital clone of the user inside Space. Second KB. Graphiti/Letta/Mem0/Mempalace vendors. Unpark P1. Growing fill-profile.bin into named objects. Chat-driven AcroForm fill before amendment + SEC-01.
