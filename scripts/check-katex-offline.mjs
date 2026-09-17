// Offline KaTeX smoke — no network. Loads vendored MIT katex.min.js.
import fs from 'node:fs';
import vm from 'node:vm';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const dir = path.dirname(fileURLToPath(import.meta.url));
const katexJs = path.resolve(dir, '../src/NetieSpace/Assets/markdown/katex/katex.min.js');
if (!fs.existsSync(katexJs)) {
  console.error('FAIL missing', katexJs);
  process.exit(1);
}
const code = fs.readFileSync(katexJs, 'utf8');
const module = { exports: {} };
const ctx = {
  module,
  exports: module.exports,
  window: {},
  self: {},
  console,
};
ctx.window = ctx;
ctx.self = ctx;
vm.runInNewContext(code, ctx);
const katex = ctx.katex || module.exports;
if (!katex || typeof katex.renderToString !== 'function') {
  console.error('FAIL katex.renderToString missing', Object.keys(katex || {}));
  process.exit(1);
}
const html = katex.renderToString('x^2+\\frac{1}{2}', { throwOnError: false, displayMode: true });
if (!html.includes('katex') || !html.includes('frac')) {
  console.error('FAIL render', html.slice(0, 200));
  process.exit(1);
}
console.log('OK: offline KaTeX renderToString');
