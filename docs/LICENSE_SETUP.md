# License API + installer setup

## What you create once (cannot fully automate without your accounts)

| Secret / account | Where | Used for |
|------------------|-------|----------|
| Cloudflare account | https://dash.cloudflare.com | Workers + D1 (+ R2 later) |
| `wrangler login` | terminal | Deploy API |
| Resend API key | https://resend.com | Owner + buyer emails |
| Verified From address | Resend Domains | `RESEND_FROM_EMAIL` |
| Owner inbox | your email | Activation / login alerts |
| Random `JWT_SECRET` | generate | Session + entitlement signing |
| `DEVICE_PEPPER` | same string in Worker secret + `appsettings.json` `License.DevicePepper` | Device bind |

**Never emailed:** passwords. Owner mail is event metadata only (email, license, device prefix, IP, time).

## One-command setup

```powershell
cd "D:\Netie Space"
.\scripts\setup-license-api.ps1          # npm install + D1 create + local migrate
.\scripts\setup-license-api.ps1 -LocalOnly   # wrangler dev on :8787
.\scripts\setup-license-api.ps1 -Deploy      # remote migrate + secrets + deploy
```

Then set in [`src/NetieSpace/appsettings.json`](../src/NetieSpace/appsettings.json):

```json
"License": {
  "ApiBaseUrl": "https://netie-space-license.<you>.workers.dev",
  "DevicePepper": "<same as DEVICE_PEPPER secret>",
  "SkipGate": false
}
```

Local Worker: `"ApiBaseUrl": "http://127.0.0.1:8787"`

## API surface (Stripe-ready)

- `POST /v1/register` — email + password → user + license key + Resend
- `POST /v1/login`
- `POST /v1/activate` — Bearer + `device_hash` → one laptop bind + entitlement token
- `POST /v1/download` — one-shot download token (burns on GET)
- `GET /v1/me`
- `POST /v1/checkout` — Stripe Checkout Session (`mode=subscription`). 503 `stripe_not_configured` until secrets are set. Sign in first.
- `POST /v1/webhooks/stripe` — verifies `Stripe-Signature`; fulfills `checkout.session.completed`
- `POST /v1/billing-portal` — Stripe Customer Portal (Bearer + existing `stripe_customer_id`)
- `GET /v1/checkout/success` — retrieves `session_id` and fulfills if paid (webhook backup)
- `GET /v1/checkout/cancel`
- `GET /v1/stripe/status` — `{stripe_configured, webhook_configured, price_id_set}` (no secrets)
- Setup **Activate this laptop** — `POST /v1/activate` with the saved session (after pay)
- D1 columns `stripe_customer_id`, `stripe_subscription_id`, `stripe_checkout_session_id` written by the webhook
- Human secrets + click path: [`docs/HUMAN_STRIPE_CHECKOUT.md`](HUMAN_STRIPE_CHECKOUT.md)

Password hashing: **PBKDF2-SHA256** (310k) via Web Crypto — Workers-native. Field `password_algo` allows future Argon2id without schema break.

## Offline behavior

After activate, entitlement is DPAPI-sealed under `%LOCALAPPDATA%\NetieSpace\license\`. Preview / Edit / PDF / video work offline. **AI** and **sign-in / rebind** need network.

## Installer (Inno)

1. `.\scripts\publish.ps1` (no `.env` by default — use `-IncludeEnv` only for your machine)
2. Install [Inno Setup](https://jrsoftware.org/isinfo.php)
3. Compile `installer\NetieSpace.iss` → `dist\installer\NetieSpace-Setup.exe`
4. Upload to R2 as object key `NetieSpace-Setup.exe` (enable R2 binding in `wrangler.toml`)

## Production checklist

- [ ] `SkipGate: false`
- [ ] Resend secrets set
- [ ] Authenticode-sign `NetieSpace-Setup.exe` (optional but recommended)
- [ ] Do not ship `.env` with AI keys to customers
- [ ] Stripe secrets (`STRIPE_SECRET_KEY`, `STRIPE_PRICE_ID`, `STRIPE_WEBHOOK_SECRET`) + live test pay (`docs/HUMAN_STRIPE_CHECKOUT.md`)
- [ ] Do not enable Stripe Tax / `automatic_tax` until you have an active registration
