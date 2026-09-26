// A-009, K12: exportiert den OpenAPI-Vertrag aus der API (src/backend/CompanyHero.Api/Contracts/openapi.json).
// Der Host startet auf einem zufälligen lokalen Port, schreibt den Vertrag und endet; keine Datenbankverbindung nötig.
// Danach: npm run api:generate (Client) und in CI prüft check-generated-client.mjs, dass Client und Vertrag zusammenpassen;
// der Integrationstest OpenApiContractTests prüft, dass der eingecheckte Vertrag dem Export entspricht.
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { resolve, dirname } from 'node:path';

const frontend = dirname(dirname(fileURLToPath(import.meta.url)));
const project = resolve(frontend, '..', 'backend', 'CompanyHero.Api');
const target = resolve(project, 'Contracts', 'openapi.json');
const configuration = process.env.CH_CONFIGURATION ?? 'Release';

const result = spawnSync(
  'dotnet',
  ['run', '--project', project, '-c', configuration, '--', '--export-openapi', target],
  {
    stdio: 'inherit',
    shell: process.platform === 'win32',
    env: { ...process.env, ConnectionStrings__Default: process.env.ConnectionStrings__Default ?? 'Host=localhost;Database=export;Username=export;Password=export', DOTNET_NOLOGO: '1' },
  },
);
if (result.status !== 0) {
  console.error('OpenAPI-Export fehlgeschlagen.');
  process.exit(result.status ?? 1);
}
console.log(`OpenAPI-Vertrag exportiert: ${target}`);
