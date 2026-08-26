# Human Stripe Checkout (secrets you hold)

Playwright cannot open Netie Space (WPF `.exe`). Test-mode Product `netie_space`
and Price `price_1U8ckCFV5wcFod2faXiUCsaV` ($9/mo USD) already exist on Stripe
account NETIE. The Worker `[vars]` has that Price id. You still set
`STRIPE_SECRET_KEY` and `STRIPE_WEBHOOK_SECRET` (never commit those).

Tax: do not turn on Stripe Tax / `automatic_tax` until you have an active
registration. Stripe will otherwise collect $0 tax with no error.

## 1. Stripe Dashboard (about 10 minutes)

1. Open https://dashboard.stripe.com (Test mode). Product **Netie Space**
   (`netie_space`) should already be there with Price
   `price_1U8ckCFV5wcFod2faXiUCsaV`. Do not put a second plan on that Product.
2. Developers -> API keys. Prefer a restricted key (`rk_test_...`) that can
   create Checkout Sessions and read subscriptions. Do not commit the key.
3. Developers -> Webhooks -> Add endpoint:
   `https://<your-worker>/v1/webhooks/stripe`
   Events: `checkout.session.completed`, `customer.subscription.updated`,
   `customer.subscription.deleted`.
4. Copy the webhook signing secret (`whsec_...`).

Login, Setup Buy, and Activate share one host: `NETIE_LICENSE_API` (wins) or
`appsettings` `License.ApiBaseUrl`. Do not point those at different Workers.

Local test instead of a public Worker:

```
stripe listen --forward-to http://127.0.0.1:8787/v1/webhooks/stripe
```

Use the `whsec_...` that `stripe listen` prints.

## 2. Cloudflare Worker secrets

If `license-api/wrangler.toml` still says `database_id = "REPLACE_AFTER_D1_CREATE"`,
do not run `npx wrangler deploy`. That placeholder is not a D1 id. Wrangler will
reject it or bind a new empty database. Run `.\scripts\setup-license-api.ps1 -Deploy`
first (creates D1 and writes the id). After the id exists:

```
cd license-api
npx wrangler secret put STRIPE_SECRET_KEY
npx wrangler secret put STRIPE_PRICE_ID
npx wrangler secret put STRIPE_WEBHOOK_SECRET
npx wrangler deploy
```

Or the same one-prompt path: `.\scripts\setup-license-api.ps1 -Deploy` (Enter skips a secret).
Test Price is already in Worker `[vars]`; you still set the two secrets.

Also keep the existing Resend / JWT / DEVICE_PEPPER secrets from
`docs/LICENSE_SETUP.md`.

## 3. App click path (Windows)

1. `install.bat` / `run.bat` -> `dist\NetieSpace\NetieSpace.exe`.
2. Tray -> Account / Sign in. Register if you have no account. Register does
   **not** unlock the app when Stripe secrets are live (`pending` until pay).
   Sign-in auto-activate then shows **Get Netie subscription** + **Activate
   this laptop** on the login gate (so `SkipGate: false` is not a deadlock).
3. Login gate or Setup -> Get Netie subscription.
4. Browser opens Stripe Checkout. Pay with test card `4242 4242 4242 4242`.
5. You land on `/v1/checkout/success` on the same Worker host the app called
   (local `:8787` or your `workers.dev`), not the committed `PUBLIC_BASE_URL`.
   The Worker retrieves that session and fulfills the license even if the
   webhook is a few seconds late.
6. Login / Setup -> **Activate this laptop**. Needs a session from step 2.
   Success page already fulfilled the license. Activate without a Stripe
   session id returns `payment_required`.
7. Setup -> **Manage billing** opens the Stripe Customer Portal (needs a
   customer id from step 4). If you have not paid, the button says so.

If secrets are missing, the button shows `stripe_not_configured` and still
opens netie.space. That is expected until step 2 is done.

## 4. What to check

- Setup button opens a `checkout.stripe.com` URL, not only netie.space.
- Stripe Dashboard -> Payments shows the test charge.
- Worker logs / D1: `users.stripe_customer_id` and
  `licenses.stripe_checkout_session_id` are set. License `status` is `active`.
- Buyer email (Resend) has the license key. Password is never in that mail.
- Cancel at Checkout lands on `/v1/checkout/cancel`. License stays unchanged.
- A second pay with the same session id does not create a second license.
- A second **Get Netie subscription** click after a paid active license
  returns `already_paid` (409). Do not checkout again -- Activate.
  Expired licenses can checkout again and reuse the Stripe customer id.
- After a customer id exists, `POST /v1/billing-portal` (signed in) returns a
  `billing.stripe.com` URL. After you leave the portal you land on
  `/v1/billing/return` ("Billing updated", not "Payment received").
  Cancel / update card there, then license status follows
  `customer.subscription.updated` / `deleted`.
- If the webhook wrote the session before customer/subscription IDs were
  parseable (string or `{ id }`), the success page retrieve backfills them.
  Manage billing should still open.
- After Activate, preview / edit works offline (`SkipGate` can stay true on
  your own PC while you test).

## 5. What this agent already proved

- `license-api` typecheck includes `/v1/checkout`, `/v1/checkout/success` fulfill,
  `/v1/stripe/status`, and `/v1/webhooks/stripe`.
- Missing secrets return 503 `stripe_not_configured` (no live charge).
- Setup button calls `LicenseApiClient.CreateCheckoutAsync`.
- Live pay, webhook, and WPF clicks: only you on Windows + Stripe Dashboard.
