-- Netie Space license schema (Stripe columns ready; Checkout later)

CREATE TABLE IF NOT EXISTS users (
  id TEXT PRIMARY KEY,
  email TEXT NOT NULL UNIQUE COLLATE NOCASE,
  password_hash TEXT NOT NULL,
  password_algo TEXT NOT NULL DEFAULT 'pbkdf2-sha256',
  email_verified INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL,
  stripe_customer_id TEXT
);

CREATE TABLE IF NOT EXISTS licenses (
  id TEXT PRIMARY KEY,
  user_id TEXT NOT NULL REFERENCES users(id),
  license_key TEXT NOT NULL UNIQUE,
  status TEXT NOT NULL DEFAULT 'active', -- active | revoked | expired
  max_devices INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL,
  stripe_subscription_id TEXT,
  stripe_checkout_session_id TEXT
);

CREATE TABLE IF NOT EXISTS devices (
  id TEXT PRIMARY KEY,
  license_id TEXT NOT NULL REFERENCES licenses(id),
  device_hash TEXT NOT NULL,
  device_label TEXT,
  bound_at TEXT NOT NULL,
  last_seen_at TEXT NOT NULL,
  UNIQUE(license_id, device_hash)
);

CREATE TABLE IF NOT EXISTS download_tokens (
  id TEXT PRIMARY KEY,
  license_id TEXT NOT NULL REFERENCES licenses(id),
  device_hash TEXT,
  jti TEXT NOT NULL UNIQUE,
  expires_at TEXT NOT NULL,
  used_at TEXT,
  created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS sessions (
  id TEXT PRIMARY KEY,
  user_id TEXT NOT NULL REFERENCES users(id),
  token_hash TEXT NOT NULL UNIQUE,
  expires_at TEXT NOT NULL,
  created_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_licenses_user ON licenses(user_id);
CREATE INDEX IF NOT EXISTS idx_devices_license ON devices(license_id);
CREATE INDEX IF NOT EXISTS idx_download_unused ON download_tokens(license_id, used_at);
