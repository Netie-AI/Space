/** Stripe Checkout + webhook for Netie Space licenses. No Stripe SDK (Workers fetch). */

import type { Env } from "./types";
import {
  hashPassword,
  normalizeEmail,
  randomId,
  randomLicenseKey,
} from "./crypto";
import { emailBuyer, notifyOwner } from "./email";

const STRIPE_CHECKOUT_API = "https://api.stripe.com/v1/checkout/sessions";
const STRIPE_PORTAL_API = "https://api.stripe.com/v1/billing_portal/sessions";
const STRIPE_VERSION = "2026-07-29.dahlia";
const WEBHOOK_TOLERANCE_SEC = 300;

export function stripeConfigured(env: Env): boolean {
  return Boolean(env.STRIPE_SECRET_KEY && env.STRIPE_PRICE_ID);
}

export function stripeWebhookConfigured(env: Env): boolean {
  return Boolean(env.STRIPE_WEBHOOK_SECRET);
}

function randomSuffix(n = 8): string {
  const bytes = crypto.getRandomValues(new Uint8Array(n));
  return [...bytes].map((b) => (b % 26 + 10).toString(36)).join("").slice(0, n);
}

export async function hmacSha256Hex(secret: string, payload: string): Promise<string> {
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );
  const sig = await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(payload));
  return [...new Uint8Array(sig)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

function timingSafeEqual(a: string, b: string): boolean {
  if (a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}

/** Verify Stripe-Signature (t + v1) against the raw body. */
export async function verifyStripeSignature(
  rawBody: string,
  header: string,
  secret: string,
  nowSec = Math.floor(Date.now() / 1000)
): Promise<boolean> {
  const parts = header.split(",").map((p) => p.trim());
  let timestamp = "";
  const v1: string[] = [];
  for (const part of parts) {
    const eq = part.indexOf("=");
    if (eq < 0) continue;
    const k = part.slice(0, eq);
    const v = part.slice(eq + 1);
    if (k === "t") timestamp = v;
    if (k === "v1") v1.push(v);
  }
  const ts = Number(timestamp);
  if (!timestamp || !Number.isFinite(ts) || v1.length === 0) return false;
  if (Math.abs(nowSec - ts) > WEBHOOK_TOLERANCE_SEC) return false;
  const expected = await hmacSha256Hex(secret, `${timestamp}.${rawBody}`);
  return v1.some((sig) => timingSafeEqual(sig, expected));
}

export async function createCheckoutSession(
  env: Env,
  opts: { userId: string; email: string; customerId?: string | null }
): Promise<{ url: string; id: string }> {
  const body = new URLSearchParams();
  body.set("mode", "subscription");
  body.set("line_items[0][price]", env.STRIPE_PRICE_ID!);
  body.set("line_items[0][quantity]", "1");
  body.set(
    "success_url",
    `${env.PUBLIC_BASE_URL}/v1/checkout/success?session_id={CHECKOUT_SESSION_ID}`
  );
  body.set("cancel_url", `${env.PUBLIC_BASE_URL}/v1/checkout/cancel`);
  body.set("client_reference_id", opts.userId);
  // Stripe rejects customer + customer_email together.
  const customerId = stripeRefId(opts.customerId, "cus_");
  if (customerId) body.set("customer", customerId);
  else body.set("customer_email", opts.email);
  body.set("metadata[user_id]", opts.userId);
  body.set("metadata[email]", opts.email);
  body.set("subscription_data[metadata][user_id]", opts.userId);
  body.set("integration_identifier", `netie-spc-${randomSuffix()}`);

  const res = await fetch(STRIPE_CHECKOUT_API, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${env.STRIPE_SECRET_KEY}`,
      "Content-Type": "application/x-www-form-urlencoded",
      "Stripe-Version": STRIPE_VERSION,
    },
    body,
  });
  const json = (await res.json()) as { id?: string; url?: string; error?: { message?: string } };
  if (!res.ok || !json.url || !json.id) {
    throw new Error(json.error?.message || `stripe_http_${res.status}`);
  }
  return { url: json.url, id: json.id };
}

type CheckoutSession = {
  id: string;
  status?: string | null;
  payment_status?: string | null;
  customer?: unknown;
  subscription?: unknown;
  customer_email?: string | null;
  customer_details?: { email?: string | null } | null;
  client_reference_id?: string | null;
  metadata?: Record<string, string> | null;
};

/** Stripe IDs arrive as strings or expanded `{ id }` objects. Never throw. */
export function stripeRefId(value: unknown, prefix?: string): string | null {
  let id = "";
  if (typeof value === "string") id = value.trim();
  else if (value && typeof value === "object" && "id" in value) {
    const raw = (value as { id?: unknown }).id;
    if (typeof raw === "string") id = raw.trim();
  }
  if (!id) return null;
  if (prefix && !id.startsWith(prefix)) return null;
  return id;
}

export function checkoutSessionPaid(session: {
  status?: string | null;
  payment_status?: string | null;
}): boolean {
  return session.status === "complete" || session.payment_status === "paid";
}

/** GET /v1/checkout/sessions/:id. Null on HTTP/parse failure. Never throws. */
export async function retrieveCheckoutSession(
  env: Env,
  sessionId: string
): Promise<CheckoutSession | null> {
  try {
    if (!sessionId.startsWith("cs_") || !env.STRIPE_SECRET_KEY) return null;
    const res = await fetch(`${STRIPE_CHECKOUT_API}/${encodeURIComponent(sessionId)}`, {
      headers: {
        Authorization: `Bearer ${env.STRIPE_SECRET_KEY}`,
        "Stripe-Version": STRIPE_VERSION,
      },
    });
    if (!res.ok) return null;
    const json = (await res.json()) as CheckoutSession;
    if (!json?.id) return null;
    json.customer = stripeRefId(json.customer, "cus_");
    json.subscription = stripeRefId(json.subscription, "sub_");
    return json;
  } catch {
    return null;
  }
}

export async function fulfillCheckoutSession(env: Env, session: CheckoutSession): Promise<void> {
  const sessionId = session.id;
  if (!sessionId) return;

  const customerId = stripeRefId(session.customer, "cus_");
  const subscriptionId = stripeRefId(session.subscription, "sub_");

  const already = await env.DB.prepare(
    "SELECT id, user_id FROM licenses WHERE stripe_checkout_session_id = ?"
  )
    .bind(sessionId)
    .first<{ id: string; user_id: string }>();
  if (already) {
    // Webhook may have written the session before customer/subscription IDs
    // were parseable. Success-page retrieve must still backfill portal ids.
    if (customerId) {
      await env.DB.prepare(
        "UPDATE users SET stripe_customer_id = COALESCE(stripe_customer_id, ?) WHERE id = ?"
      )
        .bind(customerId, already.user_id)
        .run();
    }
    if (subscriptionId) {
      await env.DB.prepare(
        "UPDATE licenses SET stripe_subscription_id = COALESCE(stripe_subscription_id, ?) WHERE id = ?"
      )
        .bind(subscriptionId, already.id)
        .run();
    }
    return;
  }

  const email = normalizeEmail(
    session.metadata?.email ||
      session.customer_details?.email ||
      session.customer_email ||
      ""
  );
  const userId = session.client_reference_id || session.metadata?.user_id || "";
  const now = new Date().toISOString();

  let user = userId
    ? await env.DB.prepare("SELECT id, email FROM users WHERE id = ?")
        .bind(userId)
        .first<{ id: string; email: string }>()
    : null;
  if (!user && email) {
    user = await env.DB.prepare("SELECT id, email FROM users WHERE email = ?")
      .bind(email)
      .first<{ id: string; email: string }>();
  }

  if (!user) {
    if (!email.includes("@")) return;
    const newId = randomId("usr");
    const passwordHash = await hashPassword(randomId("tmp"));
    await env.DB.prepare(
      `INSERT INTO users (id, email, password_hash, password_algo, email_verified, created_at, stripe_customer_id)
       VALUES (?, ?, ?, 'pbkdf2-sha256', 1, ?, ?)`
    )
      .bind(newId, email, passwordHash, now, customerId)
      .run();
    user = { id: newId, email };
  } else if (customerId) {
    await env.DB.prepare("UPDATE users SET stripe_customer_id = ? WHERE id = ?")
      .bind(customerId, user.id)
      .run();
  }

  let license = await env.DB.prepare(
    "SELECT id, license_key FROM licenses WHERE user_id = ? ORDER BY created_at DESC LIMIT 1"
  )
    .bind(user.id)
    .first<{ id: string; license_key: string }>();

  if (!license) {
    const licenseId = randomId("lic");
    const licenseKey = randomLicenseKey();
    await env.DB.prepare(
      `INSERT INTO licenses (id, user_id, license_key, status, max_devices, created_at, stripe_subscription_id, stripe_checkout_session_id)
       VALUES (?, ?, ?, 'active', 1, ?, ?, ?)`
    )
      .bind(licenseId, user.id, licenseKey, now, subscriptionId, sessionId)
      .run();
    license = { id: licenseId, license_key: licenseKey };
  } else {
    await env.DB.prepare(
      `UPDATE licenses SET status = 'active', stripe_subscription_id = ?, stripe_checkout_session_id = ? WHERE id = ?`
    )
      .bind(subscriptionId, sessionId, license.id)
      .run();
  }

  await notifyOwner(env, "stripe checkout", [
    `event: checkout.session.completed`,
    `email: ${user.email}`,
    `license: ${license.license_key}`,
    `session: ${sessionId}`,
    `ts: ${now}`,
  ]);
  await emailBuyer(
    env,
    user.email,
    `${env.APP_NAME} subscription active`,
    `Payment received.\n\nLicense key: ${license.license_key}\n\nOpen Netie Space → tray → Account / Sign in → Activate this laptop.\n\nWe never email your password.`
  );
}

export async function createPortalSession(
  env: Env,
  customerId: string
): Promise<{ url: string }> {
  const body = new URLSearchParams();
  body.set("customer", customerId);
  body.set("return_url", `${env.PUBLIC_BASE_URL}/v1/checkout/success`);

  const res = await fetch(STRIPE_PORTAL_API, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${env.STRIPE_SECRET_KEY}`,
      "Content-Type": "application/x-www-form-urlencoded",
      "Stripe-Version": STRIPE_VERSION,
    },
    body,
  });
  const json = (await res.json()) as { url?: string; error?: { message?: string } };
  if (!res.ok || !json.url) {
    throw new Error(json.error?.message || `stripe_portal_${res.status}`);
  }
  return { url: json.url };
}

export async function markSubscriptionStatus(
  env: Env,
  subscriptionId: string,
  status: string
): Promise<void> {
  if (!subscriptionId) return;
  const licenseStatus = status === "active" || status === "trialing" ? "active" : "expired";
  await env.DB.prepare("UPDATE licenses SET status = ? WHERE stripe_subscription_id = ?")
    .bind(licenseStatus, subscriptionId)
    .run();
}
