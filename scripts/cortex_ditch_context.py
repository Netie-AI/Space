"""Space-side Cortex sidecar ditch tiers (SWA / CSA / HCA).

Mirrors src/NetieSpace/Services/DitchContext.cs so Cortex/AirGPT can share the
contract without merging codebases. Never ditches system or the last user turn;
never exceeds budget_chars.
"""
from __future__ import annotations

from typing import Any

DEFAULT_BUDGET_CHARS = 12_000
SWA_KEEP = 6


def _content(msg: dict[str, Any]) -> str:
    return str(msg.get("content") or "")


def _role(msg: dict[str, Any]) -> str:
    return str(msg.get("role") or "")


def _trim(s: str, n: int) -> str:
    s = (s or "").replace("\n", " ").strip()
    return s if len(s) <= n else s[:n] + "…"


def _truncate(s: str, n: int) -> str:
    s = s or ""
    return s if len(s) <= n else s[: n - 1] + "…"


def _total_chars(msgs: list[dict[str, Any]]) -> int:
    return sum(len(_content(m)) for m in msgs)


def apply(messages: list[dict[str, Any]], budget_chars: int = DEFAULT_BUDGET_CHARS) -> list[dict[str, Any]]:
    if not messages:
        return []

    system = [m for m in messages if _role(m) == "system"]
    rest = [m for m in messages if _role(m) != "system"]
    if not rest:
        return list(system)

    last_user_idx = max((i for i, m in enumerate(rest) if _role(m) == "user"), default=len(rest) - 1)
    swa_start = max(0, len(rest) - SWA_KEEP)
    swa_start = min(swa_start, last_user_idx)
    swa = rest[swa_start:]
    older = rest[:swa_start]

    result: list[dict[str, Any]] = list(system)
    if older:
        pins = [
            _trim(_content(m), 200)
            for m in older
            if _role(m) == "note" or "remember" in _content(m).lower()
        ][:4]
        bits = [
            f"{_role(m)}: {_trim(_content(m), 120)}"
            for m in older
            if _role(m) in {"user", "assistant", "note"}
        ][-8:]
        digest = " · ".join(bits)
        hca = "[Earlier conversation digest]\n" + digest
        for p in pins:
            hca += "\n• pin: " + p
        result.append({"role": "system", "content": hca.strip()})

    for m in swa:
        text = _content(m)
        if len(text) > 2_500 and "Attached context" in text:
            result.append({"role": _role(m), "content": _truncate(text, 1_200)})
        elif len(text) > 4_000:
            result.append({"role": _role(m), "content": _truncate(text, 2_000)})
        else:
            result.append(m)

    while _total_chars(result) > budget_chars and len(result) > 2:
        idx = next(
            (
                i
                for i, m in enumerate(result)
                if _role(m) != "system" or _content(m).startswith("[Earlier")
            ),
            -1,
        )
        if idx < 0 or idx == len(result) - 1:
            break
        cur = _content(result[idx])
        if len(cur) > 400:
            result[idx] = {"role": _role(result[idx]), "content": _truncate(cur, 400)}
        else:
            result.pop(idx)

    return result
