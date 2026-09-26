# CompanyHero

Modulare Multi-Tenant-PWA für betriebliche Gesundheitsförderung in Österreich: white-labelbar je Firma, mit firmenübergreifender Arena und nutzungsbasierter Abrechnung.

Das verbindliche Konzept steht in [concept/](concept/README.md); alle Entscheidungen im [Entscheidungsregister](concept/architektur-entscheidungen.md). Der Ordner `concept/` enthält ausschließlich den gültigen Stand, ohne Historie und ohne Zwischenergebnisse. [design/](design/mockups/README.md) enthält Gestaltungsvorlagen für die Umsetzung; sie sind Vorlage, keine Entscheidung.

## Technischer Durchstich

Die Umsetzung folgt dem [technischen Durchstich](concept/technischer-durchstich.md) (A-106) in acht Stufen. Je Stufe liegt ein Abnahmeprotokoll unter [durchstich/abnahme/](durchstich/abnahme/); die fixierten Versionen stehen in [durchstich/versionen.md](durchstich/versionen.md). CI bei jedem Push auf jedem Branch (A-030); Stufen 1 bis 5 entstanden direkt auf `master`, ab Stufe 6 arbeitet jede Stufe in einem eigenen Branch `stufe-<n>-<name>` und wird am Ende auf `master` gemergt; Images werden nur aus `master` nach GHCR gebracht.

| Stufe | Inhalt | Protokoll |
|---|---|---|
| 1 Fundament | Repository, CI, Compose, Images, Migrations-Container, OpenBao, Caddy, Beobachtung, Backup mit Wiederherstellung | [stufe-1.md](durchstich/abnahme/stufe-1.md) |
| 2 Isolation | zwei Tenants, RLS mit Laufzeitrechten, Tenant-Kontext je Request und Job, Modulschemata, Sichtbarkeitsregel | [stufe-2.md](durchstich/abnahme/stufe-2.md) |
| 3 Queue und Idempotenz | logische Queue, Outbox-Kopplung, Worker-Replikate, Vorgangskennung und Idempotenzschlüssel | [stufe-3.md](durchstich/abnahme/stufe-3.md) |
| 4 Zugang | Beitritt, Passkey, Wiederherstellungscode, Magic-Link, OIDC, Kiosk, Sitzungen, Austritt | [stufe-4.md](durchstich/abnahme/stufe-4.md) |
| 5 Vertrag und Oberfläche | OpenAPI-Client, Referenzscreen, Marken, Großflächenmodus, Ladebudget, Manifest | [stufe-5.md](durchstich/abnahme/stufe-5.md) |
| 6 Fachpfad | Organisation, Kickoff-Challenge, Beiträge, Fortschritt, Feed, Sichtbarkeit, Push und E-Mail | [stufe-6.md](durchstich/abnahme/stufe-6.md) |
| 7 Geld | Ledger, Slots, Kostenvorschau, Testphase, Rechnungsentwurf | offen |
| 8 Onboarding | Beitritt unter 90 Sekunden, Programmstart-Checkliste, Rollout-Fortschritt | offen |

## Aufbau des Repositories

| Pfad | Inhalt |
|---|---|
| `src/backend/` | .NET-10-Solution `CompanyHero.slnx`: `CompanyHero.Platform` (Querschnitte: Tenant-Kontext je Request und Job, Kontexttransaktion je Scope mit RLS, logische Job-Queue und Zeitpläne, Fachereignis-Zustellung, Metering-Emission als Schnittstelle, Hosting, Konfiguration aus OpenBao), `CompanyHero.Modules.*` (ein Projekt je Domäne der [Domänenkarte](concept/domaenen-und-schnittmengen.md), innen `Domain`, `Application`, `Infrastructure`, `Api`; eigenes Schema und eigener DbContext), `CompanyHero.ModuleCatalog`, Hosts `CompanyHero.Api`, `CompanyHero.Worker`, `CompanyHero.Migrations` (alle Kontexte, RLS in Migrationen) |
| `src/frontend/` | Angular-Workspace mit Material; Feature-Einstiege Mitglieder-App, Verwaltung und Kiosk aus einer Codebasis; Lint-Grenzen aus den [Integrationsregeln](concept/architektur-integrationsregeln.md) |
| `tests/` | Fachtests ohne Infrastruktur, Architekturtests (Abhängigkeitsrichtung, Modulgrenzen), Integrationstests gegen PostgreSQL 18 in Testcontainern mit denselben Rollen wie in Compose |
| `deploy/compose/plattform/` | Compose-Projekt Plattform: Caddy, API, Worker, Migrations-Container, PostgreSQL mit pgBackRest, Garage, OpenBao |
| `deploy/compose/beobachtung/` | Compose-Projekt Beobachtung: OpenTelemetry Collector, Prometheus, Loki, Tempo, Grafana, Uptime Kuma |
| `deploy/scripts/` | Geheimnisse anlegen, Deployment mit Rollback, Garage-Einrichtung, Betriebsnachweise, Wiederherstellungsübung |
| `.github/workflows/` | CI bei jedem Push, wöchentlicher Neubau der Images |
| `durchstich/` | Abnahmeprotokolle je Stufe, fixierte Versionen |
| `arbeit/`, `concept-entwurf-v0.1/` | Arbeitsprodukte und früherer Entwurf, nicht verbindlich, nicht versioniert |

Modulnamen im Code sind englisch: Identity (Zugang und Identität), Organisation, Branding (Marke und Theme), Entitlements, Privacy (Datenschutz und Nachweis), Progress (Fortschritt), Challenges, Feed, Notifications (Benachrichtigungen), Metering (Metering und Abrechnung).

## Einstiegspunkte

```bash
dotnet build CompanyHero.slnx -c Release
```

```bash
dotnet test CompanyHero.slnx -c Release
```

Die Integrationstests brauchen einen Docker-Host (Testcontainers). Frontend aus `src/frontend/`:

```bash
npm ci && npm run lint && npm run lint:styles && npm run test:ci && npm run build && npm run ladebudget && npx playwright install chromium && npm run e2e
```

## API-Vertrag und generierter Client (A-009, A-109)

Der Vertrag entsteht aus der Endpunktregistrierung der API und liegt eingecheckt unter `src/backend/CompanyHero.Api/Contracts/openapi.json`; der TypeScript-Client wird offline daraus erzeugt und nie von Hand geändert. Nach jeder Änderung an Endpunkten oder DTOs, aus `src/frontend/`:

```bash
npm run api:export      # startet die API kurz (kein Datenbankzugriff) und schreibt Contracts/openapi.json
npm run api:generate    # openapi-typescript -> src/libs/api-client/generated/api.ts
```

Die Integrationstests (`OpenApiContractTests`) scheitern, wenn der eingecheckte Vertrag vom Export abweicht oder K13 verletzt (Kennungen und Dezimalwerte als Strings, Zeitpunkte mit `date-time`, Pflichtfelder); der CI-Schritt `check-generated-client.mjs` scheitert, wenn der generierte Client nicht zum Vertrag passt. Antworten werden mit `.Produces<T>()` typisiert, sonst bleibt der Client für diese Operation untypisiert. Vertragsproben (echte Antworten) liegen unter `src/frontend/e2e/fixtures/api/` und werden mit `CH_WRITE_SAMPLES=1 dotnet test tests/CompanyHero.Integration.Tests` neu aufgenommen.

Lokaler Stack (Linux oder WSL, aus dem Repository-Stamm):

```bash
bash deploy/scripts/bootstrap-secrets.sh && cp deploy/compose/plattform/.env.example deploy/compose/plattform/.env && docker compose -f deploy/compose/plattform/compose.yaml up -d --build --wait
```

Danach ist die App unter `https://localhost` erreichbar (internes Zertifikat). Betriebsnachweise und Wiederherstellungsübung: `bash deploy/scripts/compose-nachweise.sh` und `bash deploy/scripts/wiederherstellung.sh`.
