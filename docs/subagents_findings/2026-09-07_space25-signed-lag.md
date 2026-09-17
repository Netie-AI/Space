---
keywords: space-25, chatpanel, propose-then-confirm, fill-sign, space#3, sec-01, api-memory, lag, online-host, ep-form, abstain
main_idea: Founder SIGNED Space-25 ChatPanel propose-then-confirm in Cursor (not Space chat). Widget-from-chat tickets still wait on Space#3. Empty Cortex memory abstain; no EP FORM fill. Local lag execute-now on WPF/sidecars; whole rewrite PARK P7; online host NEEDS-YOU.
date: 2026-09-07
kind: prd_intake
product: Space
---

# PRD intake - Space-25 signed + lag/host-online (Mode B)

PREFLIGHT: HIT. Reused `2026-09-05_chatspace-fill-confirm-prd.md`, `2026-09-06_ilove-parity-prd.md`.

Analog: TAS-SPACE live ChatPanel + Fill rail + `FillProfileHarvest` confirm-list. TAS-CORTEX `/api/memory` (lanes 12/17/18 SKIP second KB). TAS-POINTER `reviewPlan` for OS/web clicks. TAS-AIRGPT is the host shell. Prefer extend-live. Do not unpark Palantir P1. BAN n8n / grok-bot / AGPL / GPL / leaked trees. Do not redesign layout DNA.

## Nearness (artifacts checked, no %)

Customer-facing artifacts: `PreviewWindow` ChatPanel, Fill & Sign Autofill on `netie-viewer.html`.

What a person can do today:

- Chat about the open file (`OnChatSendRequested` -> `AiService.ChatAsync`; text only).
- Manual AcroForm + local DPAPI Autofill (`PdfAutofill_Click` -> `autofillProfile`).
- Confirm-before-write analog already live for profile harvest (`FillProfileHarvest.Propose`).

What they cannot do:

- Ask chat to write widgets or show a confirm-apply list.
- Pull Cortex `/api/memory` into fill (`CortexClient.cs` has no `/api/memory`).
- Rely on leave-machine gating (`AiService` still ungated; TAS-SPACE section 4).

Landing: not fetched (Marketing owns building it). PRD still draft, not sliced.

## Sign event

Founder signed Space-25 in Cursor, not Netie Space chat. They typed "sign space-25" into Space chat by mistake; Gemini failed with providers unavailable. Intent is ACCEPT ChatPanel propose-then-confirm for Fill & Sign.

Ledger row 25 marked **SIGNED / accepted**. Row 31 records the Cursor channel. Row 27 NEEDS-YOU is closed by 31. PDF-widget path of row 26 rides the signed clause; OS/web stays Pointer.

## Still blocked

`Netie-AI/Space#3` SEC-01 before any ticket that writes widgets from chat.

```
Repo:            Netie-AI/Space
Contract impact: none (consume live Cortex /api/memory)
Depends on:      Netie-AI/Space#3
```

No engine+consumer in one epic. No Cortex PDF writer. No tickets this session (EPIC Agent later).

## Refuse this session

- Silent EP FORM fill. Filling EP FORM / offer. Fact invent while Cortex memory is empty (abstain).
- Grow fill-profile.bin. ChatPanel as Pointer. Unpark P1.

## Lag / host-online (not previously in a 2026-09-07 finding)

Split of the same dump:

1. Local lag fixes -- **execute-now** on live WPF + existing sidecars (ledger rows 1/6/8). Not ChatPanel widget writes. Not a new epic.
2. Whole-app Rust/Go rewrite -- **PARK P7**.
3. Online host (Space as web/cloud product) -- **NEEDS-YOU**. Press-release rewrite. Windows desktop Quick Look stays the PRD. AirGPT already owns the host shell. Do not slice a second host.

Ledger row 32.

## Execute now

None in this PRD-agent run. No ChatPanel writes. No EP FORM. Seated writer may still do ledger 28 overlay drag/resize and local lag on existing paths.

## Cortex / OpenVault

Cortex F27 ack only. Empty memory -> abstain. OpenVault unchanged (keys only).
