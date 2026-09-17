---
keywords: cortex-fill-skill, fill-form, uacc, pointer, playwright, deepseek-harness, space#3, disclose-first, fill-only, airgpt-host, enquire-then-remember
main_idea: Cortex fills forms, not the Cursor writer. Fill-form skill lives in CortexOS/crew. Pointer-on-rail skips Space#3 widget API but still needs disclose. Space#3 is disclose-first not silent bypass. PDF this wave is FILL_ONLY. Markdown PRD later. Host is AirGPT. BAN DeepSeek Harness as a second orchestrator.
date: 2026-09-07
kind: prd_intake
product: Space
---

# PRD intake - Cortex fill skill (Mode B)

PREFLIGHT: HIT. Reused `2026-09-07_chatfill-split-apps-prd.md` (rows 34-38), `2026-09-05_chatspace-fill-confirm-prd.md` (OS clicks = Pointer), TAS-POINTER, TAS-CORTEX, TAS-CREW. Cortex F21-F28 already ack Space fill/memory.

Analog: TAS-CORTEX is the only engine (`agent_task.py` loop, `/api/memory`). TAS-CREW computer-use is UACC behind flags + per-call confirm. TAS-POINTER is hands/eyes (`reviewPlan`), not a second orchestrator. TAS-AIRGPT owns the host shell. UACC = OS mouse. Playwright = Chrome DOM. Do not dual-drive UACC+Pointer. BAN n8n / grok-bot / DeepSeek Harness as product. DISTILL plugin-loop/enquire into live Cortex+Crew. Palantir P1 stays parked. Empty Cortex memory = abstain. Never silent clone.

## Nearness (artifacts checked, no %)

Customer-facing artifacts: `PreviewWindow` ChatPanel, Fill & Sign on `netie-viewer.html`, Crew converse (no fill-form skill), Pointer HUD confirm.

What a person can do today:

- Chat about the open file (text only).
- Manual AcroForm + local DPAPI Autofill.
- Pointer confirmed clicks on any OS window, including Space, after `reviewPlan`.

What they cannot do:

- Type "Fill in the form for me" into Cortex/Crew and have related boxes filled.
- ChatPanel widget apply (`OnChatSendRequested` -> `AiService.ChatAsync` then `AddAssistantMessage` only).
- Space consume `/api/memory` (`CortexClient.cs` has no `/api/memory`).

Landing: not fetched (Marketing owns building it). PRD still draft, not sliced. Open GitHub: `Netie-AI/Space#3` SEC-01 OPEN.

## Verify (this turn)

| Claim | File | Result |
|---|---|---|
| Chat fill | `PreviewWindow.xaml.cs` `OnChatSendRequested` | Text only. No widget `PostWebMessage`. |
| Cortex memory client | `CortexClient.cs` | `/health`, `/api/engine/run`, `/api/workflows/run` only. |
| Cortex memory engine | `D:\Cortex\CortexOS\api\memory_routes.py` | upsert/query/assemble/stats live. Query needs a vector. |
| Fill-form skill | `D:\Cortex\CortexOS\crew\skill_packs` | No fill-form. `computer-reach.md` is outreach. |
| Crew computer-use | `crew/policy.py`, `connectors/computer_control.py` | Flag + arm + confirm. Execute sidecar_required. |
| Pointer confirm | `D:\Pointer\electron\netie\safety.js` `reviewPlan` | Live. `NETIE_CU_PLANNER` off by default. |
| Space#3 | `gh issue view 3 --repo Netie-AI/Space` | OPEN. Four undisclosed egress paths. Cheap path = disclose banner + indicator. |
| Loop | `CortexOS/execution/agent_task.py` | Quality-terminated tool loop on existing DAG. Not a second orchestrator. |
| Open Cortex epics | `gh issue list --label epic` | #11, #17, #34. No Crew/fill epic open. |

## Per-ask CALL

| # | Ask | Maps to | CALL |
|---|---|---|---|
| 1 | Cortex fills; writer does not type widgets; pick UACC/cloud/Pointer/Playwright; enquire then upsert | Cortex F13/F16/F22 + Space 25/39; no open fill epic | **SIGNED Cortex-owns-fill.** Skill in `CortexOS/crew` (not Cortex-crew). Loop = live `agent_task`. **Pointer-on-rail = no Space#3 widget API.** Vision/LLM still leave -> disclose. Empty memory abstain. BAN DeepSeek Harness product. DISTILL loop+plugin. Ticket-later for EPIC Agent. |
| 2 | Space#3 "bypass safety deliver first" | OPEN Space#3 | **SIGNED disclose-first / deliver.** REFUSE silent bypass. Do not close #3 by deleting the four paths. Cheap path = first-run banner + cloud-path indicator. Gate-through-OpenVault stays the full path. Disclose != ChatPanel PostWebMessage. |
| 3 | PDF remaining local tools | rows 8/14/28/36 | **FILL_ONLY.** Compress / page-numbers / crop stay PARK this wave. |
| 4 | Markdown separate PRD after Fill | row 37 | **SIGNED, queued.** Not this WIP. Do not vendor Milkdown/TipTap now. |
| 5 | Host try-all | row 32 | **AirGPT.** Do not slice a second host. |

## Pointer-on-rail vs Space#3

True: clicks on the already-open Space Fill rail do not need ChatPanel `PostWebMessage` or Space `CortexClient` `/api/memory`.

Still needs disclose: screenshots / vision / LLM plans leave via OpenVault (`POST /dms/secure`) or a cloud computer-use worker. That is leave-machine. Pointer `reviewPlan` confirm is necessary and not a silent local Autofill. Space#3 cheap banner still covers Space's own ungated `AiService` / Baidu / keys. Do not treat Pointer-on-rail as permission to skip the constitution.

## DeepSeek Harness

https://github.com/deepseek-ai/deepseek-harness -- MIT, "everything is a plugin", loop+tools.

| Verdict | Rule |
|---|---|
| **BAN** as product / third orchestrator | NETIE.md section 6. Cortex stays the only engine. Do not vendor the repo into Crew. |
| **DISTILL** | On-demand skills, tool loop until enquire-or-done, plugin-shaped allowlist. Land in `agent_task.py` + a fill-form skill pack. |
| **PARK** analog checkout | Study only if cloned later. Same class as mydeepagents: ideas yes, LangGraph/harness runtime no. |

## Seated writer NOW (no tickets from PRD)

`D:\Space`

1. Fill & Sign smoothness on `netie-viewer.html` (FILL_ONLY).
2. Space#3 step 1: first-run disclose banner + persistent cloud-path indicator. Do not close #3. Do not delete the four paths.
3. Not ChatPanel widget apply. Not `CortexClient` `/api/memory`. Not typing EP FORM widgets (row 34 HITL revoked). Not compress/page-numbers/crop. Not a Markdown product. Not a second host.

`D:\Cortex` (`CortexOS/crew`, not Cortex-crew)

1. Add `skill_packs/fill-form.md` OR a smart Crew prompt that loops KB + tools + enquire + `/api/memory` upsert after human answer.
2. Driver pick: UACC for OS (including Space window), Playwright for Chrome DOM, Pointer for HUD/confirm, remote computer-use only when the laptop cannot see the screen.
3. Not a second `dag_runner`. Not DeepSeek Harness vendor. Not silent upsert. Not dual-drive UACC+Pointer.

`D:\Pointer`

1. Keep `reviewPlan` as confirm. Do not grow a second orchestrator. Do not enable `NETIE_CU_PLANNER` and pretend Cortex planned if that route 404s.
2. Do not add Pointer `driver.js` SendInput next to UACC.

## WIP

At most two in-flight. One must be human-inspectable.

- Inspectable now: Space PDF Fill smoothness (FILL_ONLY) + Space#3 disclose banner (existing ticket step 1; avoid ChatPanel).
- Second lane: Cortex fill-form skill (different repo). Engine-change none. Consumer ChatPanel still Depends on Space#3.
- Markdown PRD queued after Fill. Host = AirGPT, not a lane.

```
Repo:            Netie-AI/Cortex
Contract impact: none (skill + existing /api/memory + agent_task)
Depends on:      none for the skill; Space ChatPanel consumer Depends on Netie-AI/Space#3
```

```
Repo:            Netie-AI/Space
Contract impact: none (consume live Cortex /api/memory later)
Depends on:      Netie-AI/Space#3
```

No tickets this session (EPIC Agent later for the fill-skill epic; Space#3 already exists).

## Ledger

Appended Space PRD-001 rows 39-43. Rows 1-38 unchanged. Row 7 still absent.

Cortex PRD-001 F29 (owns fill-skill slice) + F30 (ack Space#3 disclose). Cortex SoT finding: `D:\Cortex\docs\subagents_findings\2026-09-07_fill-form-skill-prd.md`.
