"""Contract tests for Cortex sidecar ditch tiers + WASM isolate stubs."""
from __future__ import annotations

import os
import sys
import time
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))

import cortex_ditch_context as ditch  # noqa: E402
import cortex_wasm_isolate as wasm  # noqa: E402

POLICY_CS = ROOT / "src/NetieSpace/Services/CortexWasmPolicy.cs"
CLIENT_CS = ROOT / "src/NetieSpace/Services/CortexClient.cs"
DITCH_CS = ROOT / "src/NetieSpace/Services/DitchContext.cs"
PARKING = ROOT / "PARKING_LOT.md"


class TestWasmIsolate(unittest.TestCase):
    def setUp(self):
        self._prev = os.environ.get("CORTEX_WASM_DISABLED")
        os.environ.pop("CORTEX_WASM_DISABLED", None)

    def tearDown(self):
        if self._prev is None:
            os.environ.pop("CORTEX_WASM_DISABLED", None)
        else:
            os.environ["CORTEX_WASM_DISABLED"] = self._prev

    def test_scaffold_returns_42(self):
        r = wasm.run_module({"run": 42})
        self.assertTrue(r.ok)
        self.assertEqual(r.value, 42)
        self.assertTrue(any(a["event"] == "start" for a in r.audit))

    def test_fuel_exhaust(self):
        r = wasm.run_module({"run": ["loop"]}, fuel=8)
        self.assertFalse(r.ok)
        self.assertEqual(r.kind, "fuel")
        self.assertEqual(r.fuel_left, 0)

    def test_oom_pages(self):
        r = wasm.run_module({"run": 42, "memory_pages": 128}, memory_pages=wasm.DEFAULT_MEMORY_PAGES)
        self.assertFalse(r.ok)
        self.assertEqual(r.kind, "oom")

    def test_missing_export(self):
        r = wasm.run_module({"other": 42}, export="run")
        self.assertFalse(r.ok)
        self.assertEqual(r.kind, "missing_export")

    def test_deny_wasi_fs_and_net(self):
        fs = wasm.run_module({"run": 42}, imports=["wasi_snapshot_preview1.path_open"])
        self.assertFalse(fs.ok)
        self.assertEqual(fs.kind, "denied_import")
        net = wasm.run_module({"run": 42}, imports=["wasi_snapshot_preview1.sock_open"])
        self.assertFalse(net.ok)
        self.assertEqual(net.kind, "denied_import")

    def test_hard_timeout_kills(self):
        start = time.monotonic()
        r = wasm.run_module({"run": ["sleep", "loop"]}, timeout_ms=30)
        elapsed_ms = (time.monotonic() - start) * 1000
        self.assertFalse(r.ok)
        self.assertEqual(r.kind, "timeout")
        self.assertLess(elapsed_ms, 1500)

    def test_disabled_via_env(self):
        os.environ["CORTEX_WASM_DISABLED"] = "1"
        r = wasm.run_module({"run": ["loop"]}, fuel=1)
        self.assertTrue(r.ok)
        self.assertEqual(r.kind, "disabled")
        self.assertIsNone(r.value)


class TestDitchSidecar(unittest.TestCase):
    def test_never_ditch_system_or_last_user(self):
        msgs = [
            {"role": "system", "content": "You are Space."},
            {"role": "user", "content": "old question " + ("x" * 200)},
            {"role": "assistant", "content": "old answer " + ("y" * 200)},
        ] * 8
        msgs.append({"role": "user", "content": "LAST USER TURN unique-token-zz9"})
        out = ditch.apply(msgs, budget_chars=800)
        joined = " ".join(m["content"] for m in out)
        self.assertTrue(any(_role_is(m, "system") and "You are Space." in m["content"] for m in out))
        self.assertIn("LAST USER TURN unique-token-zz9", joined)
        last_user = [m for m in out if m.get("role") == "user"][-1]
        self.assertIn("LAST USER TURN unique-token-zz9", last_user["content"])

    def test_budget_never_exceeded(self):
        msgs = [{"role": "system", "content": "sys"}]
        for i in range(40):
            msgs.append({"role": "user", "content": f"u{i} " + ("a" * 400)})
            msgs.append({"role": "assistant", "content": f"a{i} " + ("b" * 400)})
        msgs.append({"role": "user", "content": "final"})
        out = ditch.apply(msgs, budget_chars=2_000)
        total = sum(len(m.get("content") or "") for m in out)
        self.assertLessEqual(total, 2_000)
        self.assertEqual(out[-1]["content"], "final")


def _role_is(m, role):
    return m.get("role") == role


class TestCsContract(unittest.TestCase):
    def test_policy_constants_match_python(self):
        cs = POLICY_CS.read_text(encoding="utf-8")
        self.assertIn("public const int FuelLimit = 50_000;", cs)
        self.assertIn("public const int MemoryBytes = 4 * 1024 * 1024;", cs)
        self.assertIn("public const int TimeoutMs = 2_000;", cs)
        self.assertIn("CORTEX_WASM_DISABLED", cs)
        self.assertEqual(wasm.DEFAULT_FUEL, 50_000)
        self.assertEqual(wasm.DEFAULT_MEMORY_BYTES, 4 * 1024 * 1024)
        self.assertEqual(wasm.DEFAULT_MEMORY_PAGES, 64)

    def test_client_sends_wasm_policy(self):
        cs = CLIENT_CS.read_text(encoding="utf-8")
        self.assertIn("CortexWasmPolicy.ToPayload()", cs)
        self.assertIn('["wasm_policy"]', cs)
        self.assertIn("DitchContext.DefaultBudgetChars", cs)

    def test_ditch_cs_invariants(self):
        cs = DITCH_CS.read_text(encoding="utf-8")
        self.assertIn("Never ditch system or the last user message.", cs)
        self.assertIn("public const int DefaultBudgetChars = 12_000;", cs)
        self.assertEqual(ditch.DEFAULT_BUDGET_CHARS, 12_000)

    def test_parking_lot_checked(self):
        text = PARKING.read_text(encoding="utf-8")
        self.assertIn("- [x] Cortex sidecar ditch tiers + WASM stubs (parallel track)", text)


if __name__ == "__main__":
    unittest.main()
