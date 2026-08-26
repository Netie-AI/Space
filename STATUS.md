# STATUS

**Hard cap: 60 lines.** Older narrative moves to CHANGELOG.md; it does not accumulate here.

## Now

1. **Cursor-origin CI** -- `scripts/ci-cursor.sh` (python contracts + license-api
   `tsc` + ffmpeg smoke). GitHub Actions hosted runners are billing-blocked; ignore them.
2. **Ready PRs #4-#27 landed**. Draft #1 (stale demo path) stays draft.
3. **Convert engine** -- `tools/convert_engine.py` is tracked. C# ConversionService
   and PreviewWindow More menu (watermark / protect / unlock / OCR) call it.
4. **Buy / Stripe** -- Worker `/v1/checkout` + webhook + portal. Live pay needs
   your Stripe secrets (`docs/HUMAN_STRIPE_CHECKOUT.md`).
5. **ComfyUI** -- code-ready local client (`ComfyUiClient`, Image ribbon
   Send to Comfy...). Live GPU still human (`docs/HUMAN_PARKED.md`).

## Next

- Human Windows: Select->Split->Drag->Delete->Fade->Wipe->Export
  (`docs/HUMAN_PILLAR1_TEST.md`). Playwright cannot open this WPF app.
- Human: Stripe secrets + test card, then tick Buy API / Stripe.
- Parked: cert signatures, Comfy GPU, P7 (`docs/HUMAN_PARKED.md`).

## Later

- Product identity in Netie constitution (document workspace vs warehouse Spaces).
- Optional marketing after Pillar 1 user Export stress.
