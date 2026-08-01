# Claude Code handoff — OpenVault secrets / cards custody

**Date:** 2026-07-26 (updated 2026-07-27)  
**Assign to:** Claude Code (hard / security-heavy)  
**Canonical trees:** `D:\OpenVault` (custody SoT) · `D:\Netie Space` (thin client)  
**Contract:** `D:\OpenVault\PRODUCT_ROLES.md` — Keys SoT = OpenVault; no second vault in Netie.

> **Status 2026-07-27: OpenVault side delivered** — see `D:\OpenVault\docs\SECRETS_CUSTODY.md`.  
> Remaining Netie-side follow-up (optional, not this UI pass): DPAPI-wrap `user.env` offline AI key cache (today plaintext; password vault / license already DPAPI).

---

## Already done in Netie Space (Cursor)

- Setup UI masks keys (`first4…last4`); delete + add/replace only — no inline edit of existing secrets.
- `OpenVaultKeySync` wired: Setup “Connect / sync keys” + soft sync on app startup.
- Local DPAPI password vault remains **offline fallback only**, labeled as such.
- Look Up / Cortex thinking / Enter·Ctrl+Enter / History / auto-pin Look Ups — shipped in Netie UI.
- Apple Design motion pass: `AppleMotion` helper, press-scale buttons, interruptible chat sheet, reduced-motion / reduced-transparency, PDF toolbar `:active` scale.

---

## OpenVault acceptance (Claude — verify / close)

- [x] Password + card schema / custody docs in OpenVault  
- [x] Gated reveal + audit (see SECRETS_CUSTODY.md)  
- [ ] Netie `user.env` DPAPI (optional hardening — separate ticket)  
- [x] PRODUCT_ROLES / STATUS updated in OpenVault tree
