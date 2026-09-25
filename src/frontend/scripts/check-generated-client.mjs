// K12 / A-009: Der TypeScript-Client wird aus der OpenAPI-Beschreibung generiert und nie von Hand geändert.
// Solange kein Generator eingerichtet ist (Stufe 5), darf der Zielordner nicht existieren. Sobald er existiert,
// muss die Regeneration (npm run api:generate) ohne Unterschied durchlaufen; die Prüfung vergleicht den Inhalt.
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { execSync } from 'node:child_process';
import { join } from 'node:path';
import { createHash } from 'node:crypto';

const dir = new URL('../src/libs/api-client/generated/', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1');
const pkg = JSON.parse(readFileSync(new URL('../package.json', import.meta.url), 'utf8'));

function digest(root) {
  const hash = createHash('sha256');
  const walk = (d) => {
    for (const entry of readdirSync(d).sort()) {
      const p = join(d, entry);
      if (statSync(p).isDirectory()) walk(p);
      else { hash.update(p); hash.update(readFileSync(p)); }
    }
  };
  walk(root);
  return hash.digest('hex');
}

if (!existsSync(dir)) {
  if (pkg.scripts['api:generate']) {
    console.error('Generator ist konfiguriert, aber der generierte Client fehlt. npm run api:generate ausführen und einchecken.');
    process.exit(1);
  }
  console.log('Kein generierter Client vorhanden; Generator folgt in Stufe 5 (Vertrag und Oberfläche).');
  process.exit(0);
}

if (!pkg.scripts['api:generate']) {
  console.error('Generierter Client vorhanden, aber kein Skript api:generate; manuelle Pflege ist unzulässig (K12).');
  process.exit(1);
}

const before = digest(dir);
execSync('npm run -s api:generate', { stdio: 'inherit' });
const after = digest(dir);
if (before !== after) {
  console.error('Der eingecheckte generierte Client weicht von der Regeneration ab; manuelle Änderungen sind unzulässig (K12).');
  process.exit(1);
}
console.log('Generierter Client entspricht der Regeneration.');
