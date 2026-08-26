import type { Env } from "./types";
import {
  createSessionToken,
  hashPassword,
  hmacSign,
  normalizeEmail,
  randomId,
  randomLicenseKey,
  sha256Hex,
  verifyPassword,
  verifySessionToken,
} from "./crypto";
import { emailBuyer, notifyOwner } from "./email";
import {
  checkoutSessionPaid,
  createCheckoutSession,
  createPortalSession,
  requestPublicBase,
  fulfillCheckoutSession,
  markSubscriptionStatus,
  retrieveCheckoutSession,
  stripeConfigured,
  stripeRefId,
  stripeWebhookConfigured,
  verifyStripeSignature,
} from "./stripe";

function corsHeaders(env: Env, req: Request): HeadersInit {
  const origin = req.headers.get("Origin") ?? "*";
  const allowed = (env.CORS_ORIGINS || "")
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean);
  const allow =
    allowed.length === 0 || allowed.includes(origin) ? origin || "*" : allowed[0];
  return {
    "Access-Control-Allow-Origin": allow,
    "Access-Control-Allow-Methods": "GET,POST,OPTIONS",
    "Access-Control-Allow-Headers": "Content-Type, Authorization",
    "Access-Control-Max-Age": "86400",
  };
}

function html(env: Env, req: Request, message: string): Response {
  const safe = message.replace(/[<>&]/g, "");
  return new Response(
    `<!doctype html><meta charset="utf-8"><title>${env.APP_NAME}</title><p>${safe}</p>`,
    {
      headers: { "Content-Type": "text/html; charset=utf-8", ...corsHeaders(env, req) },
    }
  );
}

function json(env: Env, req: Request, body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      "Content-Type": "application/json",
      ...corsHeaders(env, req),
    },
  });
}

async function readJson(req: Request): Promise<Record<string, unknown>> {
  try {
    return (await req.json()) as Record<string, unknown>;
  } catch {
    return {};
  }
}

function clientIp(req: Request): string {
  return req.headers.get("CF-Connecting-IP") ?? req.headers.get("X-Forwarded-For") ?? "unknown";
}

async function requireUser(env: Env, req: Request): Promise<{ userId: string } | Response> {
  const auth = req.headers.get("Authorization") ?? "";
  const token = auth.startsWith("Bearer ") ? auth.slice(7).trim() : "";
  if (!token || !env.JWT_SECRET) {
    return json(env, req, { error: "unauthorized" }, 401);
  }
  const session = await verifySessionToken(env.JWT_SECRET, token);
  if (!session) return json(env, req, { error: "unauthorized" }, 401);

  const row = await env.DB.prepare(
    "SELECT id FROM sessions WHERE token_hash = ? AND expires_at > ?"
  )
    .bind(await sha256Hex(token), new Date().toISOString())
    .first<{ id: string }>();
  if (!row) return json(env, req, { error: "session_expired" }, 401);
  return session;
}

export default {
  async fetch(req: Request, env: Env): Promise<Response> {
    const url = new URL(req.url);
    if (req.method === "OPTIONS") {
      return new Response(null, { status: 204, headers: corsHeaders(env, req) });
    }

    try {
      if (url.pathname === "/health" && req.method === "GET") {
        return json(env, req, { ok: true, app: env.APP_NAME });
      }

      if (url.pathname === "/v1/register" && req.method === "POST") {
        return await handleRegister(req, env);
      }
      if (url.pathname === "/v1/login" && req.method === "POST") {
        return await handleLogin(req, env);
      }
      if (url.pathname === "/v1/activate" && req.method === "POST") {
        return await handleActivate(req, env);
      }
      if (url.pathname === "/v1/download" && req.method === "POST") {
        return await handleDownload(req, env);
      }
      if (url.pathname === "/v1/me" && req.method === "GET") {
        return await handleMe(req, env);
      }
      if (url.pathname === "/v1/checkout" && req.method === "POST") {
        return await handleCheckout(req, env);
      }
      if (url.pathname === "/v1/billing-portal" && req.method === "POST") {
        return await handleBillingPortal(req, env);
      }
      if (url.pathname === "/v1/webhooks/stripe" && req.method === "POST") {
        return await handleStripeWebhook(req, env);
      }
      if (url.pathname === "/v1/stripe/status" && req.method === "GET") {
        return json(env, req, {
          stripe_configured: stripeConfigured(env),
          webhook_configured: stripeWebhookConfigured(env),
          price_id_set: Boolean(env.STRIPE_PRICE_ID),
        });
      }
      if (url.pathname === "/v1/checkout/success" && req.method === "GET") {
        return await handleCheckoutSuccess(req, env, url);
      }
      if (url.pathname === "/v1/billing/return" && req.method === "GET") {
        return html(
          env,
          req,
          "Billing updated. Return to Netie Space. This was not a new charge."
        );
      }
      if (url.pathname === "/v1/checkout/cancel" && req.method === "GET") {
        return html(
          env,
          req,
          "Checkout cancelled. Setup -> Get Netie subscription to try again."
        );
      }

      if (url.pathname === "/v1/download/file" && req.method === "GET") {
        return await handleDownloadFile(req, env, url);
      }

      return json(env, req, { error: "not_found" }, 404);
    } catch (err) {
      console.error(err);
      return json(env, req, { error: "server_error", message: String(err) }, 500);
    }
  },
};

async function handleRegister(req: Request, env: Env): Promise<Response> {
  const body = await readJson(req);
  const email = normalizeEmail(String(body.email ?? ""));
  const password = String(body.password ?? "");
  if (!email.includes("@") || password.length < 8) {
    return json(env, req, { error: "invalid_input", message: "Email + password (8+) required" }, 400);
  }

  const existing = await env.DB.prepare("SELECT id FROM users WHERE email = ?")
    .bind(email)
    .first();
  if (existing) return json(env, req, { error: "email_taken" }, 409);

  const userId = randomId("usr");
  const licenseId = randomId("lic");
  const licenseKey = randomLicenseKey();
  const now = new Date().toISOString();
  const passwordHash = await hashPassword(password);
  // Pay-first when Stripe secrets are live. Local/dev Worker without secrets
  // still grants active so SkipGate testers can Activate without a card.
  const licenseStatus = stripeConfigured(env) ? "pending" : "active";

  await env.DB.batch([
    env.DB.prepare(
      `INSERT INTO users (id, email, password_hash, password_algo, email_verified, created_at, stripe_customer_id)
       VALUES (?, ?, ?, 'pbkdf2-sha256', 0, ?, NULL)`
    ).bind(userId, email, passwordHash, now),
    env.DB.prepare(
      `INSERT INTO licenses (id, user_id, license_key, status, max_devices, created_at, stripe_subscription_id, stripe_checkout_session_id)
       VALUES (?, ?, ?, ?, 1, ?, NULL, NULL)`
    ).bind(licenseId, userId, licenseKey, licenseStatus, now),
  ]);

  const { token, expiresAt, tokenHash } = await createSessionToken(env.JWT_SECRET, userId);
  await env.DB.prepare(
    `INSERT INTO sessions (id, user_id, token_hash, expires_at, created_at) VALUES (?, ?, ?, ?, ?)`
  )
    .bind(randomId("sess"), userId, tokenHash, expiresAt, now)
    .run();

  await notifyOwner(env, "new registration", [
    `event: register`,
    `email: ${email}`,
    `license: ${licenseKey}`,
    `ip: ${clientIp(req)}`,
    `ts: ${now}`,
  ]);

  await emailBuyer(
    env,
    email,
    `Welcome to ${env.APP_NAME}`,
    `Your account is ready.\n\nLicense key: ${licenseKey}\n\n${
      stripeConfigured(env)
        ? "Pay first (Get Netie subscription), then Activate this laptop (one device)."
        : "Sign in inside Netie Space, then Activate on this laptop (one device)."
    }\n\nWe never email your password.`
  );

  return json(env, req, {
    token,
    expires_at: expiresAt,
    email,
    license_key: licenseKey,
    license_id: licenseId,
  });
}

async function handleLogin(req: Request, env: Env): Promise<Response> {
  const body = await readJson(req);
  const email = normalizeEmail(String(body.email ?? ""));
  const password = String(body.password ?? "");

  const user = await env.DB.prepare(
    "SELECT id, email, password_hash FROM users WHERE email = ?"
  )
    .bind(email)
    .first<{ id: string; email: string; password_hash: string }>();

  if (!user || !(await verifyPassword(password, user.password_hash))) {
    return json(env, req, { error: "invalid_credentials" }, 401);
  }

  const license = await env.DB.prepare(
    "SELECT id, license_key, status FROM licenses WHERE user_id = ? ORDER BY created_at DESC LIMIT 1"
  )
    .bind(user.id)
    .first<{ id: string; license_key: string; status: string }>();

  const now = new Date().toISOString();
  const { token, expiresAt, tokenHash } = await createSessionToken(env.JWT_SECRET, user.id);
  await env.DB.prepare(
    `INSERT INTO sessions (id, user_id, token_hash, expires_at, created_at) VALUES (?, ?, ?, ?, ?)`
  )
    .bind(randomId("sess"), user.id, tokenHash, expiresAt, now)
    .run();

  await notifyOwner(env, "login", [
    `event: login`,
    `email: ${user.email}`,
    `ip: ${clientIp(req)}`,
    `ts: ${now}`,
  ]);

  return json(env, req, {
    token,
    expires_at: expiresAt,
    email: user.email,
    license_key: license?.license_key ?? null,
    license_id: license?.id ?? null,
    license_status: license?.status ?? null,
  });
}

async function handleActivate(req: Request, env: Env): Promise<Response> {
  const auth = await requireUser(env, req);
  if (auth instanceof Response) return auth;

  const body = await readJson(req);
  const deviceHash = String(body.device_hash ?? "").trim();
  const deviceLabel = String(body.device_label ?? "Windows PC").slice(0, 120);
  const licenseKey = String(body.license_key ?? "").trim().toUpperCase();

  if (deviceHash.length < 16) {
    return json(env, req, { error: "invalid_device" }, 400);
  }

  let license = licenseKey
    ? await env.DB.prepare(
        "SELECT id, user_id, license_key, status, max_devices, stripe_checkout_session_id FROM licenses WHERE license_key = ?"
      )
        .bind(licenseKey)
        .first<{
          id: string;
          user_id: string;
          license_key: string;
          status: string;
          max_devices: number;
          stripe_checkout_session_id: string | null;
        }>()
    : await env.DB.prepare(
        "SELECT id, user_id, license_key, status, max_devices, stripe_checkout_session_id FROM licenses WHERE user_id = ? ORDER BY created_at DESC LIMIT 1"
      )
        .bind(auth.userId)
        .first<{
          id: string;
          user_id: string;
          license_key: string;
          status: string;
          max_devices: number;
          stripe_checkout_session_id: string | null;
        }>();

  if (!license || license.user_id !== auth.userId) {
    return json(env, req, { error: "license_not_found" }, 404);
  }
  if (license.status !== "active") {
    const unpaid = license.status === "pending" || !license.stripe_checkout_session_id;
    return json(
      env,
      req,
      {
        error: unpaid ? "payment_required" : "license_inactive",
        message: unpaid
          ? "Pay first (Get Netie subscription), then Activate this laptop."
          : "License is not active.",
      },
      403
    );
  }
  if (stripeConfigured(env) && !license.stripe_checkout_session_id) {
    return json(
      env,
      req,
      {
        error: "payment_required",
        message: "Pay first (Get Netie subscription), then Activate this laptop.",
      },
      403
    );
  }

  const now = new Date().toISOString();
  const existing = await env.DB.prepare(
    "SELECT id FROM devices WHERE license_id = ? AND device_hash = ?"
  )
    .bind(license.id, deviceHash)
    .first<{ id: string }>();

  if (existing) {
    await env.DB.prepare("UPDATE devices SET last_seen_at = ?, device_label = ? WHERE id = ?")
      .bind(now, deviceLabel, existing.id)
      .run();
  } else {
    const countRow = await env.DB.prepare(
      "SELECT COUNT(*) AS c FROM devices WHERE license_id = ?"
    )
      .bind(license.id)
      .first<{ c: number }>();
    const count = Number(countRow?.c ?? 0);
    if (count >= license.max_devices) {
      return json(
        env,
        req,
        {
          error: "device_limit",
          message: "This license is already bound to another laptop. Contact support to transfer.",
        },
        403
      );
    }

    await env.DB.prepare(
      `INSERT INTO devices (id, license_id, device_hash, device_label, bound_at, last_seen_at)
       VALUES (?, ?, ?, ?, ?, ?)`
    )
      .bind(randomId("dev"), license.id, deviceHash, deviceLabel, now, now)
      .run();
  }

  const user = await env.DB.prepare("SELECT email FROM users WHERE id = ?")
    .bind(auth.userId)
    .first<{ email: string }>();

  await notifyOwner(env, "activate", [
    `event: activate`,
    `email: ${user?.email ?? "?"}`,
    `license: ${license.license_key}`,
    `device: ${deviceHash.slice(0, 12)}…`,
    `label: ${deviceLabel}`,
    `ip: ${clientIp(req)}`,
    `ts: ${now}`,
  ]);

  // Offline entitlement payload (client seals locally)
  const entitlement = {
    email: user?.email ?? "",
    license_key: license.license_key,
    license_id: license.id,
    device_hash: deviceHash,
    activated_at: now,
    offline_ok: true,
  };

  return json(env, req, {
    ok: true,
    entitlement,
    // Signed entitlement for offline verify
    entitlement_token: await signEntitlement(env, entitlement),
  });
}

async function signEntitlement(
  env: Env,
  entitlement: Record<string, unknown>
): Promise<string> {
  const payload = btoa(JSON.stringify(entitlement))
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/, "");
  const sig = await hmacSign(env.JWT_SECRET, payload);
  return `${payload}.${sig}`;
}

async function handleDownload(req: Request, env: Env): Promise<Response> {
  const auth = await requireUser(env, req);
  if (auth instanceof Response) return auth;

  const body = await readJson(req);
  const deviceHash = String(body.device_hash ?? "").trim();

  const license = await env.DB.prepare(
    "SELECT id, license_key FROM licenses WHERE user_id = ? AND status = 'active' ORDER BY created_at DESC LIMIT 1"
  )
    .bind(auth.userId)
    .first<{ id: string; license_key: string }>();
  if (!license) return json(env, req, { error: "no_license" }, 404);

  const device = await env.DB.prepare(
    "SELECT id FROM devices WHERE license_id = ? AND device_hash = ?"
  )
    .bind(license.id, deviceHash)
    .first();
  if (!device) {
    return json(env, req, { error: "activate_first" }, 403);
  }

  const used = await env.DB.prepare(
    "SELECT id FROM download_tokens WHERE license_id = ? AND used_at IS NOT NULL LIMIT 1"
  )
    .bind(license.id)
    .first();
  if (used) {
    return json(env, req, { error: "download_already_used", message: "One download per license." }, 403);
  }

  const now = new Date();
  const expires = new Date(now.getTime() + 5 * 60 * 1000);
  const jti = randomId("dl");
  await env.DB.prepare(
    `INSERT INTO download_tokens (id, license_id, device_hash, jti, expires_at, used_at, created_at)
     VALUES (?, ?, ?, ?, ?, NULL, ?)`
  )
    .bind(randomId("dlt"), license.id, deviceHash, jti, expires.toISOString(), now.toISOString())
    .run();

  // Same host the client called (local :8787 or this Worker). PUBLIC_BASE_URL
  // is fallback only -- a committed workers.dev URL used to steal local tokens.
  const downloadUrl = `${requestPublicBase(req, env)}/v1/download/file?jti=${encodeURIComponent(jti)}`;

  await notifyOwner(env, "download issued", [
    `event: download_token`,
    `license: ${license.license_key}`,
    `device: ${deviceHash.slice(0, 12)}…`,
    `jti: ${jti}`,
    `ip: ${clientIp(req)}`,
    `ts: ${now.toISOString()}`,
  ]);

  return json(env, req, {
    download_url: downloadUrl,
    expires_at: expires.toISOString(),
    jti,
    note: "One-shot token. Upload NetieSpace-Setup.exe to R2 before production downloads.",
  });
}

async function handleDownloadFile(req: Request, env: Env, url: URL): Promise<Response> {
  const jti = url.searchParams.get("jti") ?? "";
  if (!jti) return json(env, req, { error: "missing_jti" }, 400);

  const row = await env.DB.prepare(
    "SELECT id, license_id, expires_at, used_at FROM download_tokens WHERE jti = ?"
  )
    .bind(jti)
    .first<{ id: string; license_id: string; expires_at: string; used_at: string | null }>();

  if (!row) return json(env, req, { error: "invalid_token" }, 404);
  if (row.used_at) return json(env, req, { error: "already_used" }, 403);
  if (new Date(row.expires_at).getTime() < Date.now()) {
    return json(env, req, { error: "expired" }, 403);
  }

  await env.DB.prepare("UPDATE download_tokens SET used_at = ? WHERE id = ?")
    .bind(new Date().toISOString(), row.id)
    .run();

  if (env.INSTALLERS) {
    const obj = await env.INSTALLERS.get("NetieSpace-Setup.exe");
    if (obj) {
      const headers = new Headers();
      obj.writeHttpMetadata(headers);
      headers.set("Content-Type", "application/octet-stream");
      headers.set("Content-Disposition", 'attachment; filename="NetieSpace-Setup.exe"');
      return new Response(obj.body, { headers });
    }
  }

  return json(env, req, {
    error: "installer_not_uploaded",
    message: "Token burned. Upload NetieSpace-Setup.exe to R2 bucket key NetieSpace-Setup.exe.",
  }, 503);
}

async function handleMe(req: Request, env: Env): Promise<Response> {
  const auth = await requireUser(env, req);
  if (auth instanceof Response) return auth;

  const user = await env.DB.prepare("SELECT email, created_at, stripe_customer_id FROM users WHERE id = ?")
    .bind(auth.userId)
    .first<{ email: string; created_at: string; stripe_customer_id: string | null }>();
  const license = await env.DB.prepare(
    "SELECT id, license_key, status, max_devices FROM licenses WHERE user_id = ? ORDER BY created_at DESC LIMIT 1"
  )
    .bind(auth.userId)
    .first();
  const devices = await env.DB.prepare(
    "SELECT device_hash, device_label, bound_at, last_seen_at FROM devices WHERE license_id = ?"
  )
    .bind((license as { id: string } | null)?.id ?? "")
    .all();

  return json(env, req, {
    email: user?.email,
    created_at: user?.created_at,
    stripe_customer_id: user?.stripe_customer_id ?? null,
    license,
    devices: devices.results ?? [],
  });
}

async function handleCheckoutSuccess(req: Request, env: Env, url: URL): Promise<Response> {
  const sessionId = url.searchParams.get("session_id") ?? "";
  let paid = false;
  if (stripeConfigured(env) && sessionId.startsWith("cs_")) {
    try {
      const session = await retrieveCheckoutSession(env, sessionId);
      if (session && checkoutSessionPaid(session)) {
        await fulfillCheckoutSession(env, session);
        paid = true;
      }
    } catch {
      // Webhook may still fulfill. Never fail the buyer-facing page.
    }
  }
  if (paid) {
    return html(
      env,
      req,
      "Payment received. Return to Netie Space: Activate this laptop."
    );
  }
  if (sessionId.startsWith("cs_")) {
    return html(
      env,
      req,
      "Payment is not complete yet. If you were charged, wait and Activate. Otherwise Setup -> Get Netie subscription."
    );
  }
  return html(env, req, "Return to Netie Space.");
}

async function handleCheckout(req: Request, env: Env): Promise<Response> {
  if (!stripeConfigured(env)) {
    return json(
      env,
      req,
      {
        error: "stripe_not_configured",
        message:
          "Stripe Checkout is not live. Set Worker secrets STRIPE_SECRET_KEY, STRIPE_PRICE_ID, STRIPE_WEBHOOK_SECRET. See docs/HUMAN_STRIPE_CHECKOUT.md.",
      },
      503
    );
  }

  const body = await readJson(req);
  const auth = await requireUser(env, req);
  let userId = "";
  let email = normalizeEmail(String(body.email ?? ""));

  if (!(auth instanceof Response)) {
    userId = auth.userId;
    const row = await env.DB.prepare("SELECT email FROM users WHERE id = ?")
      .bind(userId)
      .first<{ email: string }>();
    if (row?.email) email = normalizeEmail(row.email);
  }

  // Playbook: Buy without a session is register_first. An email-only
  // lookup used to start Checkout for any registered address (Login
  // sends EmailBox even when Sign in never ran).
  if (!userId) {
    return json(
      env,
      req,
      {
        error: "register_first",
        message: "Sign in first (tray -> Account), then click Get Netie subscription.",
      },
      400
    );
  }

  const paid = await env.DB.prepare(
    `SELECT l.status, l.stripe_checkout_session_id, u.stripe_customer_id
     FROM licenses l
     JOIN users u ON u.id = l.user_id
     WHERE l.user_id = ?
     ORDER BY l.created_at DESC LIMIT 1`
  )
    .bind(userId)
    .first<{
      status: string;
      stripe_checkout_session_id: string | null;
      stripe_customer_id: string | null;
    }>();
  if (paid?.status === "active" && paid.stripe_checkout_session_id) {
    return json(
      env,
      req,
      {
        error: "already_paid",
        message: "Already paid. Activate this laptop (do not checkout again).",
      },
      409
    );
  }

  try {
    const session = await createCheckoutSession(env, {
      userId,
      email,
      customerId: paid?.stripe_customer_id,
      publicBase: requestPublicBase(req, env),
    });
    return json(env, req, { checkout_url: session.url, session_id: session.id });
  } catch (err) {
    return json(env, req, { error: "stripe_error", message: String(err) }, 502);
  }
}

async function handleBillingPortal(req: Request, env: Env): Promise<Response> {
  if (!stripeConfigured(env)) {
    return json(env, req, { error: "stripe_not_configured" }, 503);
  }
  const auth = await requireUser(env, req);
  if (auth instanceof Response) return auth;

  const user = await env.DB.prepare("SELECT stripe_customer_id FROM users WHERE id = ?")
    .bind(auth.userId)
    .first<{ stripe_customer_id: string | null }>();
  if (!user?.stripe_customer_id) {
    return json(
      env,
      req,
      { error: "no_customer", message: "Pay first (Setup -> Get Netie subscription), then manage billing." },
      404
    );
  }

  try {
    const portal = await createPortalSession(env, user.stripe_customer_id, requestPublicBase(req, env));
    return json(env, req, { portal_url: portal.url });
  } catch (err) {
    return json(env, req, { error: "stripe_error", message: String(err) }, 502);
  }
}

async function handleStripeWebhook(req: Request, env: Env): Promise<Response> {
  if (!stripeWebhookConfigured(env)) {
    return json(env, req, { error: "stripe_not_configured" }, 503);
  }

  const raw = await req.text();
  const header = req.headers.get("Stripe-Signature") ?? "";
  if (!(await verifyStripeSignature(raw, header, env.STRIPE_WEBHOOK_SECRET!))) {
    return json(env, req, { error: "invalid_signature" }, 400);
  }

  let event: { type?: string; data?: { object?: Record<string, unknown> } };
  try {
    event = JSON.parse(raw) as typeof event;
  } catch {
    return json(env, req, { error: "invalid_json" }, 400);
  }

  const obj = event.data?.object ?? {};
  if (event.type === "checkout.session.completed") {
    await fulfillCheckoutSession(env, {
      id: String(obj.id ?? ""),
      customer: stripeRefId(obj.customer, "cus_"),
      subscription: stripeRefId(obj.subscription, "sub_"),
      customer_email: typeof obj.customer_email === "string" ? obj.customer_email : null,
      customer_details:
        obj.customer_details && typeof obj.customer_details === "object"
          ? (obj.customer_details as { email?: string | null })
          : null,
      client_reference_id:
        typeof obj.client_reference_id === "string" ? obj.client_reference_id : null,
      metadata:
        obj.metadata && typeof obj.metadata === "object"
          ? (obj.metadata as Record<string, string>)
          : null,
    });
  } else if (
    event.type === "customer.subscription.deleted" ||
    event.type === "customer.subscription.updated"
  ) {
    await markSubscriptionStatus(env, String(obj.id ?? ""), String(obj.status ?? ""));
  }

  return json(env, req, { received: true });
}
