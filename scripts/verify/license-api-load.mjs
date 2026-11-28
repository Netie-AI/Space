// Local load check for license-api (wrangler dev --local only; refuses any non-loopback URL).
// Usage: node scripts/verify/license-api-load.mjs [baseUrl=http://127.0.0.1:8787] [levels=1,10,50]
// Flow per virtual user: register -> login -> activate -> me. Prints per-step p50/p95 and error counts.
// Never prints tokens or license keys.
const base = process.argv[2] ?? "http://127.0.0.1:8787";
const levels = (process.argv[3] ?? "1,10,50").split(",").map(Number);
const u = new URL(base);
if (!["127.0.0.1", "localhost", "::1"].includes(u.hostname)) {
  console.error("refusing non-loopback target " + u.hostname);
  process.exit(2);
}

const pct = (a, q) => (a.length ? [...a].sort((x, y) => x - y)[Math.min(a.length - 1, Math.ceil(q * a.length) - 1)] : 0);

async function call(path, init) {
  const t = performance.now();
  try {
    const r = await fetch(base + path, init);
    const body = await r.json().catch(() => ({}));
    return { ms: performance.now() - t, status: r.status, body };
  } catch (e) {
    return { ms: performance.now() - t, status: 0, body: { error: String(e) } };
  }
}

async function user(run, i) {
  const email = `load-${run}-${i}@example.test`;
  const password = "netie-load-pass-" + i;
  const json = { "content-type": "application/json" };
  const out = {};
  out.register = await call("/v1/register", { method: "POST", headers: json, body: JSON.stringify({ email, password }) });
  out.login = await call("/v1/login", { method: "POST", headers: json, body: JSON.stringify({ email, password }) });
  const token = out.login.body?.token;
  const auth = { ...json, authorization: "Bearer " + token };
  out.activate = await call("/v1/activate", { method: "POST", headers: auth, body: JSON.stringify({ device_hash: "dev-" + run + "-" + i + "-0123456789abcdef" }) });
  out.me = await call("/v1/me", { headers: auth });
  return out;
}

const run = Date.now().toString(36);
const rows = [];
for (const n of levels) {
  const t = performance.now();
  const results = await Promise.all(Array.from({ length: n }, (_, i) => user(run + "-" + n, i)));
  const wall = performance.now() - t;
  for (const step of ["register", "login", "activate", "me"]) {
    const ms = results.map((r) => r[step].ms);
    const codes = {};
    for (const r of results) codes[r[step].status] = (codes[r[step].status] ?? 0) + 1;
    rows.push({ users: n, step, p50: Math.round(pct(ms, 0.5)), p95: Math.round(pct(ms, 0.95)), codes: JSON.stringify(codes), wall: Math.round(wall) });
  }
}
console.log("| users | step | p50 ms | p95 ms | status counts | wall ms (all steps) |");
console.log("|---|---|---|---|---|---|");
for (const r of rows) console.log(`| ${r.users} | ${r.step} | ${r.p50} | ${r.p95} | ${r.codes} | ${r.wall} |`);
const bad = rows.filter((r) => Object.keys(JSON.parse(r.codes)).some((c) => c !== "200"));
if (bad.length) { console.log("NON-200 rows: " + bad.length); process.exit(1); }
console.log("PASS license-api-load");
