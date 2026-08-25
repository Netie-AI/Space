"""Space-side Cortex WASM isolate stub (no wasmtime).

Mirrors CortexOS/execution/wasm_isolate.py until Cortex owns the runtime:
deny-by-default imports (no WASI FS/network), pack-policy fuel/memory ceilings,
hard timeout + kill, audit every run. Production default ON; demo may set
CORTEX_WASM_DISABLED=1.

See docs/CORTEX_RUNTIME_BEST_FINAL.md.
"""
from __future__ import annotations

import os
import time
from dataclasses import dataclass, field
from typing import Any

PAGE_SIZE = 65_536
DEFAULT_FUEL = 50_000
DEFAULT_MEMORY_BYTES = 4 * 1024 * 1024
DEFAULT_MEMORY_PAGES = DEFAULT_MEMORY_BYTES // PAGE_SIZE  # 64
DEFAULT_TIMEOUT_MS = 2_000
SCAFFOLD_RESULT = 42

WASI_FS = frozenset(
    {
        "path_open",
        "path_create_directory",
        "path_unlink_file",
        "fd_read",
        "fd_write",
        "fd_seek",
        "fd_close",
    }
)
WASI_NET = frozenset(
    {
        "sock_open",
        "sock_connect",
        "sock_recv",
        "sock_send",
        "sock_shutdown",
    }
)


def is_disabled() -> bool:
    return os.environ.get("CORTEX_WASM_DISABLED", "").strip().lower() in {
        "1",
        "true",
        "yes",
        "on",
    }


@dataclass
class IsolateResult:
    ok: bool
    value: Any = None
    kind: str | None = None  # fuel | oom | missing_export | denied_import | timeout | disabled
    error: str | None = None
    fuel_left: int = 0
    audit: list[dict[str, str]] = field(default_factory=list)


def _audit(audit: list[dict[str, str]], event: str, detail: str) -> None:
    audit.append({"event": event, "detail": detail, "ts": str(time.time())})


def _denied_import(name: str) -> bool:
    bare = name.rsplit(".", 1)[-1]
    ns = name.split(".", 1)[0] if "." in name else ""
    if bare in WASI_FS or bare in WASI_NET:
        return True
    if ns.startswith("wasi"):
        return True
    if name in {"env.fetch", "env.http", "wasi_snapshot_preview1"}:
        return True
    return False


def run_module(
    module: dict[str, Any] | None,
    *,
    export: str = "run",
    fuel: int = DEFAULT_FUEL,
    memory_pages: int = DEFAULT_MEMORY_PAGES,
    requested_pages: int | None = None,
    imports: list[str] | None = None,
    timeout_ms: int = DEFAULT_TIMEOUT_MS,
) -> IsolateResult:
    """Execute a fake module dict. Real wasmtime is Cortex-side; this encodes the gates."""
    audit: list[dict[str, str]] = []
    _audit(audit, "start", f"export={export} fuel={fuel} pages={memory_pages}")

    if is_disabled():
        _audit(audit, "disabled", "CORTEX_WASM_DISABLED")
        return IsolateResult(ok=True, value=None, kind="disabled", fuel_left=fuel, audit=audit)

    for imp in imports or []:
        if _denied_import(imp):
            _audit(audit, "denied_import", imp)
            return IsolateResult(
                ok=False,
                kind="denied_import",
                error=f"deny-by-default import: {imp}",
                fuel_left=fuel,
                audit=audit,
            )

    pages_needed = requested_pages if requested_pages is not None else (module or {}).get(
        "memory_pages", 1
    )
    try:
        pages_needed = int(pages_needed)
    except (TypeError, ValueError):
        pages_needed = 1
    if pages_needed > memory_pages:
        _audit(audit, "oom", f"{pages_needed} > {memory_pages}")
        return IsolateResult(
            ok=False,
            kind="oom",
            error=f"memory pages {pages_needed} exceed ceiling {memory_pages}",
            fuel_left=fuel,
            audit=audit,
        )

    if not module or export not in module:
        _audit(audit, "missing_export", export)
        return IsolateResult(
            ok=False,
            kind="missing_export",
            error=f"missing export: {export}",
            fuel_left=fuel,
            audit=audit,
        )

    ops = module[export]
    if ops == 42 or ops is None:
        _audit(audit, "scaffold", "return 42")
        return IsolateResult(ok=True, value=SCAFFOLD_RESULT, fuel_left=fuel - 1, audit=audit)

    if not isinstance(ops, list):
        ops = ["return"]

    deadline = time.monotonic() + (timeout_ms / 1000.0)
    left = fuel
    last: Any = SCAFFOLD_RESULT
    i = 0
    while True:
        if time.monotonic() > deadline:
            _audit(audit, "timeout", f"killed after {timeout_ms}ms")
            return IsolateResult(
                ok=False,
                kind="timeout",
                error="hard timeout; isolate killed",
                fuel_left=left,
                audit=audit,
            )
        if left <= 0:
            _audit(audit, "fuel", "exhausted")
            return IsolateResult(
                ok=False,
                kind="fuel",
                error="fuel exhausted",
                fuel_left=0,
                audit=audit,
            )
        op = ops[i % len(ops)] if ops else "return"
        left -= 1
        if op == "loop":
            i += 1
            continue
        if op == "sleep":
            time.sleep(min(0.05, max(0.0, timeout_ms / 1000.0) + 0.01))
            i += 1
            continue
        if isinstance(op, int):
            last = op
            _audit(audit, "return", str(last))
            return IsolateResult(ok=True, value=last, fuel_left=left, audit=audit)
        if op in {"return", "i32.const 42"}:
            _audit(audit, "return", str(last))
            return IsolateResult(ok=True, value=last, fuel_left=left, audit=audit)
        i += 1


if __name__ == "__main__":
    r = run_module({"run": 42})
    print(r)
