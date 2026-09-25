// Abhängigkeitsgrenze (Integrationsregeln Abschnitt 6): keine konkurrierenden UI-Pakete (A-003), kein zweites
// State- oder Query-Framework (A-004, K11), keine Palettenableitung im Frontend (A-013, K01), kein zweiter Styling-Build (K04).
// Ergänzt die ESLint-Importregeln um eine Prüfung der package.json, damit auch ungenutzte Installationen auffallen.
import { readFileSync } from 'node:fs';

const pkg = JSON.parse(readFileSync(new URL('../package.json', import.meta.url), 'utf8'));
const installed = Object.keys({ ...pkg.dependencies, ...pkg.devDependencies });

const forbidden = [
  { pattern: /^(@ng-bootstrap\/|bootstrap$|primeng|primeicons|@ionic\/|ng-zorro-antd|@nebular\/|@clr\/|@taiga-ui\/|@ng-select\/|ngx-bootstrap|@angular\/material-experimental)/, reason: 'Angular Material ist die alleinige UI-Bibliothek (A-003).' },
  { pattern: /^(tailwindcss|@tailwindcss\/|postcss-preset-env|sass-loader|unocss|windicss|styled-components|@emotion\/)/, reason: 'Kein Utility-CSS-System und kein zusätzlicher Styling-Buildschritt (A-003, K04).' },
  { pattern: /^(@ngrx\/|@ngxs\/|@tanstack\/|akita$|@datorama\/|elf$|@ngneat\/elf|rxjs-state|mobx)/, reason: 'Kein zusätzliches State- oder Query-Framework (A-004, K11).' },
  { pattern: /^(@material\/material-color-utilities|culori|chroma-js|color$|colord|tinycolor2|polished|d3-color|color-convert|wcag-contrast|apca-w3)$/, reason: 'Palettenableitung und Kontrastprüfung erfolgen im Backend (A-013, K01).' },
  { pattern: /^(@ngx-formly\/|ngx-formly|@rxweb\/|angular-signals-form)/, reason: 'Typed Reactive Forms besitzen den Formularzustand (K10).' },
  { pattern: /^(firebase|@sentry\/|@datadog\/|newrelic|posthog-js|mixpanel|@amplitude\/|@segment\/|onesignal)/, reason: 'Keine externen Telemetrie-, Analyse- oder Push-Dienste (A-026, A-059).' },
];

const findings = installed.flatMap((name) => forbidden.filter((f) => f.pattern.test(name)).map((f) => `${name}: ${f.reason}`));

if (findings.length > 0) {
  console.error('Unzulässige Frontend-Abhängigkeiten:\n  ' + findings.join('\n  '));
  process.exit(1);
}

const material = pkg.dependencies['@angular/material'];
const cdk = pkg.dependencies['@angular/cdk'];
const core = pkg.dependencies['@angular/core'];
const major = (v) => String(v).replace(/^[\^~]/, '').split('.')[0];
if (!material || !cdk || major(material) !== major(core) || major(cdk) !== major(core)) {
  console.error(`Material (${material}) und CDK (${cdk}) müssen derselben Major-Version wie Angular (${core}) folgen (A-002, K18).`);
  process.exit(1);
}

console.log(`Abhängigkeiten geprüft: ${installed.length} Pakete, keine verbotenen; Material/CDK ${material} zu Angular ${core}.`);
