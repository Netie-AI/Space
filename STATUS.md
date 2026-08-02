# STATUS

**Hard cap: 60 lines.** Older narrative moves to CHANGELOG.md; it does not accumulate here.

## Now

1. **Open PR #2** (`feat/document-chat-and-cortex-sync`): document chat, OCR/image
   editing, Cortex + OpenVault sync — 61 files committed; security follow-up landed
   (DPAPI-sealed keys, cloud OCR opt-in, credential-scan removed). Not merged.
2. **No CI** — builds not verified on push; last local work needed .NET SDK absent on
   the machine that wrote the security commit.
3. **Naming collision** — "Netie Space" (this desktop doc workspace) vs Cortex "DMS
   Spaces" (ACL sandboxes) still confuses cross-repo docs; not resolved here.

## Next

- Merge or close PR #2 after compile verify on a machine with the .NET SDK.
- Route primary AI path through OpenVault gate (called out in PR #3 review, not in tree).
- Draft PR #1 (demo video renderer) still open since 2026-07-28.

## Later

- Product identity in Netie constitution (document workspace vs warehouse Spaces).
- `docs/LOOP_STATE.md` / `PARKING_LOT.md` loop gates stale since 2026-07-14.
