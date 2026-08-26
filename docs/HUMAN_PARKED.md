# Parked tickets this agent cannot finish

These stay parked on purpose. Do not start P7.

## Cryptographic PDF signatures (Tier C)

Stamp in the PDF ribbon is visual only. Real cert signing needs a licensed
PDF library (iText AGPL is not allowed in this repo).

What you do:

1. Pick a license you accept (paid iText, or another commercial SDK).
2. Add the NuGet on a Windows machine and wire Sign to embed a PKCS#7
   signature, not only a stamp image.
3. Test: open the signed PDF in Adobe Reader. Signature panel must show
   valid (or valid with your test cert), not "unsigned appearance".
4. Keep the existing stamp path for people without a cert.

What to check: file still opens if signing fails; original bytes untouched
on error (same rule as highlight persist).

## ComfyUI / Windows AI

Needs a Windows PC with a GPU and a local ComfyUI install. This Linux
cloud agent has neither.

What you do:

1. Install ComfyUI on Windows. Confirm `http://127.0.0.1:8188` loads.
2. Decide the Space hook: Image ribbon -> Send to Comfy (path + workflow
   JSON) or a watched output folder.
3. Keep rembg / Copy as the offline path when Comfy is down.

What to check: Space still previews if Comfy is offline; no hang; output
image can Copy / drag-out like rembg.

## P7 — do not start

- Rust/Go rewrite
- Google AI Overview scrape
- Higgsfield / Comfy generative video

Leave them unchecked.

## Playwright / "open the app locally"

Netie Space is `NetieSpace.exe` (WPF). Playwright drives browsers only.
There is no Playwright project in this repo.

On Windows: `install.bat` or `run.bat`, then
`dist\NetieSpace\NetieSpace.exe`. Click path:
`docs/HUMAN_PILLAR1_TEST.md`.
