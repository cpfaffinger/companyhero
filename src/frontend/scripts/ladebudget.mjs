// Ladebudget K17: höchstens 180 KB komprimiertes JavaScript im ersten Ladevorgang der Mitglieder-App, gemessen am
// Referenzscreen (Frontend 6). Angular-Budgets messen unkomprimierte Größen; diese Prüfung misst die gzip-komprimierte
// Summe der initialen Skripte aus index.html des Produktionsbuilds plus der Routen-Chunks, die der Referenzscreen
// (/t/{tenant}/challenges: Mitglieder-Schale und Challenges-Seite) beim ersten Aufruf nachlädt, und scheitert oberhalb des
// Budgets. Verwaltungs- und Zugangs-Chunks dürfen nicht im initialen Ladevorgang liegen.
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { gzipSync, brotliCompressSync } from 'node:zlib';

const budgetBytes = Number(process.env.CH_LADEBUDGET_BYTES ?? 180 * 1024);
const distDir = process.argv[2] ?? join('dist', 'companyhero-frontend', 'browser');
const index = readFileSync(join(distDir, 'index.html'), 'utf8');

// Routen-Chunks des Referenzscreens (namedChunks: Dateiname beginnt mit dem Modulnamen).
const referenceRoutePrefixes = ['mitglieder.routes-', 'challenges-page-', 'challenges-kontext-'];
// Diese Einstiege dürfen nie zum ersten Ladevorgang der Mitglieder-App gehören (K17, A-096).
const forbiddenInitialPrefixes = ['marke-page-', 'kiosk-page-', 'zugang'];

// Initial geladen sind alle <script src> in index.html sowie deren statisch importierten Chunks.
const scriptSources = [...index.matchAll(/<script[^>]+src="([^"]+)"/g)].map((m) => m[1]);
if (scriptSources.length === 0) {
  console.error('Keine <script src> in index.html gefunden; Build unvollständig?');
  process.exit(2);
}

const allJs = readdirSync(distDir).filter((f) => f.endsWith('.js'));
const routeChunks = referenceRoutePrefixes.map((prefix) => allJs.find((f) => f.startsWith(prefix))).filter(Boolean);
const missingRoutes = referenceRoutePrefixes.filter((prefix) => !allJs.some((f) => f.startsWith(prefix)));
if (missingRoutes.length > 0) {
  console.error(`Routen-Chunks des Referenzscreens nicht gefunden (namedChunks erwartet): ${missingRoutes.join(', ')}`);
  process.exit(2);
}

function closure(entries) {
  const seen = new Set();
  const queue = [...entries];
  while (queue.length > 0) {
    const file = queue.shift();
    if (seen.has(file)) continue;
    seen.add(file);
    const source = readFileSync(join(distDir, file), 'utf8');
    // Statische Importe (import ... from "./chunk-XYZ.js") zählen zum Ladevorgang, dynamische import() nicht.
    for (const m of source.matchAll(/(?:^|[;\s])import(?:[^;"']*?from)?\s*["'](\.\/[^"']+\.js)["']/g)) {
      queue.push(m[1].replace(/^\.\//, ''));
    }
  }
  return seen;
}

const initial = closure(scriptSources);
const reference = closure([...scriptSources, ...routeChunks]);

for (const file of reference) {
  if (forbiddenInitialPrefixes.some((p) => file.startsWith(p))) {
    console.error(`Unzulässiger Einstieg im Ladevorgang des Referenzscreens: ${file} (K17, A-096)`);
    process.exit(1);
  }
}

function measure(files) {
  let raw = 0;
  let gzip = 0;
  let brotli = 0;
  const rows = [];
  for (const file of files) {
    const buf = readFileSync(join(distDir, file));
    const g = gzipSync(buf, { level: 9 }).length;
    const b = brotliCompressSync(buf).length;
    raw += buf.length;
    gzip += g;
    brotli += b;
    rows.push({ Datei: file, roh: buf.length, gzip: g, brotli: b });
  }
  return { raw, gzip, brotli, rows };
}

const initialMeasure = measure(initial);
const referenceMeasure = measure(reference);
console.table(referenceMeasure.rows);
console.log(`Initiales JavaScript (App-Shell): roh ${initialMeasure.raw} B, gzip ${initialMeasure.gzip} B, brotli ${initialMeasure.brotli} B`);
console.log(`Referenzscreen (App-Shell plus Routen-Chunks ${routeChunks.join(', ')}): roh ${referenceMeasure.raw} B, gzip ${referenceMeasure.gzip} B, brotli ${referenceMeasure.brotli} B; Budget gzip ${budgetBytes} B`);

const otherJs = allJs.filter((f) => !reference.has(f));
console.log(`Lazy geladene Skripte außerhalb des Referenzscreens (nicht im Budget): ${otherJs.length} Datei(en), ${otherJs.reduce((s, f) => s + statSync(join(distDir, f)).size, 0)} B roh: ${otherJs.join(', ')}`);

if (referenceMeasure.gzip > budgetBytes) {
  console.error(`Ladebudget überschritten: ${referenceMeasure.gzip} B gzip > ${budgetBytes} B (K17). Eine Budgetänderung ist eine Registerentscheidung.`);
  process.exit(1);
}
