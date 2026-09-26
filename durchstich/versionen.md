# Fixierte Versionen des Durchstichs

Beim Projektaufbau festgelegt (A-002, Betrieb 4 Reproduzierbarkeit). Quellen der Wahrheit sind die Lockfiles und
Konfigurationsdateien; diese Liste dokumentiert sie an einem Ort. Änderungen laufen über Dependabot und CI.

## Backend

| Bestandteil | Version | Festgelegt in |
|---|---|---|
| .NET SDK | 10.0.401 (rollForward latestFeature) | `global.json` |
| .NET Laufzeit-Images | `mcr.microsoft.com/dotnet/aspnet:10.0`, `runtime:10.0`, `sdk:10.0` | `Dockerfile` |
| EF Core, Npgsql-Provider | 10.0.12, 10.0.3 | `Directory.Packages.props` |
| Npgsql, Npgsql.OpenTelemetry | 10.0.3 | `Directory.Packages.props` |
| OpenTelemetry (Hosting, OTLP, AspNetCore, Http, Runtime) | 1.19.1 / 1.19.0 | `Directory.Packages.props` |
| xUnit v3 auf Microsoft.Testing.Platform | 4.0.1 | `Directory.Packages.props`, `tests/Directory.Build.props` |
| Testcontainers.PostgreSql | 4.15.0 | `Directory.Packages.props` |
| NetArchTest.Rules | 1.3.2 | `Directory.Packages.props` |
| dotnet-ef | 10.0.12 | `.config/dotnet-tools.json` |
| Jobbibliothek | keine; eigene Umsetzung auf `SKIP LOCKED` mit Npgsql (A-108) | `src/backend/CompanyHero.Platform/Jobs/` |
| OIDC-Middleware | Microsoft.AspNetCore.Authentication.OpenIdConnect 10.0.12, Schemata je Anbieter zur Laufzeit registriert (A-111) | `Directory.Packages.props`, `src/backend/CompanyHero.Modules.Identity/Infrastructure/Oidc/` |
| WebAuthn | Fido2 (Fido2NetLib) 4.1.0 (A-111) | `Directory.Packages.props`, `src/backend/CompanyHero.Modules.Identity/Application/Passkeys/` |
| Data-Protection-Schlüssel | Microsoft.AspNetCore.DataProtection.EntityFrameworkCore 10.0.12, Tabelle `platform.data_protection_key` (A-007) | `Directory.Packages.props`, `src/backend/CompanyHero.Platform/Data/` |
| OpenAPI-Erzeugung | Microsoft.AspNetCore.OpenApi 10.0.12 (Microsoft.OpenApi 2.12.0), OpenAPI 3.1, Export über `--export-openapi` (A-109) | `Directory.Packages.props`, `src/backend/CompanyHero.Api/OpenApi/` |
| Theme-Ableitung | keine Bibliothek; eigene Portierung von material-color-utilities (Apache-2.0, TypeScript-Paket 0.4.0, Stand 5b3618b) (A-110) | `src/backend/CompanyHero.Modules.Branding/Domain/Color/` |
| Web Push, E-Mail, Aushang | keine Bibliothek; VAPID (ES256) und RFC 8291 (`aes128gcm`) mit `ECDsa`, `ECDiffieHellman`, `HKDF`, `AesGcm` aus .NET 10; SMTP über `System.Net.Mail`; eigener PDF-Schreiber (PDF 1.4) (A-112) | `src/backend/CompanyHero.Modules.Notifications/Application/` |
| Preisrechnung, Ledger und Slots | keine Bibliothek; `System.Decimal` mit `MidpointRounding.AwayFromZero`, `numeric(18,4)`/`numeric(18,2)` in PostgreSQL, HMAC-SHA256 (`System.Security.Cryptography`) für Zähler-Slots, Periodensalz mit Microsoft.AspNetCore.DataProtection 10.0.12 geschützt (A-113) | `src/backend/CompanyHero.Modules.Metering/Domain/`, `Application/` |
| Analyseregeln | `latest-recommended`, Warnungen als Fehler, Ausnahmen in `.editorconfig` | `Directory.Build.props` |

## Frontend

| Bestandteil | Version | Festgelegt in |
|---|---|---|
| Node, npm | 24.15.0, 11.12.1 | `package.json` (engines über Angular CLI), `Dockerfile` |
| Angular, Material, CDK, Service Worker | 22.2.0 | `src/frontend/package.json`, Lockfile |
| TypeScript | 6.0.x (Angular 22 verlangt >=6.0 <6.1) | Lockfile |
| Vitest | 5.0.2 | Lockfile |
| Playwright | 1.63.0 | Lockfile |
| ESLint, angular-eslint, typescript-eslint | 10.11.0, 22.5.0, 8.70.1 | `package.json` |
| Stylelint, stylelint-config-standard-scss | 17.15.0, 17.0.0 | `package.json` |
| Inter, Inter Tight, Material Symbols Rounded (selbst ausgeliefert) | fontsource 5.3.0 / 5.3.0 / 5.3.7 | `package.json` |
| OpenAPI-Clientgenerator | openapi-typescript 7.13.0 (A-109); Peer-Auflösung auf TypeScript 6.0 über `overrides` | `package.json`, Skript `api:generate` |
| QR-Code-Erzeugung (Beitrittslink, Kiosk-Kennung, Übertragung) | qrcode 1.5.4 (MIT), @types/qrcode 1.5.6; nur in Lazy-Chunks | `package.json`, `src/frontend/src/libs/ui/qr-code/` |
| @types/node (Tests, Skripte) | 24.10.1 (passend zur Node-Hauptversion 24; Dependabot-Vorschlag 26 nicht übernommen) | `package.json` |
| Push-Service-Worker | eigener Worker `public/push-sw.js` (kein Angular-Service-Worker für Push; Nutzlast nur Referenz, A-059) | `src/frontend/public/push-sw.js` |

Browsermatrix (K18): Angular 22 unterstützt die aktuelle und die vorherige Hauptversion von Chrome, Firefox, Edge und
Safari sowie iOS-Safari ab 16.4; Web Push in installierten PWAs setzt auf iOS 16.4 voraus (A-059). Ältere Browser erhalten
keine Unterstützung.

## Plattformkomponenten (A-029)

| Komponente | Image | Festgelegt in |
|---|---|---|
| PostgreSQL | `postgres:18` (18.6 zum Zeitpunkt der Fixierung) plus pgBackRest aus Debian trixie | `deploy/compose/plattform/postgres/Dockerfile` |
| Caddy | `caddy:2.11-alpine` | `Dockerfile` |
| Garage | `dxflrs/garage:v2.4.1` | `deploy/compose/plattform/compose.yaml` |
| OpenBao | `openbao/openbao:2.7.0` | `deploy/compose/plattform/compose.yaml` |
| OpenTelemetry Collector | `otel/opentelemetry-collector-contrib:0.161.0` | `deploy/compose/beobachtung/compose.yaml` |
| Prometheus | `prom/prometheus:v3.15.0` | ebd. |
| Loki | `grafana/loki:3.7.8` | ebd. |
| Tempo | `grafana/tempo:3.0.3` | ebd. |
| Grafana | `grafana/grafana:13.2.2` | ebd. |
| Uptime Kuma | `louislam/uptime-kuma:2.5.5` | ebd. |
| Trivy (CI) | `aquasec/trivy:0.74.0` | `.github/workflows/ci.yml` |
| GitHub Actions | checkout v7, setup-dotnet v6, setup-node v7, buildx v4, login v3, upload-artifact v7 (Dependabot-Vorschläge vom 26.09.2026 übernommen) | `.github/workflows/ci.yml`, `images-woechentlich.yml` |

Produktionsimages werden per Digest ausgerollt (Betrieb 4); die CI schreibt die Digests in die Job-Ausgaben und der
Deploy-Job in `deploy/compose/plattform/.env` des Zielhosts.
