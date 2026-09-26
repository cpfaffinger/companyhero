// Statischer Server für die Ende-zu-Ende-Tests: liefert den Produktionsbuild wie Caddy (try_files → index.html, A-032);
// /api/* antwortet 404, damit nur ausdrücklich in den Tests hinterlegte Vertragsproben als API-Antworten dienen.
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { extname, join, normalize } from 'node:path';

const port = Number(process.argv[2] ?? 4310);
const root = join(process.cwd(), 'dist', 'companyhero-frontend', 'browser');
const types = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json',
  '.woff2': 'font/woff2',
  '.ico': 'image/x-icon',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
};

createServer(async (request, response) => {
  const url = new URL(request.url ?? '/', `http://127.0.0.1:${port}`);
  if (url.pathname.startsWith('/api/')) {
    response.writeHead(404, { 'content-type': 'application/problem+json' });
    response.end(JSON.stringify({ status: 404, title: 'Nicht hinterlegt' }));
    return;
  }
  const relative = normalize(decodeURIComponent(url.pathname)).replace(/^([/\\])+/, '');
  let file = join(root, relative);
  try {
    const info = await stat(file);
    if (info.isDirectory()) {
      file = join(root, 'index.html');
    }
  } catch {
    file = join(root, 'index.html');
  }
  try {
    const body = await readFile(file);
    const type = types[extname(file)] ?? 'application/octet-stream';
    response.writeHead(200, { 'content-type': type, 'cache-control': file.endsWith('index.html') ? 'no-cache' : 'public, max-age=3600' });
    response.end(body);
  } catch {
    response.writeHead(500);
    response.end();
  }
}).listen(port, '127.0.0.1', () => console.log(`Produktionsbuild unter http://127.0.0.1:${port} (${root})`));
