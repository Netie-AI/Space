# Lane checks

`scripts/check-app-shell.ps1` dot-sources every `*.ps1` in this folder after its own checks,
on Windows, against the built `NetieSpace.exe`. A lane adds one file here instead of editing
the main script. Inside a lane file you can use:

- `Invoke-App @(args) [timeoutSec]` -> `@{ Code; Out; Err }`
- `Invoke-Tool "<tool-id>" @(inputs) @("--opt","k=v")` (asserts exit 0, ok:true, a non-empty output)
- `Check "<name>" <bool> "<detail>"` (a false value fails the whole run)
- `$Exe`, `$Out` (scratch folder, uploaded as the CI artifact), `$root`, `$pdfIn`, `$icon`

Write outputs under `$Out` so they land in the artifact a person can open.
