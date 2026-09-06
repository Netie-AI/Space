# STATUS

**Hard cap: 60 lines.** Older narrative moves to CHANGELOG.md; it does not accumulate here.

## Now

1. **PDFSpace workbench (Edit in app)** -- keep Netie Space identity. Top-right
   **Edit in app** = Fill & Sign (fill+sign panels) / Edit PDF / Blank / Combine
   right rail. AcroForm widgets live and aligned to page zoom. Local DPAPI
   Autofill. Serialized PDF save-then-close. Explorer F2 Space types a space.
   Zoom % remembered. Open-failed PDF -> Open in Netie Space (not Acrobat).
2. **Open with Netie Space** -- additive HKCU right-click + Start Menu. Does not
   steal Word/Excel/PDF defaults. `scripts/register-open-with.ps1` from publish.
3. **Native hot paths (not a rewrite)** -- Rust `native/netie-pdf-detect` snap-cell
   (JS `detectCellAt` fallback). Go `native/netie-pdf-form` pdfcpu form-list/fill
   (`tools/netie-pdf-form/` when built). P7 whole-app rewrite stays parked.
4. **Stripe pay-first** -- Product `netie_space` / Price `price_1U8ckCFV5wcFod2faXiUCsaV`.
   Register `pending` until pay. Worker miss is `worker_unreachable`. You still
   put secrets.
5. **Cert sign** -- xunit `PdfClassicXref`. UI `LoadPfx` EphemeralKeySet then
   PersistKeySet. Adobe Reader check still human.

## Next

- Human: reopen EP FORM after this build -- widgets should sit on the printed labels at zoom. Gear or My details for local profile. Autofill only matched types.
- Human Windows Export (`docs/HUMAN_PILLAR1_TEST.md`). Stripe `4242`. Adobe on cert PDF.
- Parked: Comfy GPU, P7 rewrite (`docs/HUMAN_PARKED.md`). Product rename later. Hex editors for binaries. Object memory (ledger 22-24): typed cards rejected; Cortex `/api/memory` for facts; OpenVault for keys only; Space#3 before any AI parse. Chatspace fill+confirm (ledger 25-26): NEEDS-YOU sign; then blocked on Space#3.
