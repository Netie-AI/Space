# YC Fall 2026 — application draft

Source of truth: `docs/NETIE_PLATFORM_WHITEPAPER.html` (status chips), `docs/NETIE_SPACE_PRODUCT_BRIEF.md`,
`docs/AIRGPT_SYNERGY.md`, `docs/CORTEX_RUNTIME_BEST_FINAL.md`. Anything in [brackets] is a number or fact
only you can fill — do not ship the application with brackets left in.

---

## Company description (50 char limit)

Pick one:

1. `Press Space: private AI for any file on Windows` (47)
2. `Private AI workspace for Windows knowledge work` (47)
3. `Governed local AI for every file on Windows` (43)
4. `Windows AI desktop with a governed AI engine` (44)

Recommended: **1** — it names the gesture, the platform, and the privacy stance.

---

## What is your company going to make?

Netie is a private AI workspace for Windows, built as three products on one trust boundary.

**Netie Space** is the surface. Press Space on any file in File Explorer or the Desktop and it opens
instantly — PDF, Word, PowerPoint, Excel, images, code, video — as real pages, not a text dump. Inside
that preview you can summarize or chat with the file, merge/split/sign PDFs, trim and caption video,
OCR an image, and convert anything to Markdown or PDF. Everything except AI works with no network.
.NET 8 WPF, self-contained EXE, device-bound licensing. Shipped.

**AirGPT** is the app plane behind it. A local server on your own machine that holds your chat history,
clipboard, screenshots, phone photos, and whatever Space hands it, so context arrives without a paste.
Python, SQLite, phone→PC over a tunnel. Shipped, v0.9.0.

**Cortex** (the Netie Engine) is the governed runtime everything routes through. Before a prompt is
built it masks PII, blocks prompt-injection and scams, applies deterministic YAML compliance rules,
picks a model tier under a hard cost ceiling, runs tools in a WASM sandbox with fuel metering, and
appends every decision to a hash-chained audit ledger you can verify. Business mode hard-fails if the
engine is offline; personal mode soft-fails so a dead sidecar never breaks your day. FastAPI sidecar.
Partial — the gate, compliance engine, router, ledger and skill capture are real; the reversible PII
token vault, mTLS/egress allowlist and WASM memory cap are not built yet and are tracked in our own
gap register.

The consumer app is the distribution. The engine is the business.

---

## Who are your competitors? What do you understand about your business that they don't?

Three planes, three sets of competitors.

**Netie Space (desktop surface):** Microsoft PowerToys Peek, QuickLook, Seer for preview; Adobe Acrobat
for PDF; CapCut/Premiere for video; Copilot in Windows for AI. The previewers show a file and stop —
no AI, no editing. Acrobat and CapCut are an 8–12 second app launch for a 10-second task. Copilot
doesn't know which file you're staring at in Explorer, because it doesn't live there.

**AirGPT (app plane):** ChatGPT and Claude desktop apps, Msty, LM Studio, AnythingLLM, NotebookLM.
These are chat windows you paste into. AirGPT already holds the clipboard, the screenshot, the phone
photo and the file Space just handed it — the context is there before you ask.

**Cortex (engine plane):** LiteLLM, Portkey, Cloudflare AI Gateway, Lakera and Presidio, Credal, Glean,
Palantir AIP. Every one of them is sold to a platform team as a separate line item, and every one
assumes the enterprise already has AI usage worth governing.

What we understand that they don't: **the AI governance layer and the consumer desktop tool are the
same product, and only the consumer end has distribution.** Gateways sell to a buyer who has to first
convince their own workforce to use AI at all; most of them govern traffic that never materializes. We
ship the thing a worker actually presses — the Space bar, on a file they already have open — and the
masking, routing, cost ceiling and audit ledger ride in underneath it. By the time a compliance team
asks who's sending company documents to a model, the answer is already instrumented, because it was
installed as a preview tool.

Second thing: everyone above is built for a US SaaS default — upload the document, trust the vendor.
That is a non-starter for the buyers we're closest to. Cortex masks NRIC-format IDs, bounds spend in
MYR, defaults tiers 0–2 to self-hosted models, and deploys as Docker Compose behind Caddy on a
customer's own NAS or VPS, so the document never leaves the building. In Southeast Asia that's not a
feature, it's the entry ticket — and the incumbents' architecture points the other way.

Third: Windows. Mac gets the beautiful AI-native tools; Windows fleets get a Copilot licence. The
majority of regulated knowledge work in our market happens in Explorer, on Windows, on a laptop the
company owns. We build for that machine first.

---

## Why did you pick this idea to work on? Do you have domain expertise? How do you know people need it?

I switched to Windows and lost Quick Look — the one Mac gesture I used hundreds of times a day. Every
glance at a file became an app launch. I built Space for myself first and used it every day for
[N] months before showing anyone.

The AI part came from the same place: I wanted to ask a contract or a slide deck a question without
uploading it. Building that honestly forced the engine — once you route real work documents through a
model, you need masking, a spend ceiling and a log, or you shouldn't be doing it at all.

Evidence so far, honestly stated: I've demoed to [N] people ([who — students, SME ops staff, etc.]),
[N] asked to install it, and the consistent reaction is the local-first part, not the AI part — people
in [Malaysia/SEA] assume a cloud AI tool means their documents leave the country. I've run [N] survey
responses / [N] conversations. No paying users yet.

Domain expertise: [your background — years in X, the DMS/warehouse work behind Cortex's compliance
packs, etc.]. The Cortex compliance engine came out of a real document-management problem [describe
it in one line], not a whiteboard.

---

## How far along are you?

- **Netie Space** — shipped. Self-contained win-x64 build, Inno installer, Cloudflare Workers license
  API with register → activate → device-bind, Stripe-ready. Preview for PDF/DOCX/PPTX/XLSX/images/
  code/video, Acrobat-class PDF tools, multi-track video timeline with Whisper captions and SRT
  burn-in, MD/PDF conversion picked by benchmark, DPAPI-encrypted local vault, consent-gated local
  embedding memory that never leaves the machine.
- **AirGPT** — shipped, v0.9.0. Local server, SQLite sessions, clipboard/screenshot sync, phone→PC
  tunnel, multi-provider routing, context-compression so long chats stay cheap.
- **Cortex** — partial. PII redaction, injection guard, scam guard, deterministic YAML compliance
  engine, tiered router with cost ceiling, WASM isolate with fuel metering, hash-chained SQLite audit
  ledger and opt-in skill capture are working code. Reversible PII token vault, app-level mTLS and
  egress allowlist, WASM memory cap and Postgres serialized append are not built. We keep a gap
  register in our own architecture doc precisely so nobody demos a slide the code can't back.
- Public launch of Space this month. No revenue, no users outside my own testing yet.

---

## How long have each of you been working on this? Full-time?

Solo founder, full-time. About a year gathering the problem and testing tools, four months building —
the last [N] months full-time, no other job or clients.

---

## Tech stack (incl. AI models and AI coding tools)

- **Netie Space:** C# / .NET 8 WPF, WebView2, pdf.js, PdfSharp, LibVLC + FFmpeg, Python side tools
  (PyMuPDF, pymupdf4llm, pdfmd, pptx2md, markitdown, rembg, faster-whisper).
- **AirGPT:** Python HTTP server, SQLite, single-page JS UI, Cloudflare tunnel.
- **Cortex:** Python FastAPI, wasmtime WASM isolation, SQLite → Postgres audit ledger, BGE dense +
  BM25 sparse hybrid retrieval with RRF reranking, Docker Compose + Caddy for deployment.
- **Models:** Claude (Anthropic) for the hardest tier, Gemini, Groq-hosted Llama for fast/cheap
  summaries, SEA-LION for Southeast Asian languages, DeepSeek and OpenRouter as fallbacks, self-hosted
  Qwen on vLLM for tiers 0–2, Whisper for captions, BGE for embeddings. The router picks the tier; the
  cheapest rung that holds wins.
- **AI coding tools:** Claude Code (primary) and Cursor. I run a two-gate review protocol — a slice
  only advances when two independent models both pass it on correctness and scope creep — which is why
  a solo founder can hold three codebases.

---

## When will you have a version people can use?

Netie Space ships publicly this month. Preview, PDF tools, video editing and conversion are free and
work offline forever. Paid starts when you want the AI: a Netie subscription for hosted models, or
bring your own key. Revenue after that comes from Cortex — the governed engine sold per seat or as an
on-prem appliance to companies that can't send documents to a US SaaS.

---

## How do or will you make money? How much could you make?

Three lines:

1. **Netie Space subscription** — free to preview and edit; [$8]/month or [$39] one-time for AI
   summary, chat and Look Up with hosted models. Consumer/prosumer, low ACV, high volume, and it's the
   acquisition channel for everything else.
2. **Netie AI (hosted inference)** — margin on routed inference, with the cost ceiling and tier router
   making the gross margin defensible rather than accidental.
3. **Cortex** — the real business. Per-seat governed AI for regulated mid-market companies, or an
   on-prem appliance where nothing egresses. [$15–30]/seat/month, or [$40–80k]/year for on-prem.

Best estimate: [50,000] paying Space seats at [$8]/month is ~[$4.8M] ARR; [40] Cortex accounts at
[$60k] is ~[$2.4M]. A ~[$7M] ARR path within [3] years without needing either line to be a
category winner. The upside case is that the engine becomes the default governance layer for AI in
Southeast Asian regulated industries, which is a market measured in [seats, not apps].

---

## Location

Kuala Lumpur, Malaysia / Singapore, Malaysia.

Southeast Asia is where this product is needed first and served worst. Regulated buyers here — banks,
logistics, healthcare, government contractors — run Windows fleets and cannot send documents to US
SaaS under PDPA and sector rules, so they've simply opted out of AI rather than adopted it badly. That
is a forward-deployment market: the winner is whoever will sit in the customer's office and make the
governed path work, not whoever has the best API docs. We're already there, we speak the languages,
and our engineering cost base means we can serve a $40k on-prem deal profitably that a US vendor
can't be bothered to bid on. If YC wants us in the US for the batch, we go — the company can be
Delaware-topped with the engineering and first customers in KL/SG.

---

## Who writes code / other technical work?

I do — solo, assisted by Claude Code and Cursor. No non-founder has written production code. I hold
the architecture and the review gates; the models do the typing. All three codebases (Space, AirGPT,
Cortex) are mine.

---

## Are you looking for a cofounder?

Yes. Two specific gaps.

A technical cofounder who is a real validator — solid software engineering fundamentals, genuinely
into machine learning, reads papers and can tell me when a claim in my own architecture doc is
overselling the code. Not someone to type faster than the models; someone to be wrong with me in the
right direction.

A go-to-market cofounder for SEA forward deployment — comfortable sitting in a customer's office,
running a pilot, and turning it into a contract.

The bar for both is the same: can you run several agents and a large queue of unfinished work without
being overwhelmed by it. Hard to find someone who shares this specific vision, so I've kept building
rather than waiting.

---

## Previous batch / pivot

First application. No pivot — the wedge has been the same since the first line of code: Windows users
have been promised AI and handed a chat box that doesn't know what's on their screen.

*(Note: your draft says "I think my idea is perfect." Cut that. YC reads it as un-coachable. Say what
you've learned instead.)*

---

## Incubators

None. No accelerator, pre-accelerator, or program commitments.

---

## Equity

- Legal entity: [Yes/No — if a Malaysian Sdn Bhd or Singapore Pte Ltd exists, list it with the
  formation country. If none, say "None yet" — that is a fine answer.]
- Ownership: Jian Hong Oo, CEO — 100%. No employees, no other stockholders, no option pool yet.
- Investment taken: None.
- Currently fundraising: [Yes/No].

---

## Other ideas considered

*(This box is worth filling — YC has funded people out of it.)* Candidates from your own roadmap:

- **OmniRoute BYO-key gateway** — one endpoint over the free tiers a user legitimately holds, with a
  live budget meter, so AI keeps working when a subscription lapses.
- **Netie Talkie / Netie AIM** — the consumer AI hardware line on netie.ai.
- **Vanguard / Closer** — the signal-intelligence and autonomous-negotiation agents.
- Cortex as a standalone **audit-and-masking gateway in front of Huawei Cloud / Ascend** for sovereign
  deployments.

---

## What convinced you to apply?

[Honest and specific: who told you, which YC essay or company made you think this was fundable, any
YC event you attended. If nobody encouraged you and you found it yourself, say that — it's fine.]

---

## Things to fix before you submit

1. **Complete your founder profile** — the form blocks submission on it.
2. **Record the one-minute founder video** — under 100 MB. Just you, to camera: who you are, what you
   built, why you. No slides.
3. **Record a demo** — this is your strongest asset and it costs nothing. Screen recording, under 3
   minutes: press Space on a PDF, ask it a question, sign it, press Space on a video, trim and caption
   it, then flip Business mode on and show Cortex blocking a payload with NRIC + an injection string.
   That last 20 seconds is the whole company.
4. **Fill the 50-char description** — it's a hard blocker.
5. Fix the typos in the free-text boxes. Several answers currently have five or six each. It reads as
   carelessness, not speed.
6. If you mention free-tier fallback anywhere, frame it as **bring-your-own-key**. Your own whitepaper
   already rejects auto-creating provider accounts, and an investor will probe it.
