// Live glue: AcroForm widget + fill overlay stay wrap-relative under scroll/zoom/fit.
// Needs puppeteer-core (npm i in this folder) + python http.server on NETIE_VIEWER_BASE.
import puppeteer from 'puppeteer-core';
import fs from 'node:fs';

const CANDIDATES = [
  process.env.NETIE_BROWSER,
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Google/Chrome/Application/chrome.exe'
].filter(Boolean);
const EDGE = CANDIDATES.find(p => fs.existsSync(p));
if (!EDGE) { console.error('No Edge/Chrome - set NETIE_BROWSER'); process.exit(2); }
const BASE = process.env.NETIE_VIEWER_BASE || 'http://127.0.0.1:8519';
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
function near(a, b, eps = 0.004) {
  return a != null && b != null && Math.abs(a - b) < eps;
}

const browser = await puppeteer.launch({
  executablePath: EDGE, headless: 'new',
  args: ['--no-first-run', '--disable-gpu']
});
const page = await browser.newPage();
await page.setViewport({ width: 1280, height: 800, deviceScaleFactor: 1 });
await page.goto(`${BASE}/netie-viewer.html?file=__stress/acroform.pdf&cb=${Date.now()}`,
  { waitUntil: 'domcontentloaded' });
await page.waitForFunction(() => window.__netie?.sent?.some(m => m.type === 'ready'), { timeout: 25000 });
await page.waitForFunction(() => document.querySelector('.annotationLayer section'), { timeout: 15000 });

const glue = () => page.evaluate(() => window.__netie.debug().glue);
await page.evaluate(() => window.__netie.onHostMessage({ type: 'fitPage' }));
await sleep(400);
const fit = await glue();

await page.evaluate(() => { document.getElementById('viewer').scrollTop = 180; });
await sleep(200);
const scrolled = await glue();

await page.evaluate(() => window.__netie.onHostMessage({ type: 'setZoom', scale: 2.5 }));
await sleep(700);
const zoomed = await glue();

await page.evaluate(() => window.__netie.onHostMessage({ type: 'enterFillMode' }));
await sleep(150);
await page.evaluate(() => window.__netie.onHostMessage({
  type: 'seedFillTexts',
  texts: [{ page: 1, xPt: 72, yFromBottom: 400, wPt: 200, hPt: 24, fontPt: 12, text: 'glue', snapped: 'free' }]
}));
await sleep(200);
const filled = await glue();
await page.evaluate(() => { document.getElementById('viewer').scrollTop = 220; });
await sleep(200);
const fillScroll = await glue();

await browser.close();

const fail = [];
if (!fit?.widget) fail.push('no widget at fit');
if (!near(fit?.widget?.x, scrolled?.widget?.x) || !near(fit?.widget?.y, scrolled?.widget?.y))
  fail.push('widget flew on scroll');
if (!near(fit?.widget?.x, zoomed?.widget?.x) || !near(fit?.widget?.y, zoomed?.widget?.y))
  fail.push('widget flew on zoom 2.5');
if (filled?.annotPe !== 'none') fail.push('annot PE ' + filled?.annotPe + ' want none');
if (filled?.sectionPe !== 'auto') fail.push('section PE ' + filled?.sectionPe + ' want auto');
if (!filled?.fill) fail.push('no fill overlay');
if (!near(filled?.fill?.x, fillScroll?.fill?.x) || !near(filled?.fill?.y, fillScroll?.fill?.y))
  fail.push('fill overlay flew on scroll');

if (fail.length) {
  console.error('FAIL', fail, { fit, scrolled, zoomed, filled, fillScroll });
  process.exit(1);
}
console.log('OK: widget+fill glue under scroll/zoom/fit; fill-mode annot PE none');
process.exit(0);
