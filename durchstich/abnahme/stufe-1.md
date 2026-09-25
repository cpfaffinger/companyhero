# Abnahmeprotokoll Stufe 1 – Fundament

**Stufe:** 1 Fundament gemäß [technischer-durchstich.md](../../concept/technischer-durchstich.md), Abschnitt 3.
**Nachweise aus:** Betrieb 9.1, 9.4, 9.5, 9.7; ergänzend Backend 9 (TDD, Testcontainer mit Laufzeitrechten), Integrationsregeln 6 (Lint-Grenzen ab Stufe 1), A-012 (Architekturtests), A-030 (Lieferkette).
**Umgebung:** GitHub-Actions-Runner als frische Umgebung gemäß A-107; zusätzlich lokale Läufe unter WSL (Docker CE 29).
**Stand:** wird mit dem grünen CI-Lauf abgeschlossen; siehe Abschnitt 4.

## 1. Was Stufe 1 liefert

- Backend-Solution auf .NET 10 (`CompanyHero.slnx`): Plattforminfrastruktur, zehn Modulprojekte entlang der Domänenkarte, Modulkatalog, Hosts API und Worker aus einem Image, Migrations-Container mit eigener Datenbankrolle.
- Datenbankrollen: `ch_migrator` besitzt Schemata und führt Migrationen aus; `ch_app` (Laufzeit) ohne Superuser, ohne `BYPASSRLS`, ohne `CREATE`, nur Datenrechte auf den Modulschemata. Dasselbe Init-Skript in Compose und in den Testcontainern.
- Angular-22-Workspace mit Material, `strict` und `strictTemplates`, Lint-Grenzen aus den Integrationsregeln als ESLint-, Stylelint- und Abhängigkeitsprüfung, Ladebudget-Messung komprimiert.
- Compose-Projekte `plattform` und `beobachtung`, Caddy mit HSTS und CSP, OpenBao mit KV und Transit, Garage, pgBackRest mit WAL-Archivierung, Grafana mit Pflicht-Dashboard und Alarmen.
- CI mit Build, Tests in Testcontainern, Architekturtests, Lint, Trivy, Betriebsnachweisen in Compose, Wiederherstellungsübung, Push nach GHCR mit Digest; wöchentlicher Neubau; Dependabot.
- Register: A-107 (Durchstich-Umgebungen ohne bereitgestellte Hosts). Versionen: [versionen.md](../versionen.md).

## 2. Nachweise

| Nr. | Nachweis aus dem Konzept | Test oder Prüfung | Ergebnis | Lauf |
|---|---|---|---|---|
| 1 | Betrieb 9.1, A-031: Neuaufbau aus Images, Konfiguration und Backups innerhalb RTO 4 h, Datenstand innerhalb RPO 15 min | `deploy/scripts/wiederherstellung.sh`: Produktion mit Basissicherung und WAL-Archiv, Datenvolumes zerstört, Staging aus Repository wiederhergestellt, Zeilen geprüft, RTO und RPO gemessen | siehe Abschnitt 3 | CI-Job „Wiederherstellungsübung“ |
| 2 | Betrieb 9.4: fehlerhafte Migration bricht vor dem Anwendungsstart ab, Produktion unverändert | Integrationstest `Fehlerhafte_Migration_bricht_ab_und_hinterlaesst_keinen_Teilzustand`; Compose-Nachweis: Migrations-Container gegen sabotierte Datenbank endet mit Fehler, `compose up api` startet die Anwendung nicht (`depends_on: service_completed_successfully`) | siehe Abschnitt 3 | CI-Jobs „Backend“, „Images“ |
| 3 | Betrieb 9.4: Rollback auf vorherigen Digest ohne Datenbank-Rollback | `deploy/scripts/deploy.sh` mit einem Image, das nicht gesund wird: Gesundheitsprüfung scheitert, `.env.previous` wird zurückgespielt, Anwendung wieder gesund, Migrationsstand bleibt (Release-Zeile „kaputt“ vorhanden) | siehe Abschnitt 3 | CI-Job „Images“ |
| 4 | Betrieb 9.5: Datenbank, Garage, OpenBao, Grafana von außen nicht erreichbar | `compose ps` zeigt nur `web` mit veröffentlichten Ports; TCP-Verbindungen zu 5432, 3900, 3903, 8200, 3000, 9090, 3100, 8080 scheitern | siehe Abschnitt 3 | CI-Job „Images“ |
| 5 | Betrieb 9.7: Logs ohne Personenbezug | Integrationstest `Logs_enthalten_weder_Query_noch_Cookie_noch_Authorization`; Compose-Nachweis: Container-Logs von api, worker, web, postgres nach Anfragen mit E-Mail in der Query, Cookie und Authorization enthalten keinen der Werte; Caddy-Zugriffslog ohne Query, Cookie, Client-IP; Collector entfernt entsprechende Attribute | siehe Abschnitt 3 | CI-Jobs „Backend“, „Images“ |
| 6 | Backend 5.1 Nr. 4: Laufzeitrolle ohne Besitz und ohne `BYPASSRLS`, Migrationen mit eigenem Zugang | Integrationstests `Laufzeitrolle_hat_weder_Superuser_noch_BypassRls_noch_Besitz`, `Laufzeitrolle_kann_Daten_lesen_und_schreiben_aber_keine_Objekte_anlegen`, `Migrationslauf_protokolliert_den_Release_Stand`, `Zweiter_Migrationslauf_ist_idempotent` | grün | CI-Job „Backend“ |
| 7 | A-012, Domänenkarte 7: Architekturtests scheitern bei Projektreferenz außerhalb der erlaubten Richtung | `DependencyDirectionTests`: Matrix je Projekt, jedes Backendprojekt erfasst, kein Modul referenziert Hosts oder Katalog, `*.Domain` ohne EF Core und ASP.NET Core | grün (18 Tests) | CI-Job „Backend“ |
| 8 | Integrationsregeln 6, A-076: Lint-Grenzen ab Stufe 1 | ESLint: Rohfarbe `'#9B1B3A'` in TypeScript, `FormsModule`, `chroma-js` jeweils abgelehnt (lokal geprüft); Stylelint: `color: #ff0000` abgelehnt, `.mat-*`, `::ng-deep`, `!important` verboten; `check-dependencies.mjs` prüft package.json; `check-generated-client.mjs` bereit für Stufe 5 | grün | CI-Job „Frontend“ |
| 9 | K17: Ladebudget 180 KB komprimiert, gemessen | `scripts/ladebudget.mjs` am Produktionsbuild; Referenzscreen folgt in Stufe 5 | 58,9 KB gzip initial (Gerüst) | CI-Job „Frontend“ |
| 10 | A-030: CI bei jedem Push, Trivy blockiert kritische Funde, Images mit Digest in GHCR | Workflow `ci.yml`; Trivy `--severity CRITICAL --exit-code 1`; Push mit Version und Digest | siehe Abschnitt 4 | CI-Lauf |
| 11 | A-029: OpenBao liefert Anwendungsgeheimnisse, Transit vorhanden | `openbao-init` richtet KV `companyhero`, Transit `companyhero-transit`, Policy und Token ein; API liest `app/database` beim Start (Compose-Nachweis „Anwendung liest Geheimnis aus OpenBao“) | siehe Abschnitt 3 | CI-Job „Images“ |

## 3. Läufe

Die Protokolle der einzelnen Läufe (`compose-nachweise-*.md`, `wiederherstellung-*.md`) entstehen bei jedem CI-Lauf als Artefakt. Die für diese Abnahme maßgeblichen Läufe:

| Lauf | Umgebung | Ergebnis | Protokoll |
|---|---|---|---|
| Wiederherstellungsübung, lokal | WSL Debian 13, Docker CE 29, lokal gebaute Images | bestanden; RTO 21 s, RPO 0 s, 2 von 2 Fachzeilen, 1 von 1 Release-Zeilen | [wiederherstellung-lokal-20260925.md](laeufe/wiederherstellung-lokal-20260925.md) |
| Betriebsnachweise in Compose, lokal | WSL Debian 13, Docker CE 29, lokal gebaute Images, Ports 8088/8443 | bestanden; 41 Prüfungen | [compose-nachweise-lokal-20260925.md](laeufe/compose-nachweise-lokal-20260925.md) |
| Wiederherstellungsübung, CI | GitHub-Actions-Runner (A-107), Images per Digest aus GHCR | siehe Abschnitt 4 | Artefakt `wiederherstellung` des CI-Laufs |
| Betriebsnachweise in Compose, CI | GitHub-Actions-Runner (A-107), lokal geladene Images des Commits | siehe Abschnitt 4 | Artefakt `compose-nachweise` des CI-Laufs |

## 4. CI-Lauf

_Wird nach dem ersten grünen CI-Lauf ergänzt._

## 5. Roter Ausgangspunkt (TDD, Backend 9)

Vor der Implementierung des Migrationslaufs und der Gesundheitsendpunkte schlugen die Integrationstests aus fachlichen Gründen fehl (Auszug des lokalen Laufs vom 25.09.2026):

```
fehlerhaft ApiHostTests.Live_und_Ready_antworten_mit_200_wenn_die_Datenbank_erreichbar_ist  Expected: OK  Actual: NotFound
fehlerhaft ApiHostTests.Version_liefert_den_Release_Stand_als_JSON                          404 (Not Found)
fehlerhaft MigrationsAndRolesTests.Migrationslauf_protokolliert_den_Release_Stand           42P01: relation "platform.release" does not exist
fehlerhaft MigrationsAndRolesTests.Laufzeitrolle_kann_Daten_lesen_und_schreiben_...         3F000: schema "platform" does not exist
fehlerhaft MigrationsAndRolesTests.Fehlerhafte_Migration_bricht_ab_...                      Expected: Not 0  Actual: 0
gesamt: 10, fehlgeschlagen: 7, erfolgreich: 3
```

Nach der kleinsten Implementierung (Migrationslauf mit Rechtevergabe und Release-Zeile, Gesundheits- und Versionsendpunkte): 28 von 28 Tests grün.

## 6. Offene Punkte und Entscheidungsvorschläge

1. **Repository-Sichtbarkeit.** A-030 verlangt ein privates GitHub-Repository; `cpfaffinger/companyhero` ist öffentlich. Vorschlag: auf privat stellen (Betreibereinstellung) oder A-030 anpassen. Nicht vom Durchstich geändert.
2. **Hosts.** Wiederherstellungsübung und Betriebsnachweise laufen gemäß A-107 auf dem CI-Runner. Nachweise 9.3 (Alarme innerhalb 5 Minuten) und 9.8 (Zertifikatserneuerung) brauchen die Hosts.
3. **Backup-Repository.** pgBackRest verlangt TLS für S3; Garage im internen Netz bietet kein TLS. Für lokale Läufe und die Übung dient ein posix-Volume als Offsite-Stellvertreter; in Produktion ist das Ziel der S3-Speicher des Betreibers am zweiten Standort (Konfiguration über `secrets/backup-repo.env`). Kein Registerbedarf, weil A-031 den Offsite-Speicher als Betreibervorgabe nennt.
4. **Garage ohne Root.** Das Garage-Image läuft mit seinem Standardbenutzer; ein Betrieb ohne Root wird beim Anschluss der Medienverarbeitung (Feed und Inhalte) nachgezogen.
5. **Backup-Metriken.** Alarm „Kein erfolgreiches Backup in 26 Stunden“ ist vorbereitet; die Metrik entsteht mit dem Backup-Exporter, sobald der Worker in Stufe 3 Metriken emittiert.
