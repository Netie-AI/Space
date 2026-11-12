---
status: proposed
date: 2026-09-18
decision-makers: founder
---

# DR-0002 - Engine runtime and licence policy

## Context and Problem Statement

The PDF tools were first written on PyMuPDF, which is AGPL-3.0. Nine PDF write paths depend on
it (PRD ledger row 50). A package that ships PyMuPDF, or lets a user reach it over a network
service, carries AGPL obligations that Netie Space cannot accept for a closed product. The default
`scripts/publish.ps1` ships no Python, but `-IncludeVenv` copies the repo `.venv` into the package,
and that venv holds `pymupdf`, `pymupdf4llm` and `fitz`. Nothing stopped it, and nothing recorded
the rule, so every engine ticket would have to argue it again.

FFmpeg is the opposite case: the shipped build is GPLv3 but is run as a separate process and is
distributed with a notice and source links, which the GPL permits.

## Considered Options

- A. Buy a commercial PyMuPDF licence. A founder spend decision; not assumed here.
- B. Re-engine the PDF paths on PdfSharpCore, PdfPig, Windows.Data.Pdf and, where a Python
  library is unavoidable, pikepdf (MPL-2.0).
- C. Ship a Python runtime inside the package with wheels licensed MIT, BSD, Apache, MPL or LGPL only.
- D. Drop the tools that have no permissive engine.

## Decision Outcome

B first, C only where B has no answer, D for the rest. A is not taken unless the founder buys it.

1. C# in-process first (PdfSharpCore MIT, PdfPig Apache-2.0, ImageSharp 1.0.4 Apache-2.0,
   Windows.Data.Pdf for rendering).
2. Python only as a runtime shipped inside the package, with wheels licensed MIT, BSD, Apache,
   MPL or LGPL. A tool whose engine is missing reports itself unavailable with its real reason.
   It never falls back to a banned engine.
3. FFmpeg stays GPLv3, out of process, with `THIRD-PARTY-NOTICES.txt` in the package.
4. Never shipped: PyMuPDF, pymupdf4llm, Ghostscript, OnlyOffice.
5. The gate is the enforcer: `scripts/check-tool-engines.ps1` runs on the final package folder
   inside `scripts/publish.ps1`. It has no override switch. A dev build that needs PyMuPDF runs
   from the repo, where the converter finds the repo `.venv`; that is not a package.

## Consequences

Positive: the policy is one file an agent can read before proposing an engine. A banned engine
cannot reach `dist\NetieSpace`, `package-release.ps1` or the installer, because all three consume
that folder.

Negative: `publish.ps1 -IncludeVenv` with the repo venv now aborts at the gate naming pymupdf. That
is by design. The Python-backed PDF paths need re-engineering (S50-E3), and until then the
catalog shows honest "unavailable" rows instead of working ones.

## Confirmation

- `scripts/check-tool-engines.ps1 -SelfTest` plants one marker per banned engine and proves the scan
  fails naming all four, then proves a clean folder (with a MuPDF-written decoy PDF) passes.
- `tests/test_tool_engines_gate.py` pins the script's engine table, its non-zero exit, the absence of
  an override switch, the call from `scripts/publish.ps1` between the `.venv` copy and the launcher,
  and runs `-SelfTest` through `pwsh` or `powershell` when one is present.
