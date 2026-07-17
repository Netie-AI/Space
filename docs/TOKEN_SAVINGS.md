# Token savings (ponytail-inspired, C# — not vendored)

[ponytail](https://github.com/DietrichGebert/ponytail) is a **YAGNI ruleset for coding agents** (JS/Python plugins for Claude Code, Cursor, etc.). It is **not** a token-cache library. Netie Space does **not** vendor that repo.

We borrow the *ladder* idea: **stop at the first rung that holds** — apply it to **preview AI cost**, not to codegen.

```
1. Does this need a remote call?     → no: local extractive summary / cache hit
2. Already answered this fingerprint? → reuse session memory
3. Small model enough?               → SummaryModel / preferSmallModel
4. Only then: truncated excerpt + API
```

## What Netie ships

| Mechanism | Where | Effect |
|-----------|--------|--------|
| **Hash summary cache** | `SummaryCache` | SHA-256 of content sample (or path\|size\|mtime). Same file bytes → no second summary call. Toggle: `Preview:CacheSummaries` in `appsettings.json`. |
| **Skip AI on small files** | `LocalSummaryService.IsSufficientWithoutAi` | Text under ~2.5k chars with a usable local outline → return local bullets, 0 tokens. Images still call vision. |
| **Aggressive truncation** | `LocalSummaryService.TruncateForAi` + `AiService` caps | Head+tail excerpts (normal / huge / chat-repeat budgets) to stay under free-tier TPM. |
| **Cheaper model routing** | `preferSmallModel` + per-provider `SummaryModel` | Summaries & Look Ups always prefer the small model; huge / follow-up chat too. Groq 413 → shrink + retry. |
| **Conversation fingerprints** | `ConversationMemoryCache` | Look Up / identical first-chat Q on same excerpt → cached answer. Follow-up chat turns send a **slim** remnant (~1.8k) + local outline instead of re-sending ~9k chars. |

## Intentionally not copied

- Ponytail’s agent plugins, hooks, `/ponytail` commands, or AGENTS.md ruleset
- Any Node/Python runtime dependency
- “Write less code” prompting inside Netie’s chat (Netie answers questions about *your files*, it does not edit them)

If you want ponytail for **Cursor/Claude coding** of this repo itself, install it as an editor plugin — keep it out of the WinForms/WPF app binary.

## Knobs

- `appsettings.json` → `Preview.CacheSummaries`
- Env / settings → `SummaryModel` per provider (Groq `llama-3.1-8b-instant`, etc.)
- Constants in `AiService` / `LocalSummaryService.SmallDocChars` if you need tighter/looser budgets

## Mental model

Ponytail: *the best code is the code you never wrote.*  
Netie token layer: *the best tokens are the ones you never sent.*
