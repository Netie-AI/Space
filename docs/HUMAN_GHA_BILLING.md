# GitHub Actions billing lock (human)

**Status 2026-11-20: unlocked.** Hosted runners start and run steps again
(Actions run `43334963084` on 2026-11-13 ran the Windows xunit job: 964 passed). The lock
described below is the 2026-08 state; keep it for the day it returns. The merge
order list is historical: those draft PRs were never opened as a stack and the
stack merged as one commit range (`claude/s50-legacy-stack`).

Hosted runners on this **private** repo do not start. Every CI job dies in
2-4s with **zero steps** and empty `runner_name`. That is not a test fail.

Job annotation (copied from run `33046131550`, 2026-08-27):

> The job was not started because recent account payments have failed or
> your spending limit needs to be increased. Please check the 'Billing &
> plans' section in your settings

Last green hosted run before the lock: `2f4b64b` (all jobs SUCCESS).
See `docs/HUMAN_PILLAR1_TEST.md`.

## Unlock (org owner only)

This agent token cannot read billing (`403`), register a self-hosted
runner (`403`), or change org Actions permissions (`403`). A workflow
edit cannot assign a runner.

1. Open https://github.com/organizations/Netie-AI/settings/billing
2. Fix failed payments and/or raise the Actions spending limit above `$0`.
3. Re-run the latest `CI` workflow on `main` (Actions -> CI -> Re-run jobs).
4. Confirm `build (windows)`, `python syntax`, and `license-api` have
   real steps and a `runner_name` (not 2s empty jobs). Path filters:
   leftover dest PRs start only the python workflow; Windows is
   `src/` / xunit / wipe ps1; license-api is `license-api/`. (Not adopted in
   this tree: all three jobs live in `ci.yml` without path filters. See
   PARKING_LOT.md, CI.)

## Merge rule

Do **not** merge a PR while GitHub Actions `CI` is red. Cursor-origin
`./scripts/ci-cursor.sh` still has to PASS. After unlock, both gates
must be green.

`scripts/gha-billing-check.sh` prints the latest job annotation and
exits `1` while this lock is on.

## After unlock (merge order)

Draft leftover dest PRs wait on hosted jobs with real steps + PASS.
Do not undraft until then. Merge this stack in order (each is based on
the previous):

1. #281 CI path-split + billing playbook
2. #282 leftover RTF `fldinst`
3. #283 leftover RTF `userprops`
4. #284 leftover RTF `xmlopen`
5. #285 leftover RTF `bkmk`
6. #286 leftover `mso-element:comment`
7. #287 leftover `<a class=msocomoff>`
8. #288 nested `aml:annotation`
9. #289 leftover `CustomDocumentProperties`
10. #290 leftover `DocumentProperties` / `OfficeDocumentSettings`
11. #291 leftover Word HTML settings (`w:View` / `w:BrowserLevel`)
12. #292 leftover WordML `pStyle=CommentText`
13. #293 leftover Word `LidThemeOther` / `LidThemeAsian`
14. #294 leftover Word `docVar` / `docVars`
15. #295 leftover Word `mso-deletion` / `msoChangeDel`
16. #296 leftover Word `CharacterSpacingControl` / `ValidateAgainstSchemas`
17. #297 leftover Word `mso-comment-continuation`
18. #298 leftover Word `mso-style-name:"Comment Text"`

Draft #1 (demo video) stays draft. Parked leftovers stay parked
(headers / footnotes / smart tags / `w:sdt` / xml islands outside
`<head>` / RTF `\\fldrslt` / `\\header` / `\\upr`).
