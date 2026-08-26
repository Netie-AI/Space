# STATUS

**Hard cap: 60 lines.** Older narrative moves to CHANGELOG.md; it does not accumulate here.

## Now

1. **main CI gate** — Windows `dotnet test` + ffmpeg encode + python contracts +
   license-api `tsc` (three jobs; no 4th aggregator — billing killed that runner).
   Merge only when **all** checks on the head SHA succeed (never `gh pr merge --auto`).
2. **Ready PRs #4–#25 landed** after green CI. Draft #1 (stale demo path) stays draft.
3. **Naming collision** — "Netie Space" vs Cortex "DMS Spaces" still unresolved.

## Next

- Human: Select→Split→Drag→Delete→Fade→Export on Windows (Pillar 1 gate).
- Do not start: Buy API, Stripe (until that gate), cert signatures, Comfy, P7.

## Later

- Product identity in Netie constitution (document workspace vs warehouse Spaces).
- Optional marketing after Pillar 1 user Export stress.
