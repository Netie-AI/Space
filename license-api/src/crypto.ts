/** Crypto helpers for Netie Space license API (Workers-safe). */

const textEncoder = new TextEncoder();

export function randomId(prefix = ""): string {
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  const hex = [...bytes].map((b) => b.toString(16).padStart(2, "0")).join("");
  return prefix ? `${prefix}_${hex}` : hex;
}

export function randomLicenseKey(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(12));
  const hex = [...bytes].map((b) => b.toString(16).padStart(2, "0")).join("").toUpperCase();
  return `NETIE-${hex.slice(0, 4)}-${hex.slice(4, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 24)}`;
}

export async function sha256Hex(input: string): Promise<string> {
  const dig = await crypto.subtle.digest("SHA-256", textEncoder.encode(input));
  return [...new Uint8Array(dig)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

/** OWASP-recommended PBKDF2-SHA256 (Workers-native). Algo stored for future Argon2id upgrade. */
export async function hashPassword(password: string): Promise<string> {
  const salt = crypto.getRandomValues(new Uint8Array(16));
  const key = await derivePbkdf2(password, salt, 310_000);
  const saltB64 = btoa(String.fromCharCode(...salt));
  const hashB64 = btoa(String.fromCharCode(...new Uint8Array(key)));
  return `pbkdf2-sha256$310000$${saltB64}$${hashB64}`;
}

export async function verifyPassword(password: string, stored: string): Promise<boolean> {
  const parts = stored.split("$");
  if (parts.length !== 4 || parts[0] !== "pbkdf2-sha256") return false;
  const iterations = Number(parts[1]);
  const salt = Uint8Array.from(atob(parts[2]), (c) => c.charCodeAt(0));
  const expected = Uint8Array.from(atob(parts[3]), (c) => c.charCodeAt(0));
  const actual = new Uint8Array(await derivePbkdf2(password, salt, iterations));
  if (actual.length !== expected.length) return false;
  let diff = 0;
  for (let i = 0; i < actual.length; i++) diff |= actual[i] ^ expected[i];
  return diff === 0;
}

async function derivePbkdf2(password: string, salt: Uint8Array, iterations: number): Promise<ArrayBuffer> {
  const baseKey = await crypto.subtle.importKey(
    "raw",
    textEncoder.encode(password),
    "PBKDF2",
    false,
    ["deriveBits"]
  );
  return crypto.subtle.deriveBits(
    { name: "PBKDF2", salt: salt as BufferSource, iterations, hash: "SHA-256" },
    baseKey,
    256
  );
}

export async function hmacSign(secret: string, payload: string): Promise<string> {
  const key = await crypto.subtle.importKey(
    "raw",
    textEncoder.encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );
  const sig = await crypto.subtle.sign("HMAC", key, textEncoder.encode(payload));
  return btoa(String.fromCharCode(...new Uint8Array(sig)))
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/, "");
}

export async function createSessionToken(secret: string, userId: string, ttlSec = 60 * 60 * 24 * 30): Promise<{ token: string; expiresAt: string; tokenHash: string }> {
  const exp = Math.floor(Date.now() / 1000) + ttlSec;
  const body = `${userId}.${exp}.${randomId("s")}`;
  const sig = await hmacSign(secret, body);
  const token = `${body}.${sig}`;
  const tokenHash = await sha256Hex(token);
  return { token, expiresAt: new Date(exp * 1000).toISOString(), tokenHash };
}

export async function verifySessionToken(secret: string, token: string): Promise<{ userId: string } | null> {
  const parts = token.split(".");
  if (parts.length !== 4) return null;
  const [userId, expStr, nonce, sig] = parts;
  const body = `${userId}.${expStr}.${nonce}`;
  const expected = await hmacSign(secret, body);
  if (expected !== sig) return null;
  const exp = Number(expStr);
  if (!Number.isFinite(exp) || exp * 1000 < Date.now()) return null;
  return { userId };
}

export function normalizeEmail(email: string): string {
  return email.trim().toLowerCase();
}
