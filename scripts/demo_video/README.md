# Netie Space — short demo video

Generates a ~16.5s 1080p product promo:

1. Brand open  
2. Desktop + Space key  
3. Preview + AI pane  
4. Finale CTA  

## Render

```bash
pip install pillow cairosvg   # cairosvg optional if brand.png already exists
python3 scripts/demo_video/render_demo.py
```

Writes `/opt/cursor/artifacts/netie-space-demo.mp4` and a poster PNG.

Uses Inter + JetBrains Mono from the system font paths when present (macOS-style installs under `/usr/share/fonts/truetype/`), falling back to `scripts/demo_video/fonts/` if you vendor them. Brand mark comes from `brand.png` or is rasterized from `tests/fixtures/preview/Brand_mark.svg`.

`index.html` is an optional CSS storyboard of the same beats.
