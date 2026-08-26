# Parked tickets this agent cannot finish

These stay parked on purpose. Do not start P7.

## Cryptographic PDF signatures (Tier C)

Code is in: PDF More... -> **Cert sign (PFX)...** (`PdfCmsSigner`,
`SignedCms`, no iText). Visual **Sign** stamp is unchanged.

What you do:

1. Pull `main`. Make a test PFX: `.\scripts\make-test-pfx.ps1` (password `test`).
2. Open a PDF (xref-stream and hybrid trailer+/Type /XRef are rewritten
   to classic xref first).
3. More... -> Cert sign (PFX)... Pick `netie-test.pfx`. Password `test`.
4. Open the same file in Adobe Reader. Signature panel should list a
   signature. A self-signed test cert shows as untrusted, not "unsigned".
   The first page should show a small "Digitally signed" box (page Annots +
   appearance). Untrusted is OK; missing from the Signature panel is not.
   Space already ran a CMS self-check (`TryVerify`) before writing.
5. If sign fails, Space restores the original bytes. Stamp path still works.

Cert sign rewrites xref-stream and hybrid files first (`PdfClassicXref`): drop the
incremental `/Type /XRef` tail (Chrome/Word), then PdfSharp if needed. PdfSharp alone
fails on those hybrids (`no trailer found`). Leftover hybrid still throws; original
bytes stay. Cursor CI rewrite+signs both shapes; Adobe Reader panel is still human.

## ComfyUI / Windows AI

Code-ready local client: `ComfyUiClient` talks to `http://127.0.0.1:8188`
(override `NETIE_COMFY_URL`). Probe GET /system_stats (~1.2s). Down ->
toast/status only; rembg / Copy still work. Live GPU still human.

Click path (image preview):

1. Space an image.
2. Image ribbon -> Send to Comfy...
3. If 8188 is down: status/toast, no hang, no crash. rembg / Copy still work.
4. If up: POST `/upload/image`, then a LoadImage + SaveImage workflow.
   Space GETs `/view` and writes `{stem}.comfy.png` next to the source, then
   reloads that file so Copy / drag-out work like rembg.
   If upload/queue/download fails, write `{file}.comfy-input.json` next to
   the image (not on success) and run that workflow in ComfyUI. Upload uses
   the file's image Content-Type (`image/png`, `image/jpeg`, ...), not
   `application/octet-stream`. History poll waits for an image in
   `/history/{id}` outputs. `status.completed` with empty outputs is not
   done (Comfy can mark completed before SaveImage lands).

What you do on a Windows GPU:

1. Install ComfyUI. Confirm `http://127.0.0.1:8188` loads.
2. Click Send to Comfy... on an image. Confirm `{stem}.comfy.png` (or sidecar).
3. Copy / drag-out the reloaded output like rembg.
4. Keep rembg / Copy as the offline path when Comfy is down.

What to check: Space still previews if Comfy is offline; no hang; output
image can Copy / drag-out like rembg.

Windows AI (same image ribbon): **Windows AI...** probes WinML / Windows AI
without a WinAppSDK package. Down -> toast/status + `{file}.winai-input.json`.
rembg / Copy still work. Live enhance / Copilot+ GPU is still human.

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
