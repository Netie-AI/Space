# STATUS

**Hard cap: 60 lines.** Older narrative moves to CHANGELOG.md; it does not accumulate here.

## Now

1. **main CI is red** — Windows `dotnet build` failed CS0104 (`Block` WPF vs Markdig) in
   `ChatMarkdownRenderer.cs`. #4 merged before that check finished. Hotfix in flight.
2. **Stacked tickets** — #5 chat modes, #6 PDF highlights, #7 video wipe. Same CS0104 until
   hotfix lands; then rebase.
3. **Naming collision** — "Netie Space" vs Cortex "DMS Spaces" still unresolved.

## Next

- Merge the CS0104 hotfix only after Windows CI is green.
- Rebase #5/#6/#7 onto that fix; land only if green. #6 and #7 conflict on PreviewWindow.

## Later

- Product identity in Netie constitution (document workspace vs warehouse Spaces).
- `docs/LOOP_STATE.md` / `PARKING_LOT.md` loop gates stale since 2026-07-14.
