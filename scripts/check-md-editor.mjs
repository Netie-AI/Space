// Offline markdown editor smoke. Keep eqToMark in sync with md-editor.html.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const htmlPath = path.join(root, 'src', 'NetieSpace', 'Assets', 'markdown', 'md-editor.html');
const html = fs.readFileSync(htmlPath, 'utf8');
const need = [
  'mdSelection', 'mdAsk', 'paintOutline', 'annotateSel', 'eqToMark',
  'prefers-color-scheme', 'id="side"', 'id="outline"', 'id="notes"',
  'btnAnnot', 'btnAsk', 'btnSide', 'noteIn', '<mark', 'demo'
];
const miss = need.filter((n) => !html.includes(n));
if (miss.length) {
  console.error('FAIL md-editor missing', miss.join(', '));
  process.exit(1);
}

function eqToMark(md) {
  const parts = String(md || '').split(/(```[\s\S]*?```|`[^`\n]+`)/);
  return parts.map((p, i) => i % 2 ? p : p.replace(/==([^=\n]+)==/g, '<mark>$1</mark>')).join('');
}

const sample = 'hi ==keep== and `==code==`\n```\n==fence==\n```\n';
const out = eqToMark(sample);
if (!out.includes('<mark>keep</mark>')) {
  console.error('FAIL eqToMark highlight', out);
  process.exit(1);
}
if (!out.includes('`==code==`') || !out.includes('==fence==')) {
  console.error('FAIL eqToMark skipped code', out);
  process.exit(1);
}

const lines = '# A\n\n## B\ntext\n### C\n#### D\n##### skip\n'.split('\n');
const heads = [];
lines.forEach((line) => {
  const m = /^(#{1,4})\s+(.+)$/.exec(line);
  if (m) heads.push(m[2]);
});
if (heads.join(',') !== 'A,B,C,D') {
  console.error('FAIL outline', heads);
  process.exit(1);
}

console.log('OK: md-editor hooks + eqToMark + outline H1-H4');
