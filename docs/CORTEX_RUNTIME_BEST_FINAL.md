# Netie Cortex — best runtime learnings (final handoff)

**Purpose:** Implement against this file next session. Covers DeepSeek-style context ditching, WASM safety, hosting, dual-brain, and Space↔AirGPT↔Cortex.

**Repos:**  
- Space: `D:\Netie Space`  
- Cortex: `D:\Cortex` (or peer Cortex tree)  
- AirGPT: `D:\AirGPT`
- OpenVault: `D:\OpenVault`

**Loop gates:** Grok 4.5 + Composer (not Fable).

---

## 1. eSuite dual-brain map

```
Netie Space  ──preview/edit/handoff──►  AirGPT  ──optional sidecar──►  Cortex (:8000)
     │                                       │
     └──── token discipline (ponytail) ──────┘
```

| Product | Role now | Role later |
|---------|----------|------------|
| **Space** | Quick Look + CapCut-light + Acrobat-light | Desktop control plane for all file types |
| **AirGPT** | Deep chat / vault / ditch_context | Personal brain + clipboard |
| **Cortex** | Governed DMS brain (F1–F6 PASS) | Company dual-brain: execute + plan |

Do **not** merge codebases. Share contracts: env keys, `--preview`, `cortex_client`, ditch tiers.

---

## 2. DeepSeek → ditch tiers (systems analogy)

DeepSeek-V4 compresses **KV along sequence** (CSA ~4, HCA ~128, SWA recent full). We do **not** reimplement model KV. We mirror the idea for **agent chat/tool memory**:

| Tier | Analogy | Keep | Drop / compress |
|------|---------|------|-----------------|
| **SWA** | Recent full | System + last user + last N turns | — |
| **CSA** | Mid digest | Tool results → short digests | Raw dumps |
| **HCA** | Old summary | One paragraph + pinned facts | Full old turns |

**Source of truth today:** `D:\AirGPT\db.py` → `ditch_context(messages, budget_chars=12000, pinned=...)`.

**Port targets:**
1. Cortex: `CortexOS/ponytail/middleware.py` — replace naive truncate with scored ditch + pin compliance facts
2. Space: `ConversationMemoryCache` / chat history before provider call
3. Shared tests: budget never exceeded; system + last user never ditched

---

## 3. WASM safety contract

**Today:** Space stub `scripts/cortex_wasm_isolate.py` + `CortexWasmPolicy.cs` (fuel + 4MiB, no FS/net, scaffold 42). CortexOS `wasm_isolate.py` still owns wasmtime when that repo is in play.

**Ship stubs:**
1. Deny-by-default imports (no WASI FS/network) — done in Space stub
2. Pack-policy fuel/memory ceilings — 50k fuel / 64 pages (4MiB)
3. Hard timeout + kill; audit every run
4. Tests: fuel exhaust, OOM pages, missing export, disabled via `CORTEX_WASM_DISABLED` (`tests/test_cortex_wasm_stubs.py`)
5. Demo may disable; production default ON (env unset)

Firecracker / full isolate stays in `PARKING_LOT` until enterprise client.

---

## 4. Device adapters (one runtime)

| Device | Adapter | Notes |
|--------|---------|-------|
| Windows desktop | Space WPF + optional Cortex local API | Space stays offline-capable after license |
| Laptop demo | `demo/run_demo.ps1` | SQLite ledger OK |
| NAS / VPS | Phase 0 `deploy/docker-compose.yml` + Caddy | Postgres + API + UI |
| Phone | AirGPT tunnel → Cortex gate | Soft-fail if Cortex down |

Pack plugins (`packs/dms`) carry vertical logic; CortexOS stays thin.

---

## 5. Company dual-brain (H3 vs now)

| Brain | Job | When |
|-------|-----|------|
| **Execution** | intake → classify → suggest → human → F5 gate → execute → ledger → skills | **Now** (shipped F1–F6) |
| **Planning / world-model** | goal → safe path → propose DAG | **H3 park** — after 3+ paying pilots |

Invariant: captured skills rank F4 suggestions only — **never** mutate F5 YAML.

---

## 6. Gatekeeper protocol

| Role | Model | Scores |
|------|--------|--------|
| Builder | Session parent | Implements one slice |
| **Gate A** | **Grok 4.5** | Smoothness + pass criteria completeness |
| **Gate B** | **Composer** | Crash risk, honesty, scope creep, code quality |

Advance only if **both PASS**. 3 consecutive FAILs → park slice → next passable item.  
State file: `Netie Space/docs/LOOP_STATE.md`.

---

## 7. Stress checklist

1. `scripts\publish.ps1` (never stale `dist`)
2. `scripts\stress-edit.ps1`
3. Space open: PDF, video, PPTX, DOCX, image, MD
4. Cortex: `pytest tests/ -q` ≥ baseline; `verify_all.ps1` when demo up

---

## 8. Ordered backlog (next implement)

### Space Pillar 1 (Video) — active
1. ~~SRT burn-in on export~~ shipped
2. ~~Selection affordance~~ shipped (gold border; no DropShadow — stack overflow risk)
3. ~~A1 ripple-delete/pack~~ shipped
4. Stability: no pack:// Icon; coalesced timeline layout; no working-set clamp
5. Gate both models after user Export stress

### Known crash class (2026-07-14)
- Windows Event `0xc00000fd` stack overflow: DropShadowEffect + `AllowsTransparency` + timeline layout
- Fix: remove DropShadow; `ScheduleTimelineLayout` only; never MessageBox on layout errors

### Space Pillar 2 (PDF)
OCR + in-place text; toolbar discipline; stamp visibility

### Space Pillar 3 (Shell/Docs)
Cite / First Principles / Mindmap; ditch_context in Netie; smoke all types

### Space Pillar 4 (optional)
`marketing/` logo, screenshots, landing

### Cortex parallel
1. Ponytail ditch tiers  
2. WASM safety stubs + tests  
3. Phase 0: `demo/env.example`, `deploy/docker-compose.yml`, Caddy, verify green  

### Park
Higgsfield generative video · cert PDF signatures · Rust rewrite · Stripe Checkout · H3 world-model
