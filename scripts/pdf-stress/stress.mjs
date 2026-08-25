// Netie PDF viewer stress harness — drives netie-viewer.html in headless Edge/Chrome.
// Usage: see README.md (serve Assets/pdfjs on :8517, then `node stress.mjs all`)
import puppeteer from 'puppeteer-core';
import fs from 'node:fs';

const CANDIDATES = [
  process.env.NETIE_BROWSER,
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Google/Chrome/Application/chrome.exe'
].filter(Boolean);
const EDGE = CANDIDATES.find(p => fs.existsSync(p));
if (!EDGE) { console.error('No Edge/Chrome found — set NETIE_BROWSER'); process.exit(2); }
const BASE = process.env.NETIE_VIEWER_BASE || 'http://localhost:8517';
const OUT = new URL('.', import.meta.url).pathname.replace(/^\//, '');
const results = [];
const t = (name, pass, detail = '') => {
  results.push({ name, pass, detail });
  console.log(`${pass ? 'PASS' : 'FAIL'}  ${name}${detail ? ' — ' + detail : ''}`);
};

const sleep = (ms) => new Promise(r => setTimeout(r, ms));

async function freshPage(browser, tag) {
  const page = await browser.newPage();
  await page.setViewport({ width: 1400, height: 900, deviceScaleFactor: 1 });
  page.on('pageerror', e => console.log('PAGEERROR:', e.message));
  await page.goto(`${BASE}/netie-viewer.html?file=__stress/test.pdf&cb=${tag}${Date.now()}`,
    { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(
    () => window.__netie && window.__netie.sent.some(m => m.type === 'ready'),
    { timeout: 20000 });
  await sleep(400);
  return page;
}

const dbg = (page) => page.evaluate(() => window.__netie.debug());
const sent = (page, type) => page.evaluate(ty => window.__netie.sent.filter(m => m.type === ty), type);

// CSS coords of a PDF point (origin bottom-left) on page n
async function cssOfPt(page, n, xPt, yPt) {
  return page.evaluate(({ n, xPt, yPt }) => {
    const wrap = document.querySelector(`.page-wrap[data-page="${n}"]`);
    const c = wrap.querySelector('canvas');
    const r = c.getBoundingClientRect();
    return { x: r.left + (xPt / 612) * r.width, y: r.top + ((792 - yPt) / 792) * r.height, cw: r.width };
  }, { n, xPt, yPt });
}

async function sigDataUrl(page) {
  return page.evaluate(() => {
    const c = document.createElement('canvas');
    c.width = 360; c.height = 120;
    const g = c.getContext('2d');
    g.fillStyle = '#102030';
    g.font = 'italic 64px "Segoe Script", cursive';
    g.fillText('jianhong', 18, 78);
    return c.toDataURL('image/png');
  });
}

async function placeStamp(page, n, xPt, yPt) {
  const dataUrl = await sigDataUrl(page);
  await page.evaluate(nn => window.__netie.onHostMessage({ type: 'gotoPage', page: nn }), n);
  await sleep(450);
  await page.evaluate(d => window.__netie.onHostMessage({ type: 'enterStampMode', dataUrl: d }), dataUrl);
  const p = await cssOfPt(page, n, xPt, yPt);
  await page.mouse.move(p.x - 40, p.y - 40);
  await page.mouse.move(p.x, p.y, { steps: 4 });
  await page.mouse.down();
  await page.mouse.up();
  await sleep(250);
}

// Coordinate click — puppeteer's page.click() rejects position:fixed bars
async function clickSel(page, sel) {
  const r = await page.evaluate(s => {
    const el = document.querySelector(s);
    if (!el) return null;
    const b = el.getBoundingClientRect();
    return { x: b.x + b.width / 2, y: b.y + b.height / 2, w: b.width };
  }, sel);
  if (!r || !r.w) throw new Error(`clickSel: ${sel} not visible`);
  await page.mouse.click(r.x, r.y);
}

async function overlayRect(page, idx = 0) {
  return page.evaluate(i => {
    const els = document.querySelectorAll('.stamp-overlay');
    if (!els[i]) return null;
    const r = els[i].getBoundingClientRect();
    return { x: r.x, y: r.y, w: r.width, h: r.height, cx: r.x + r.width / 2, cy: r.y + r.height / 2 };
  }, idx);
}

async function reloadKeep(page, v) {
  const before = (await sent(page, 'ready')).length;
  await page.evaluate(vv => window.__netie.onHostMessage(
    { type: 'reloadPdf', fileUrl: `__stress/test.pdf?v=${vv}`, keepOverlays: true }), v);
  await page.waitForFunction(
    b => window.__netie.sent.filter(m => m.type === 'ready').length > b, { timeout: 20000 }, before);
  await sleep(350);
}

async function phase1(browser) {
  console.log('\n== PHASE 1: stamps / ghost / reload ==');
  let page = await freshPage(browser, 'p1');

  // S0: all visible pages actually rendered (renderGen starvation check)
  const widths = await page.evaluate(() =>
    [...document.querySelectorAll('.page-wrap canvas')].map(c => Math.round(parseFloat(c.style.width))));
  const allEq = widths.every(w => Math.abs(w - widths[0]) <= 1);
  t('S0 initial render: all pages same scale (no starved placeholders)', allEq, `widths=${widths.join(',')}`);

  // S1: place stamp on page 1 empty referee row (Name cell centre ~ (112, 600))
  await placeStamp(page, 1, 112, 600);
  let d = await dbg(page);
  const pend = await sent(page, 'stampPending');
  t('S1 place: exactly 1 overlay', d.domOverlays === 1 && d.overlays.length === 1,
    `dom=${d.domOverlays} arr=${d.overlays.length}`);
  t('S1 place: stampPending posted with sane geometry',
    pend.length === 1 && pend[0].page === 1 && pend[0].stampW > 20 && pend[0].xPt >= 0,
    JSON.stringify(pend[0] ?? {}));

  // S2: drag by +80css,+40css and verify pt delta ≈ css/cw*612
  let r0 = await overlayRect(page);
  const o0 = (await dbg(page)).overlays[0];
  await page.mouse.move(r0.cx, r0.cy);
  await page.mouse.down();
  await page.mouse.move(r0.cx + 80, r0.cy + 40, { steps: 6 });
  await page.mouse.up();
  await sleep(250);
  const o1 = (await dbg(page)).overlays[0];
  const moved = await sent(page, 'stampMoved');
  const { cw } = await cssOfPt(page, 1, 0, 0);
  const dxPt = 80 / cw * 612;
  t('S2 drag: xPt advanced by ~drag distance', Math.abs((o1.xPt - o0.xPt) - dxPt) < 3,
    `dxPt=${(o1.xPt - o0.xPt).toFixed(1)} expected≈${dxPt.toFixed(1)}`);
  t('S2 drag: stampMoved posted', moved.length >= 1, `count=${moved.length}`);
  t('S2 drag: still exactly 1 overlay', (await dbg(page)).domOverlays === 1);

  // S3: zoom in/out/fit — pt geometry stable, still 1 overlay
  await page.click('#zoomIn'); await sleep(500);
  await page.click('#zoomIn'); await sleep(500);
  const oz = (await dbg(page)).overlays[0];
  t('S3 zoom in: pt geometry unchanged', Math.abs(oz.xPt - o1.xPt) < 0.5 && Math.abs(oz.stampW - o1.stampW) < 0.5,
    `xPt ${o1.xPt}→${oz.xPt}`);
  await page.click('#fit'); await sleep(600);
  d = await dbg(page);
  t('S3 fit: still 1 overlay, geometry stable', d.domOverlays === 1 && Math.abs(d.overlays[0].xPt - o1.xPt) < 0.5);

  // S4: scroll — overlay tracks its page, float bar follows or hides, no dupes
  await page.evaluate(() => { document.getElementById('viewer').scrollTop += 300; });
  await sleep(400);
  d = await dbg(page);
  t('S4 scroll: still 1 overlay', d.domOverlays === 1, `dom=${d.domOverlays}`);

  // S5: THE GHOST — 3 rapid keepOverlays reloads (autosave storm)
  await reloadKeep(page, 1);
  await reloadKeep(page, 2);
  await reloadKeep(page, 3);
  d = await dbg(page);
  t('S5 reload×3 keepOverlays: exactly 1 overlay (no ghosts)',
    d.domOverlays === 1 && d.overlays.length === 1, `dom=${d.domOverlays} arr=${d.overlays.length}`);
  t('S5 reload: geometry preserved', Math.abs(d.overlays[0].xPt - o1.xPt) < 0.5,
    `xPt ${o1.xPt}→${d.overlays[0]?.xPt}`);
  t('S5 reload: overlay committed + connected', d.overlays[0]?.committed && d.overlays[0]?.connected);

  // S6: float bar A+ / A− / delete (scroll the box into view first, like a user)
  await page.evaluate(() => {
    document.querySelector('.stamp-overlay')?.scrollIntoView({ block: 'center' });
  });
  await sleep(450);
  let rr = await overlayRect(page);
  await page.mouse.click(rr.cx, rr.cy); await sleep(200);
  const barVisible = await page.evaluate(() =>
    getComputedStyle(document.getElementById('stampFloatBar')).display !== 'none');
  t('S6 select: float bar visible', barVisible);
  const wBefore = (await dbg(page)).overlays[0].stampW;
  await clickSel(page, "#stampSizeUp"); await sleep(150);
  const wUp = (await dbg(page)).overlays[0].stampW;
  await clickSel(page, "#stampSizeDown"); await sleep(150);
  const wDown = (await dbg(page)).overlays[0].stampW;
  t('S6 A+ grows, A− shrinks', wUp > wBefore && wDown < wUp, `${wBefore.toFixed(1)}→${wUp.toFixed(1)}→${wDown.toFixed(1)}`);
  await clickSel(page, "#stampDeleteBtn"); await sleep(200);
  d = await dbg(page);
  const del = await sent(page, 'stampDeleted');
  t('S6 trash: overlay gone + stampDeleted posted', d.domOverlays === 0 && del.length === 1,
    `dom=${d.domOverlays} deleted=${del.length}`);

  // S7: two stamps, delete one
  await placeStamp(page, 1, 112, 600);
  await placeStamp(page, 1, 400, 300);
  d = await dbg(page);
  t('S7 two stamps placed', d.domOverlays === 2, `dom=${d.domOverlays}`);
  const rA = await overlayRect(page, 0);
  await page.mouse.click(rA.cx, rA.cy); await sleep(150);
  await clickSel(page, "#stampDeleteBtn"); await sleep(200);
  d = await dbg(page);
  t('S7 delete one: other remains', d.domOverlays === 1 && d.overlays.length === 1, `dom=${d.domOverlays}`);

  // S8: place on page 3, reload keepOverlays — do we stay on page 3?
  await page.evaluate(() => window.__netie.onHostMessage({ type: 'gotoPage', page: 3 }));
  await sleep(700);
  await placeStamp(page, 3, 150, 300);
  const pgBefore = (await dbg(page)).pageNum;
  await reloadKeep(page, 8);
  const pgAfter = (await dbg(page)).pageNum;
  t('S8 reload keeps reading position (no jump to page 1)', pgBefore === 3 && pgAfter === 3,
    `before=${pgBefore} after=${pgAfter}`);

  await page.screenshot({ path: OUT + 'shot_phase1_end.png' });
  await page.close();
}

async function fillPhase(browser) {
  console.log('\n== PHASE 2: Fill & Sign (cell detect + typing) ==');
  const page = await freshPage(browser, 'fill');
  const has = await page.evaluate(() => !!window.__netie.debug().fill);
  if (!has) { t('F0 fill mode present', false, 'fill not implemented in viewer yet'); await page.close(); return; }

  await page.evaluate(() => window.__netie.onHostMessage({ type: 'enterFillMode' }));
  await sleep(200);

  // F1: click centre of empty referee Name cell (50..175 x, 580..620 y) → detect that cell
  const p1 = await cssOfPt(page, 1, 112, 600);
  await page.mouse.click(p1.x, p1.y);
  await sleep(350);
  let f = (await dbg(page)).fill;
  const cell = f.fields[0];
  const ok1 = cell && Math.abs(cell.xPt - 50) < 4 && Math.abs(cell.wPt - 125) < 8 &&
    Math.abs(cell.yFromBottom - 580) < 4 && Math.abs(cell.hPt - 40) < 8;
  t('F1 cell detect: snapped to Name cell (50,580,125,40)', !!ok1,
    cell ? `got x=${cell.xPt.toFixed(1)} y=${cell.yFromBottom.toFixed(1)} w=${cell.wPt.toFixed(1)} h=${cell.hPt.toFixed(1)}` : 'no field');

  // F2: type text → value lands in state + posted to host
  await page.keyboard.type('LIM MEI LING', { delay: 15 });
  await page.mouse.click((await cssOfPt(page, 1, 300, 500)).x, (await cssOfPt(page, 1, 300, 500)).y - 200); // click away commits
  await sleep(600);
  f = (await dbg(page)).fill;
  const posted = await sent(page, 'fillTexts');
  t('F2 typing stored', f.fields[0]?.text === 'LIM MEI LING', `text="${f.fields[0]?.text}"`);
  t('F2 fillTexts posted to host', posted.length >= 1 && posted.at(-1).texts?.[0]?.text === 'LIM MEI LING',
    `posts=${posted.length}`);

  // F3: float bar on active field — A+ / A− / trash
  const p2 = await cssOfPt(page, 1, 112, 600);
  await page.mouse.click(p2.x, p2.y); await sleep(250);
  const fb = await page.evaluate(() => {
    const b = document.getElementById('fillFloatBar');
    return b && getComputedStyle(b).display !== 'none';
  });
  t('F3 float bar visible on active field', !!fb);
  const s0 = (await dbg(page)).fill.fields[0].fontPt;
  await clickSel(page, "#fillSizeUp"); await sleep(120);
  const s1 = (await dbg(page)).fill.fields[0].fontPt;
  await clickSel(page, "#fillSizeDown"); await sleep(120);
  const s2 = (await dbg(page)).fill.fields[0].fontPt;
  t('F3 A+ grows font, A− shrinks', s1 > s0 && s2 < s1, `${s0}→${s1}→${s2}`);

  // F4: no cell lines at blank area → free text box fallback
  const p3 = await cssOfPt(page, 3, 300, 500);
  await page.evaluate(() => window.__netie.onHostMessage({ type: 'gotoPage', page: 3 }));
  await sleep(600);
  await page.mouse.click(p3.x, (await cssOfPt(page, 3, 300, 500)).y);
  await sleep(300);
  f = (await dbg(page)).fill;
  t('F4 blank area: free text box fallback created', f.fields.length === 2, `fields=${f.fields.length}`);
  await page.keyboard.type('free note');

  // F5: reload keepOverlays preserves fill fields + stamps coexist
  await placeStamp(page, 3, 150, 300);
  await sleep(300);
  await reloadKeep(page, 21);
  const d5 = await dbg(page);
  t('F5 reload: fills + stamp survive', d5.fill.fields.length === 2 && d5.domOverlays === 1,
    `fills=${d5.fill.fields.length} stamps=${d5.domOverlays}`);

  // F6: trash deletes field
  await page.evaluate(() => window.__netie.onHostMessage({ type: 'gotoPage', page: 1 }));
  await sleep(500);
  const p6 = await cssOfPt(page, 1, 112, 600);
  await page.mouse.click(p6.x, p6.y); await sleep(200);
  await clickSel(page, "#fillDeleteBtn"); await sleep(300);
  const d6 = await dbg(page);
  t('F6 trash deletes field', d6.fill.fields.length === 1, `fields=${d6.fill.fields.length}`);

  // F7: OCR/host seedFillTexts drops editable boxes at PDF points
  await page.evaluate(() => window.__netie.onHostMessage({
    type: 'seedFillTexts',
    texts: [{ page: 1, xPt: 50, yFromBottom: 100, wPt: 120, hPt: 18, fontPt: 11, text: 'OCR LINE', snapped: 'ocr' }]
  }));
  await sleep(250);
  const d7 = await dbg(page);
  const seeded = d7.fill.fields.find(f => f.text === 'OCR LINE');
  const seedPosts = await sent(page, 'fillTexts');
  t('F7 seedFillTexts creates editable OCR box',
    !!seeded && Math.abs(seeded.xPt - 50) < 1 && Math.abs(seeded.yFromBottom - 100) < 1,
    seeded ? `x=${seeded.xPt} y=${seeded.yFromBottom}` : 'missing');
  t('F7 seedFillTexts posts fillTexts to host',
    seedPosts.some(m => (m.texts || []).some(x => x.text === 'OCR LINE')));

  await page.screenshot({ path: OUT + 'shot_fill_end.png' });
  await page.close();
}

async function highlightPhase(browser) {
  console.log('\n== HIGHLIGHT: overlay + host message + reload keep ==');
  const page = await freshPage(browser, 'hl');

  await clickSel(page, '#hlBtn');
  await sleep(120);
  const on = await page.evaluate(() => window.__netie.debug().highlightMode);
  t('H0 Highlight button arms markup mode', !!on);

  await page.evaluate(nn => window.__netie.onHostMessage({ type: 'gotoPage', page: nn }), 3);
  await sleep(500);
  const a = await cssOfPt(page, 3, 60, 700);
  const b = await cssOfPt(page, 3, 360, 660);
  await page.mouse.move(a.x, a.y);
  await page.mouse.down();
  await page.mouse.move(b.x, b.y, { steps: 8 });
  await page.mouse.up();
  await sleep(250);

  const msgs = await sent(page, 'highlight');
  const d1 = await dbg(page);
  t('H1 drag posts highlight with yFromBottom',
    msgs.length >= 1 && msgs[0].page === 3 && msgs[0].wPt > 10 && Number.isFinite(msgs[0].yFromBottom),
    JSON.stringify(msgs[0] ?? {}));
  t('H1 session overlay stored', d1.highlights.length === 1 && d1.domHighlights >= 1,
    `arr=${d1.highlights.length} dom=${d1.domHighlights}`);

  await reloadKeep(page, 31);
  const d2 = await dbg(page);
  t('H2 reload keepOverlays restores highlight',
    d2.highlights.length === 1 && Math.abs(d2.highlights[0].wPt - d1.highlights[0].wPt) < 0.5,
    `arr=${d2.highlights.length}`);

  await page.evaluate(() => window.__netie.onHostMessage({ type: 'persistFailed', message: 'disk locked' }));
  const st = await page.evaluate(() => document.getElementById('status').textContent);
  const d3 = await dbg(page);
  t('H3 persistFailed keeps session overlay', d3.highlights.length === 1, `arr=${d3.highlights.length}`);
  t('H3 persistFailed status is honest', /Save failed/i.test(st || '') && /kept/i.test(st || ''), st || '');

  await page.screenshot({ path: OUT + 'shot_highlight_end.png' });
  await page.close();
}

const mode = process.argv[2] || 'all';
const browser = await puppeteer.launch({
  executablePath: EDGE,
  headless: 'new',
  args: ['--no-first-run', '--disable-gpu', '--hide-scrollbars']
});
try {
  if (mode === 'phase1' || mode === 'all') await phase1(browser);
  if (mode === 'fill' || mode === 'all') await fillPhase(browser);
  if (mode === 'highlight' || mode === 'all') await highlightPhase(browser);
} finally {
  await browser.close();
}
const fails = results.filter(r => !r.pass);
console.log(`\n${results.length - fails.length}/${results.length} passed`);
fs.writeFileSync(OUT + 'stress_results.json', JSON.stringify(results, null, 2));
process.exit(fails.length ? 1 : 0);
