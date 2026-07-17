# Netie Space × eSuite vision (working)

Local company toolkit: **Netie Space** = instant Space preview + CapCut-light video + Acrobat-light PDF + Office/LaTeX glance. **AirGPT** (`D:\AirGPT`) = deep AI workspace / vault / reasoning.

## Ponytail (token discipline)

From [ponytail](https://github.com/DietrichGebert/ponytail): *the best code/tokens are the ones you never spent.*

Netie applies that as:
- Local extractive summary before any API call
- Aggressive excerpt truncate + small models for huge docs
- Disk/memory summary cache
- Skip re-sending full document on repeat Look Up of same hash (see `docs/TOKEN_SAVINGS.md` when shipped)

## Ship order (this push)

1. Smooth video scrub (mute+pause seek) + centered DPI-safe window
2. CapCut-light bottom trim/split timeline
3. rembg Remove BG for images
4. PDF Split / Rotate / Sign stamp (PdfSharp)
5. AirGPT synergy doc + KaTeX in HTML preview
6. Keep P7 rewrite parked

Do not boil the ocean — each feature must stay Space-key fast.
