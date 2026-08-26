"""Contract checks for Stripe Checkout + Buy API (secrets-gated)."""
from __future__ import annotations

import hashlib
import hmac
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
WORKER = ROOT / "license-api/src/index.ts"
STRIPE = ROOT / "license-api/src/stripe.ts"
CLIENT = ROOT / "src/NetieSpace/Services/Licensing/LicenseApiClient.cs"
SETUP = ROOT / "src/NetieSpace/Views/ApiSetupWindow.xaml.cs"
HUMAN = ROOT / "docs/HUMAN_STRIPE_CHECKOUT.md"
PILLAR = ROOT / "docs/HUMAN_PILLAR1_TEST.md"
PARKING = ROOT / "PARKING_LOT.md"


def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def test_worker_checkout_routes():
    src = _read(WORKER)
    stripe = _read(STRIPE)
    assert 'url.pathname === "/v1/checkout"' in src
    assert 'url.pathname === "/v1/webhooks/stripe"' in src
    assert 'error: "stripe_not_configured"' in src
    assert "createCheckoutSession" in src
    assert "verifyStripeSignature" in src
    assert "fulfillCheckoutSession" in stripe
    assert "retrieveCheckoutSession" in stripe
    assert "checkoutSessionPaid" in stripe
    assert "handleCheckoutSuccess" in src
    assert 'url.pathname === "/v1/stripe/status"' in src
    assert "payment_status === \"paid\"" in stripe or 'payment_status === "paid"' in stripe
    assert "payment_method_types" not in stripe
    assert "automatic_tax" not in stripe
    assert 'mode", "subscription"' in stripe or 'body.set("mode", "subscription")' in stripe
    assert "integration_identifier" in stripe
    assert "2026-07-29.dahlia" in stripe
    wrangler = _read(ROOT / "license-api/wrangler.toml")
    assert "price_1U8ckCFV5wcFod2faXiUCsaV" in wrangler
    assert "STRIPE_PRICE_ID" in wrangler
    setup = _read(ROOT / "scripts/setup-license-api.ps1")
    assert "STRIPE_SECRET_KEY" in setup
    assert "STRIPE_WEBHOOK_SECRET" in setup


def test_wpf_buy_calls_checkout():
    client = _read(CLIENT)
    setup = _read(SETUP)
    assert "CreateCheckoutAsync" in client
    assert "CreatePortalAsync" in client
    assert "GetStripeStatusAsync" in client
    assert "checkout_url" in client
    assert "portal_url" in client
    assert "CreateCheckoutAsync" in setup
    assert "CreatePortalAsync" in setup
    assert "RefreshStripeStatusAsync" in setup
    assert "ManageBillingButton_Click" in setup
    assert "ActivateLaptopButton_Click" in setup
    assert "LicenseActivation.ActivateCurrentSessionAsync" in setup
    login = _read(ROOT / "src/NetieSpace/Views/LoginWindow.xaml.cs")
    login_xaml = _read(ROOT / "src/NetieSpace/Views/LoginWindow.xaml")
    assert "ShowPayFirstButtons" in login
    assert "BuyButton_Click" in login
    assert "ActivateAfterPayButton_Click" in login
    assert "PaymentRequired" in login
    assert 'x:Name="BuyButton"' in login_xaml
    assert 'x:Name="ActivateAfterPayButton"' in login_xaml
    assert "payment_required" in _read(WORKER)
    assert 'stripeConfigured(env) ? "pending" : "active"' in _read(WORKER)
    assert "stripe_checkout_session_id" in _read(WORKER)
    client = _read(CLIENT)
    assert "ParseErrorCode" in client
    assert "PaymentRequired" in client
    activation = _read(ROOT / "src/NetieSpace/Services/Licensing/LicenseActivation.cs")
    assert "ActivateCurrentSessionAsync" in activation
    assert "System.Windows" not in activation
    assert "public static void Configure" in activation
    assert "ResolveApiBase" in activation
    assert "LicenseActivation.ResolveApiBase()" in setup
    assert 'Environment.GetEnvironmentVariable("NETIE_LICENSE_API")' not in setup
    app = _read(ROOT / "src/NetieSpace/App.xaml.cs")
    assert "LicenseActivation.Configure" in app
    assert "LicenseActivation.ResolveApiBase()" in app
    assert "OpenBuyFallback" in setup
    assert "HUMAN_STRIPE_CHECKOUT.md" in setup
    assert "HUMAN_PILLAR1_TEST.md" in setup


def test_human_docs_exist():
    pillar = _read(PILLAR)
    human = _read(HUMAN)
    parking = _read(PARKING)
    assert "Playwright cannot open Netie Space" in pillar
    assert "Select -> Split -> Drag -> Delete" in pillar or "Select -> Split" in pillar
    assert "STRIPE_SECRET_KEY" in human
    assert "4242 4242 4242 4242" in human
    assert "stripe_not_configured" in human
    assert "pending" in human
    assert "payment_required" in human
    assert "Buy API flow" in parking
    assert "Stripe Checkout" in parking
    assert 'url.pathname === "/v1/billing-portal"' in _read(WORKER)


def _hmac_hex(secret: str, payload: str) -> str:
    return hmac.new(secret.encode(), payload.encode(), hashlib.sha256).hexdigest()


def _verify_stripe_sig(raw: str, header: str, secret: str, now: int, tolerance: int = 300) -> bool:
    ts = ""
    v1: list[str] = []
    for part in header.split(","):
        part = part.strip()
        if "=" not in part:
            continue
        k, v = part.split("=", 1)
        if k == "t":
            ts = v
        if k == "v1":
            v1.append(v)
    if not ts or not v1:
        return False
    try:
        tsi = int(ts)
    except ValueError:
        return False
    if abs(now - tsi) > tolerance:
        return False
    expected = _hmac_hex(secret, f"{ts}.{raw}")
    return any(hmac.compare_digest(sig, expected) for sig in v1)


def test_stripe_signature_vectors():
    secret = "whsec_test_secret"
    raw = '{"id":"evt_1","type":"checkout.session.completed"}'
    now = 1_700_000_000
    good = _hmac_hex(secret, f"{now}.{raw}")
    header = f"t={now},v1={good}"
    assert _verify_stripe_sig(raw, header, secret, now) is True
    assert _verify_stripe_sig(raw, header, "wrong", now) is False
    assert _verify_stripe_sig(raw, f"t={now - 400},v1={_hmac_hex(secret, f'{now - 400}.{raw}')}", secret, now) is False
    assert _verify_stripe_sig("tampered", header, secret, now) is False
    assert "verifyStripeSignature" in _read(STRIPE)
    assert "WEBHOOK_TOLERANCE_SEC = 300" in _read(STRIPE)


if __name__ == "__main__":
    test_worker_checkout_routes()
    test_wpf_buy_calls_checkout()
    test_human_docs_exist()
    test_stripe_signature_vectors()
    print("PASS test_stripe_checkout")
