# Cursor vs mission GPU / inference (honest map)

Canonical workspace: `D:\Netie Space`.

## What Cursor gives you

| Need | Cursor? | How |
|------|---------|-----|
| Coding agents (chat, Task/subagents, Cloud Agents) | Yes | IDE + [Cloud Agents API](https://cursor.com/docs/cloud-agent/api/endpoints) + `@cursor/sdk` |
| Hosted **coding** model inference (Grok/Claude/Composer in agent loop) | Yes | Cursor-hosted; billed on usage / `CURSOR_API_KEY` |
| Sandboxed coding VMs | Yes | Cloud Agents / SDK `cloud: { repos: [...] }` |
| **GPU training API** (fine-tune Grok / your weights on Cursor GPUs) | **No** | Not a Cursor product |
| Serve your fine-tuned model as Cursor’s default brain | **No** | Wire your endpoint; Cursor stays orchestrator |

## Why Task/subagents “don’t run”

1. Parent must be in **Agent** mode (not Ask) for Task spawn.
2. Open folder must be **`D:\Netie Space`** (not the stale C: copy).
3. Plan/model must allow the subagent model; Max Mode / team policy can block.
4. `subagentStart` hooks that `deny` (or invalid `ask`) kill spawn.
5. Cloud subagents need GitHub/GitLab + paid plan — they do **not** see local `D:\` unless the repo is pushed.
6. Git `safe.directory` for `D:/Netie Space` if “dubious ownership” breaks Bash/git tools.
7. Subagents often have **no .NET SDK** in their sandbox — they write code; **this machine builds**.

## Internal mission path (training + inference)

1. **Train** on your GPU cloud (RunPod / Modal / Together / on-prem) — not Cursor.
2. **Serve** with vLLM/TGI/etc. behind your API gateway.
3. **Optional:** Cursor Agent calls your gateway via MCP/HTTP tool for mission inference while coding.
4. **Coding agents only:** create a key at [cursor.com/dashboard/api](https://cursor.com/dashboard/api) → `CURSOR_API_KEY`.

```bash
# Example: programmatic coding agent (NOT model training)
npm install @cursor/sdk
# Agent.create({ apiKey: process.env.CURSOR_API_KEY, local: { cwd: "D:\\Netie Space" } })
```

Do not plan on Cursor granting “GPU access for Grok training.” Use Cursor for agents; use your cloud for weights.
