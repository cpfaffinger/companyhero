// Ladebudget K17: höchstens 180 KB komprimiertes JavaScript im ersten Ladevorgang der Mitglieder-App.
// Angular-Budgets messen unkomprimierte Größen; diese Prüfung misst die gzip-komprimierte Summe der
// initialen Skripte aus index.html des Produktionsbuilds und scheitert oberhalb des Budgets.
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { gzipSync, brotliCompressSync } from 'node:zlib';

const budgetBytes = Number(process.env.CH_LADEBUDGET_BYTES ?? 180 * 1024);
const distDir = process.argv[2] ?? join('dist', 'companyhero-frontend', 'browser');
const index = readFileSync(join(distDir, 'index.html'), 'utf8');

// Initial geladen sind alle <script src> in index.html sowie deren statisch importierten Chunks.
const scriptSources = [...index.matchAll(/<script[^>]+src="([^"]+)"/g)].map((m) => m[1]);
if (scriptSources.length === 0) {
  console.error('Keine <script src> in index.html gefunden; Build unvollständig?');
  process.exit(2);
}

const seen = new Set();
const queue = [...scriptSources];
while (queue.length > 0) {
  const file = queue.shift();
  if (seen.has(file)) continue;
  seen.add(file);
  const source = readFileSync(join(distDir, file), 'utf8');
  // Statische Importe (import ... from "./chunk-XYZ.js") zählen zum initialen Ladevorgang, dynamische import() nicht.
  for (const m of source.matchAll(/(?:^|[;\s])import(?:[^;"']*?from)?\s*["'](\.\/[^"']+\.js)["']/g)) {
    queue.push(m[1].replace(/^\.\//, ''));
  }
}

let raw = 0;
let gzip = 0;
let brotli = 0;
const rows = [];
for (const file of seen) {
  const buf = readFileSync(join(distDir, file));
  const g = gzipSync(buf, { level: 9 }).length;
  const b = brotliCompressSync(buf).length;
  raw += buf.length;
  gzip += g;
  brotli += b;
  rows.push({ Datei: file, roh: buf.length, gzip: g, brotli: b });
}

console.table(rows);
console.log(`Initiales JavaScript: roh ${raw} B, gzip ${gzip} B, brotli ${brotli} B; Budget gzip ${budgetBytes} B`);

const otherJs = readdirSync(distDir).filter((f) => f.endsWith('.js') && !seen.has(f));
console.log(`Lazy geladene Skripte (nicht im Budget): ${otherJs.length} Datei(en), ${otherJs.reduce((s, f) => s + statSync(join(distDir, f)).size, 0)} B roh`);

if (gzip > budgetBytes) {
  console.error(`Ladebudget überschritten: ${gzip} B gzip > ${budgetBytes} B (K17). Eine Budgetänderung ist eine Registerentscheidung.`);
  process.exit(1);
}
