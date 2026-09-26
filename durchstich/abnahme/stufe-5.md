# Abnahmeprotokoll Stufe 5 – Vertrag und Oberfläche

**Stufe:** 5 Vertrag und Oberfläche gemäß [technischer-durchstich.md](../../concept/technischer-durchstich.md), Abschnitt 3: OpenAPI-Client, Referenzscreen mit zwei Firmenmarken und Plattformmarke, hell und dunkel, Großflächenmodus, Ladebudget, Manifest je Tenant.
**Nachweise aus:** Integrationsregeln 6; Frontend 7; Marke 9.1 bis 9.5 und 9.7; ergänzend Backend 8 und 11.8, Frontend 4 bis 6, Integrationsregeln K01 bis K13, K17, K19; Entscheidungen A-003, A-009, A-013, A-076 bis A-081; Produktwahlen A-109 und A-110.
**Umgebung:** Backend gegen PostgreSQL 18 in Testcontainern mit den Rollen des Compose-Projekts (Laufzeitrolle `ch_app`); Frontend als Produktionsbuild (Angular 22, `strict`, `strictTemplates`) hinter einem statischen Server mit App-Shell-Fallback wie Caddy, API-Antworten als eingecheckte Vertragsproben (echte Antworten der API aus dem Testcontainer-Lauf, `src/frontend/e2e/fixtures/api/`); Chromium (Playwright 1.63) in 390, 800 und 1280 px, hell und dunkel, reduzierte Bewegung. GitHub-Actions-Runner gemäß A-107; zusätzlich lokale Läufe unter Windows mit Docker in WSL.
**Stand:** abgenommen am 26.09.2026 mit dem grünen CI-Lauf 36241323942 (Abschnitt 6).

## 1. Was Stufe 5 liefert

- **Vertrag** (A-009, K12, K13): OpenAPI 3.1 aus der Endpunktregistrierung der API (`Microsoft.AspNetCore.OpenApi` 10.0.12) mit Schematransformer für Pflichtfelder und strikter Zahlenbehandlung; Export über `--export-openapi` beziehungsweise `npm run api:export` nach `src/backend/CompanyHero.Api/Contracts/openapi.json`, eingecheckt. Alle Antworten sind mit `.Produces<T>()` typisiert; neu: `GET /api/challenges` (Daten der Challenge-Karte), `percent` und `target` (Sammelziel) an Challenge und Kollektivstand, `GET /api/version` und Fortschritt typisiert.
- **Generierter Client** (A-109): `npm run api:generate` mit openapi-typescript 7.13.0 nach `src/frontend/src/libs/api-client/generated/api.ts`; generischer, typisierter Transport über Angular `HttpClient` (`api-client.ts`), keine manuell duplizierten DTOs. CI: `check-generated-client.mjs` (Regeneration ohne Unterschied) und `OpenApiContractTests` (Export gleich eingecheckte Datei; K13-Regeln).
- **Vertragsproben** (A-009 Nachweis „echte API-Antworten“): `ContractSamplesTests` nimmt zehn echte Antworten der API auf (Theme dreier Tenants, Plattformtheme, Manifest, Challenge-Karten, Kollektivstand, Beitrag angenommen und abgelehnt, Theme abgelehnt) und prüft die eingecheckten Proben gegen die laufende API in Struktur und Werttypen; `contract.spec.ts` prüft dieselben Proben gegen den generierten Client (Dezimalstrings mit vier Nachkommastellen, Kennungen als UUIDv7, Zeitpunkte mit Offset, Aufzählungen, `null`); die Ende-zu-Ende-Tests verwenden sie als API-Antworten.
- **Theme-Ableitung im Backend** (A-013, A-077, A-110): Modul Marke und Theme mit eigener Portierung des Material-Verfahrens (CAM16, HCT-Solver, Tonpaletten, Kontrast) und der Fachregel `ThemeDerivation` (`hct-m3/1`): Tonpalette der Saatfarbe, `progress` warm mit Farbtonabstand und nie Blau, Akzent mit Mindestabstand, WCAG 2.2 AA für alle Rollenpaare (4,5:1 Text, 3:1 Bedienelemente und Fortschrittsbahnen), Saatfarbe immer angenommen und nur bei fehlendem Kontrast durch den nächsten Ton derselben Farbe ersetzt, Ersetzungsliste mit Grund; plattformweit feste Rollen aus der Konfiguration des Operators (`Branding:Platform`).
- **Theme je Tenant** (A-077, Marke 3.2 bis 3.5): Theme-Dokument Schema 1 mit Backend-Validierung, Tabelle `branding.tenant_theme` (RLS, versioniert, zwölf Monate abrufbar), Endpunkte `GET /api/branding/theme`, `GET /api/branding/theme/versions/{n}`, `PUT /api/branding/theme` (Tenant-Admin), `POST /api/branding/theme/preview` (Vorschau ohne Persistierung mit Kontrastbericht), `GET /api/branding/platform` (Plattformmarke ohne Sitzung); Standard-Theme mit dem Anzeigenamen des Tenants, solange nichts veröffentlicht ist.
- **Manifest und Icons je Tenant** (A-079): `GET /api/branding/tenants/{tenantId}/manifest.webmanifest` mit `id`, `start_url`, `scope` unter `/t/{tenantId}/`, Name aus dem Produktnamen, Farben aus dem Tokensatz, Icons 192/512/maskierbar als PNG und SVG aus dem Bildzeichen der Plattform in Tenant-Farbe (eigener PNG-Kodierer, keine Bibliothek); Kiosk-Manifest im Vollbild; nur für den Tenant der Sitzung, fremde Pfade „nicht gefunden“.
- **Textkatalog** (A-080): eingebetteter Katalog des Operators mit drei Tonalitätsvarianten je Schlüssel, jede mit Du und Sie; Sperrliste und Ausrufezeichenregel als Prüfliste; das Theme liefert die Texte in Tonalität und Anrede des Tenants mit ersetzten Bezeichnungen; das Frontend schreibt keine Systemtexte (Vitest prüft jeden verwendeten Schlüssel gegen den Katalog und die Oberfläche gegen die Sperrliste).
- **Frontend** (A-002, A-003, K01 bis K11, K17): Theme-Service für `system | light | dark`, Tenant- oder Plattformkontext und Großflächenmodus, setzt Tokens, `color-scheme`, Manifestverweis und Titel gemeinsam; Material-Adapter über `mat.theme` und `mat.theme-overrides` (alle Farbtoken auf `--ch-*`), Bedienflächen über `*-overrides`; Design-System (4-px-Raster, Radien 8/12/16/24, Bewegung 120/200/320 ms, `prefers-reduced-motion`, Fokusring, Layoutgrenzen 640/1024 als eine Definition für SCSS und TypeScript); gemeinsames UI-Paket mit Schale (Leiste, Rail, Seitennavigation, Kontextspalte 320 px), Challenge-Karte, Kollektivbalken mit Meilensteinen, Fortschrittsring mit dem einen Erfolgsmoment, Kennzahlenkachel, Leerzustand, Kiosk-Anmeldemaske; Datenzugriff je Bereich mit geteilten Requests, ausdrückliche Abbildung Formular → DTO, Idempotenzschlüssel als UUIDv7 im Client; Seiten Start, Challenges (Referenzscreen), Ich, Kiosk (`/kiosk`, immer Großflächenmodus) und Verwaltung „Marke und Sprache“ (Vorschau und Veröffentlichen, eigener Lazy-Einstieg). Inter, Inter Tight und Material Symbols Rounded selbst ausgeliefert.
- **Tests:** 70 Fachtests ohne Infrastruktur (32 neu: HCT-Referenzwerte, Ableitung, Textkatalog), 39 Architekturtests, 68 Integrationstests mit Laufzeitrechten (9 neu: Vertrag, Vertragsproben, Theme, Manifest, Isolation), 27 Vitest-Komponententests, 28 Playwright-Abnahmen. Migrationen: `branding` (Initial mit RLS), `challenges` (SammelzielTarget); CI prüft acht Kontexte auf ausstehende Modelländerungen.
- **Register:** A-109 (Clientgenerator), A-110 (Theme-Ableitung); Nachweisstände A-003, A-009, A-013, A-076, A-077, A-079, A-080, A-081. Versionen: [versionen.md](../versionen.md).

## 2. Nachweise

| Nr. | Nachweis aus dem Konzept | Test oder Prüfung | Ergebnis | Lauf |
|---|---|---|---|---|
| 1 | Integrationsregeln 6: Referenzformular, Select und Dialog mit einheitlichem Theme, korrekten Fehler- und Fokuszuständen | Playwright `referenzscreen.spec.ts` (18 Fälle: drei Marken × hell/dunkel × 390/800/1280 px): Select im Fehlerzustand mit Serverfehler aus der Vertragsprobe (422 `InFuture` als Katalogtext), Textfeld gefüllt, Dialog offen; Screenshots `laeufe/stufe-5/<marke>-<modus>-<breite>.png` | grün | CI-Job „Frontend“ |
| 2 | Integrationsregeln 6, Marke 9.1, Backend 11.8: zwei Firmenmarken und Plattformmarke, hell und dunkel; gemeinsamer Wechsel einschließlich Overlays; Farbwerte identisch mit dem Backend-Tokensatz | Playwright: `--ch-primary` auf dem Wurzelelement, gefüllter Button (`primary`/`on-primary`), Prozentzahl (`progress`), Seitengrund (`surface`) und Dialogaktion gleich den Werten der Vertragsprobe je Modus; `BrandingThemeTests.Theme_Endpunkt_und_Ableitung_liefern_identische_Werte_wie_die_Fachregel`; Vorschau gleich Veröffentlichung (`Vorschau_liefert_dieselben_Werte…`); `theme.service.spec.ts` Wechsel Tenant ↔ Plattform | grün; PDF folgt in Stufe 6 mit demselben gespeicherten Tokensatz | CI-Jobs „Frontend“, „Backend“ |
| 3 | Integrationsregeln 6, K05: Systemmodus und expliziter Modus; Betriebssystemwechsel wirkt nur bei `system` | Playwright `Systemmodus wirkt nur bei system…` (Wechsel der Präferenz ändert Tokens nur bei `system`; expliziter Modus bleibt und ist gespeichert); `theme.service.spec.ts` | grün | CI-Job „Frontend“ |
| 4 | Integrationsregeln 6, K07: Layoutgrenzen 640 und 1024 px; untere Leiste, Rail 88 px, Seitennavigation 240 px mit Kontextspalte 320 px; kein horizontaler Überlauf | Playwright je Breite: gemessene Breite der Navigation und der Kontextspalte, `scrollWidth <= innerWidth`; `layout.spec.ts`: SCSS und TypeScript nennen dieselben Werte | grün | CI-Job „Frontend“ |
| 5 | Integrationsregeln 6, Marke 9.7, A-081: Standard- und Großflächenmodus; gemessene Bedienflächen 48 px und 56 px, Text zwei Stufen größer, Kiosk immer im Großflächenmodus | Playwright: Höhe der Buttons ≥ 48 px in allen 18 Fällen, ≥ 56 px im Großflächenmodus, Fließtext 16 → 20 px und Sekundärtext 14 → 18 px; `kiosk.spec.ts`: `data-ch-scale="gross"` erzwungen, Tasten des Ziffernblocks ≥ 56 px; Screenshots `wiesner-hell-390-gross.png`, `kiosk-wiesner-1024.png` | grün (Karte 48 px, Ziffernblock 72 px) | CI-Job „Frontend“ |
| 6 | Integrationsregeln 6, Marke 7 und 2.4: Tastatur und reduzierte Bewegung | Playwright: Sprungmarke als erstes Bedienelement führt in den Inhalt; Fokusring in `primary` mit 2 px Abstand; Dialog hält den Fokus (Tab kreist), Escape schließt und stellt den Fokus wieder her; mit `prefers-reduced-motion` sind alle Bewegungswerte 0 ms und die Füllanimation aus, ohne Einschränkung 320 ms Füllen und 120 ms Zustandswechsel | grün | CI-Job „Frontend“ |
| 7 | Integrationsregeln 6, K10, K13: Formularänderung und Submit; eine führende Eingabequelle, korrektes DTO, Serverfehler sichtbar; Erfolg erst nach Serverbestätigung | Playwright `Formular und Submit…` (gesendetes DTO: `value` als String, `channel` mobile, `operationId` null, UUIDv7-Schlüssel, Zeitpunkt mit Offset, Notiz nicht übertragen; Erfolgsmoment erst nach 201); `challenges.api.spec.ts` (Abbildung, gleicher Schlüssel bei Wiederholung, 422/409/Verbindung als stabile Schlüssel) | grün | CI-Job „Frontend“ |
| 8 | Integrationsregeln 6, A-009: API-Änderung und Dezimalwerte; erkennbare Vertragsbrüche, keine stillen Präzisionsverluste | `OpenApiContractTests` (Export gleich eingecheckte Datei; Kennungen und Dezimalwerte Strings, keine Gleitkommazahl, Ganzzahlen rein, Zeitpunkte `date-time`, Pflichtfelder, jede Operation typisiert); `check-generated-client.mjs`; `contract.spec.ts`; Gegenprobe in Abschnitt 3 | grün | CI-Jobs „Backend“, „Frontend“ |
| 9 | Integrationsregeln 6, K11: mehrere Verbraucher derselben Daten ohne doppelten Datenzugriff | `challenges.api.spec.ts` (drei Verbraucher, ein Request); Kontextspalte und Seite teilen `ChallengesApi` | grün | CI-Job „Frontend“ |
| 10 | Integrationsregeln 6, K17, Frontend 6, A-096: Produktionsbuild ohne Verwaltungs- und Zugangsfeatures im Einstieg; Ladebudget 180 KB komprimiert am Referenzscreen | `ladebudget.mjs`: App-Shell 91.630 B gzip; Referenzscreen (App-Shell plus Routen-Chunks `mitglieder.routes`, `challenges-page`) **178.531 B gzip** (174,3 KB; brotli 157.355 B) bei Budget 184.320 B; `marke-page`, `kiosk-page` und Zugang außerhalb des Referenzscreens, Prüfung schlägt bei ihrem Import an | grün, 5,7 KB unter dem Budget | CI-Job „Frontend“ |
| 11 | Marke 9.2, A-077: orange Saatfarbe ergibt Fortschrittsfarbe mit Farbtonabstand, kein Blau; alle Rollenpaare bestehen 4,5:1 beziehungsweise 3:1 in beiden Modi | `ThemeDerivationTests` (zehn Saatfarben einschließlich `#E4670A`, `#FFD700`, `#808080`, Schwarz, Weiß: 26 Rollenpaare je Modus; Fortschrittsfarbe im warmen Band mit ≥ 40° Abstand; Wiesner erhält einen anderen warmen Ton als die Plattform); `HctReferenceTests` (veröffentlichte Referenzwerte) | grün | CI-Job „Backend“ |
| 12 | Marke 9.3, A-013: ungültiges Theme führt zum Standard-Theme mit Grund | `BrandingThemeTests.Ungueltiges_Theme_wird_mit_Grund_abgelehnt…` (400 mit lesbaren Gründen, gültige Version bleibt), `Ohne_Veroeffentlichung_gilt_das_Standard_Theme…`; Vertragsprobe `theme-rejected.json`; Verwaltung zeigt die Gründe | grün | CI-Job „Backend“ |
| 13 | Marke 9.4, A-079: Manifest je Tenant mit Name und Symbol; zweiter Tenant eigener Name; Kiosk-Manifest im Vollbild | `BrandingThemeTests.Manifest_und_Icons_je_Tenant_nur_fuer_die_eigene_Sitzung` (`id`/`start_url`/`scope` je Tenant, `theme_color` aus dem Tokensatz, vier Icons mit korrekten PNG-Maßen, `fullscreen` für den Kiosk, fremder Tenant 404, ohne Sitzung 401); Playwright `Manifest je Tenant…` (Verweis, `theme-color`, Titel); Vertragsprobe `manifest-wiesner.json` | grün; Installation mit zwei Tenants auf einem Gerät: manuelle Prüfung auf Staging offen | CI-Jobs „Backend“, „Frontend“ |
| 14 | Marke 9.5, A-078: Wechsel Firma ↔ Plattform-Schale; Kopfzeile, Farben und Logo-Rolle wechseln, Sitzung unverändert | `theme.service.spec.ts` (Kontextwechsel ohne Verlust des Tenant-Themes, Manifestverweis nur im Tenant); Playwright `Ohne Sitzung…` (Anmeldeseite vor Zuordnung mit Bildzeichen im Standard-Theme, `plattform-schale-390.png`); Arena selbst nicht im Durchstich | grün (Schale); Overlay-Schluss beim Wechsel folgt mit der Arena | CI-Job „Frontend“ |
| 15 | Marke 9.8 (Vorgriff), A-076, Integrationsregeln 6 Lint-Grenzen: Rohfarbe in einer Komponente scheitert am Lint; kein Tenant-Schrift-Upload | Gegenprobe in Abschnitt 3 (Stylelint `color-no-hex`, ESLint `no-restricted-syntax`); Theme-Dokument kennt keine Schriften | grün | CI-Job „Frontend“ |
| 16 | A-080: Texte in Tonalität und Anrede des Tenants; Sperrliste | `BrandingThemeTests` (Version 2 mit Sie und sachlich wechselt Texte sofort, Version 1 abrufbar), `TextCatalogTests` (Sperrbegriff abgelehnt, Ausrufezeichenregel), `text.service.spec.ts` (jeder Oberflächenschlüssel im Katalog, keine Sperrbegriffe in Oberfläche und Katalog); Referenzscreen Wiesner „Dein Beitrag heute“, Hödl „Ihr Beitrag heute“ | grün | CI-Jobs „Backend“, „Frontend“ |
| 17 | Datenschutz 7, A-026: Routen mit Tenant-Pfad antworten fremdem Tenant „nicht gefunden“ | `BrandingThemeTests` (Manifest und Icon eines fremden Tenants 404, auch für dessen Tenant-Admin; unbekannter Icon-Name 404); RLS auf `branding.tenant_theme` (`Jede_Tabelle_eines_Modulschemas_hat_Row_Level_Security…`, 13 Tabellen) | grün | CI-Job „Backend“ |

## 3. Roter Ausgangspunkt und Gegenproben (TDD, Backend 9, K19)

Die Tests entstanden vor der Umsetzung und scheiterten aus fachlichem Grund: die HCT-Referenztests an fehlenden Klassen und dann an zu enger Rundung (Werte auf drei Nachkommastellen, Toleranz 0,001), die Ableitungstests an fehlenden Rollen und Kontrastpaaren, die Vertragstests an untypisierten Antworten (`GET /api/version`, Fortschritt) und an gemischten Typen (`integer | string` ohne strikte Zahlenbehandlung), die Playwright-Abnahme an einer leeren Kontextspalte (benannter Outlet ohne URL-Segment) und am nicht aufgelösten Titel. Drei Gegenproben am fertigen Stand (lokal, 26.09.2026), jeweils ein Schutzmechanismus entfernt:

**Ohne Kontrastschwelle** (`Passes` akzeptiert jedes Verhältnis ≥ 1:1; keine Ersetzung):

```
fehlerhaft ThemeDerivationTests.Orange_Saatfarbe_wird_angenommen_und_nur_dort_ersetzt_wo_sie_unlesbar_waere_mit_Grund
fehlerhaft ThemeDerivationTests.Alle_Rollenpaare_bestehen_in_beiden_Modi_und_alle_Rollen_sind_belegt(seed: "#E4670A")
  #E4670A: primary auf surface hat 3.19:1, gefordert 4.5:1
fehlerhaft …(seed: "#FFD700")   #FFD700: primary auf surface hat 1.33:1, gefordert 4.5:1
fehlerhaft …(seed: "#9B1B3A")   #9B1B3A: progress auf surface-container hat 2.92:1, gefordert 3:1
fehlerhaft …(seed: "#808080")   #808080: primary auf surface hat 3.75:1, gefordert 4.5:1
gesamt: 70, fehlgeschlagen: 11, erfolgreich: 59
```

**Rohfarbe in einer Komponente** (`color: #9b1b3a` im SCSS der Challenge-Karte, `'#9B1B3A'` im TypeScript):

```
src/libs/ui/challenge-card/challenge-card.scss
  96:10  ✖  Keine Rohfarben in Komponenten; nur --ch-* Rollen aus dem Backend-Tokensatz (A-076, K01).  color-no-hex
src/libs/ui/challenge-card/challenge-card.ts
  53:25  error  Keine Rohfarben im Frontend; Farben kommen als --ch-* aus dem Backend-Tokensatz (A-076, K01)  no-restricted-syntax
```

**Vertragsbruch** (`ChallengeCardResponse.Target` als `decimal` statt String, ohne neuen Export):

```
fehlerhaft OpenApiContractTests.Vertrag_folgt_K13_Kennungen_und_Dezimalwerte_als_Strings_Zeitpunkte_mit_Format_Ganzzahlen_rein
  ChallengeCardResponse.target: Dezimalwert muss string sein, ist number
  ChallengeCardResponse.target: binäre Gleitkommazahl im Vertrag (K13)
fehlerhaft OpenApiContractTests.Eingecheckter_Vertrag_entspricht_dem_Export_der_laufenden_API
  Der Vertrag src/backend/CompanyHero.Api/Contracts/openapi.json ist veraltet. Neu exportieren: npm run api:export (src/frontend) und den Client mit npm run api:generate regenerieren.
gesamt: 2, fehlgeschlagen: 2
```

Mit allen Mechanismen: 70 Fachtests, 39 Architekturtests, 68 Integrationstests, 27 Vitest-Tests und 28 Playwright-Abnahmen grün.

## 4. Produktwahlen

| Wahl | Entscheidung | Version |
|---|---|---|
| OpenAPI-Clientgenerator | openapi-typescript mit typisiertem Transport über Angular HttpClient (A-109) | openapi-typescript 7.13.0; Microsoft.AspNetCore.OpenApi 10.0.12, OpenAPI 3.1 |
| Theme-Ableitung | eigene Portierung des Material-Verfahrens im HCT-Farbraum (A-110) | material-color-utilities 0.4.0 (Apache-2.0), Repository-Stand 5b3618b; Verfahrenskennung `hct-m3/1` |

## 5. Gemessene Werte

| Größe | Wert |
|---|---|
| Ladebudget Referenzscreen (K17), gzip | 178.531 B (174,3 KB) von 184.320 B; brotli 157.355 B; roh 652.700 B |
| davon App-Shell (initiale Skripte) | 91.630 B gzip |
| Lazy außerhalb des Referenzscreens | `ich-page`, `kiosk-page`, `marke-page`, `start-page` |
| Bedienflächen | Standard 48 px (Material-Buttons über `*-overrides`), Großflächenmodus 56 px, Ziffernblock 72 px |
| Text im Großflächenmodus | Fließtext 20 px (statt 16), Sekundärtext 18 px (statt 14) |
| Bewegung | 120 / 200 / 320 ms; mit `prefers-reduced-motion` 0 ms und Animation aus |
| Kontrast | 26 Rollenpaare je Modus bestehen 4,5:1 beziehungsweise 3:1 für zehn Saatfarben (Fachtests) und für die drei Marken der Vertragsproben |
| Schriften (nicht im JS-Budget) | Inter 5.3.0, Inter Tight 5.3.0, Material Symbols Rounded 5.3.7 (`wght`-Achse, 966 KB woff2) |

## 6. CI-Lauf

| Feld | Wert |
|---|---|
| Lauf | [36241323942](https://github.com/cpfaffinger/companyhero/actions/runs/36241323942), Workflow `CI`, Push auf `master`, 26.09.2026 |
| Commit | `b22c663` „CI: Schrittname des Referenzscreens als YAML-Zeichenkette“ auf `caf196d` „Stufe 5 Abnahme: Referenzscreen in Playwright …“ und `179c945` „Stufe 5 Vertrag und Oberfläche …“; der Lauf auf `caf196d` (36241288984) scheiterte an der Workflow-Syntax (unmaskierter Doppelpunkt im Schrittnamen), nicht an einem Test, der Folgecommit ändert nur `ci.yml` |
| Jobs | Backend 1 min 9 s grün (70 Fachtests, 39 Architekturtests, 68 Integrationstests; acht Kontexte ohne ausstehende Modelländerungen); Frontend 1 min 25 s grün (30 Pakete ohne verbotene, generierter Client gleich der Regeneration, ESLint, Stylelint, 27 Vitest-Tests in 9 Dateien, Produktionsbuild, Ladebudget 178.531 B gzip von 184.320 B, 28 Playwright-Abnahmen in 26,3 s, 21 Screenshots als Artefakt); Images, Trivy, Betriebsnachweise, Push 5 min 6 s grün (Rollback auf vorherigen Digest, Anwendung gesund, Datenbank ohne Rollback; vier Images per Digest nach GHCR); Wiederherstellungsübung 1 min 26 s grün (RTO 7 s, RPO 2 s) |
| Tests | 232: 68 Integration (Testcontainer, Laufzeitrolle), 39 Architektur, 70 Fachtests, 27 Vitest, 28 Playwright |

Ergebnis je Nachweis aus Abschnitt 2: Nr. 1 bis 17 grün in diesem Lauf; Screenshots des Laufs als Artefakt `referenzscreen-screenshots`, die für diese Abnahme maßgeblichen Screenshots des lokalen Laufs unter [laeufe/stufe-5/](laeufe/stufe-5/).

## 7. Festlegungen dieser Stufe (kein Registerbedarf)

1. **Tenant-Kennzeichner in Pfaden.** A-079 verlangt den Tenant-Kennzeichner in `start_url` und `scope`; verwendet wird die Tenant-Kennung (UUIDv7) unter `/t/{tenantId}/…`. Sie ist opak und ohne Personenbezug (A-026); der Pfad benennt den Tenant, ersetzt aber keine Berechtigungsprüfung (Backend 5.1): Manifest und Icons antworten nur für den Tenant der Sitzung, die Mitglieder-App leitet einen fremden oder Platzhalter-Kennzeichner (`_`) auf den Tenant der Sitzung um.
2. **Manifest nur mit Sitzung.** Der Browser holt das Manifest mit den Cookies derselben Origin; das Manifest eines Tenants ist deshalb nach der Zuordnung erreichbar und für Unangemeldete „nicht gefunden“ beziehungsweise 401. Die Anmeldeseite vor Zuordnung trägt keinen Manifestverweis (A-078).
3. **Standard-Theme als Ergebnis der Ableitung.** Die Startwerte aus Marke 2.2 sind die Eingabe des Operators; das geprüfte Standard-Theme (A-013) ist ihre Ableitung. Dadurch ersetzt die Kontrastprüfung `progress` hell (`#E4670A` → `#D45E00` gegen die Bahnfläche und die Kartenfläche), `on-progress` hell (Weiß → Schwarz, 4,5:1 Text) und `warning` hell (`#9A6300` → `#945F00` gegen `surface-container`); die Gründe stehen im Kontrastbericht. Die Werte der Mockups sind Gestaltungsannahmen (README der Mockups).
4. **Ergänzte Rollen.** Für Text auf den plattformweit festen Rollen und für Material-Fehlerzustände liefert der Tokensatz zusätzlich `on-success`, `on-warning`, `on-error`, `error-container`, `on-error-container` sowie optional `accent`/`on-accent`; sie sind abgeleitet, nicht einstellbar (Marke 3.1) und in A-110 benannt.
5. **Neutrale Flächen plattformweit.** Marke 2.2 lässt Tenants nur `primary`, Akzent und `progress` überschreiben; Flächen, Text, Ränder, Team-, Erfolgs-, Warn-, Fehler- und Diagrammfarben sind für alle Tenants gleich (aus der Plattform-Saatfarbe beziehungsweise Konfiguration abgeleitet).
6. **Sammelziel.** Die Challenge-Karte braucht ein Ziel; `Challenge.Target` (Migration `SammelzielTarget`, `numeric(12,4)`) und `percent` im Vertrag kommen vom Backend, der Client rechnet keine Prozente (K13). Die Mindestzahl fünf für Aggregate (A-023) wird mit dem Fachpfad (Stufe 6) durchgesetzt; die Karte zeigt nur den gerundeten Prozentwert.
7. **Füllanimation 320 ms.** Die Mockups der Reihe 1 zeigen 900 ms; umgesetzt sind 320 ms gemäß Marke 2.4, keine vierte Stufe.
8. **Icons.** Ein Tenant-Logo-Upload gehört zur Medienverarbeitung (A-054, Stufe 6+); bis dahin erzeugt das Backend alle Icons aus dem Bildzeichen in Tenant-Farbe (`primary`, `progress` auf `primary-container`), wie Marke 5 es als Ersatz vorsieht.
9. **Sensible Aktion Veröffentlichen.** `PUT /api/branding/theme` verlangt die Rolle Tenant-Admin; die frische Anmeldung (Zugang 3.3) prüft die Sitzung der Stufe 4.
10. **Kiosk-Maske ohne Backend.** R10 ist als Oberfläche gebaut; Kennung und PIN verlassen die Maske nicht, bis die Kiosk-Personensitzung der Stufe 4 angebunden ist; die Maske meldet das sichtbar.
11. **Symbolschrift.** Material Symbols Rounded aus fontsource (`wght`-Achse, 966 KB woff2) wird vollständig ausgeliefert; ein Subset auf die verwendeten Symbole ist eine Build-Optimierung außerhalb des JS-Budgets und bleibt offen.
12. **Vitest liest Dateien.** `layout.spec.ts` und `text.service.spec.ts` lesen SCSS und Vorlagen über Node-Dateizugriff (`@types/node`), damit K07 (eine Definition) und A-080 (kein Systemtext im Frontend, Sperrliste) als Tests statt als Konvention gelten.

## 8. Offene Punkte und Entscheidungsvorschläge

1. **Aushang-PDF** (A-013, Marke 9.1): identische Farbwerte im Aushang folgen mit Benachrichtigungen (Stufe 6); der gespeicherte Tokensatz je Version steht bereit.
2. **Installation mit zwei Tenants auf einem Gerät** (Marke 9.4): manuelle Prüfung auf Staging nach Bereitstellung der Hosts (A-107); Manifest, Icons und Verweise sind nachgewiesen.
3. **Screenreader-Durchlauf** (Marke 9, A-081): Textentsprechungen (`aria-label` je Fortschritt, Live-Region für den Beitragsstatus) sind umgesetzt; ein Durchlauf mit echter Hilfstechnik steht aus.
4. **Wechsel Firma ↔ Arena mit Overlay-Schluss** (Marke 9.5): der Theme-Service wechselt Kontext und Tokens; der Arena-Kontext folgt mit dem Arena-Dienst.
5. **Symbolschrift-Subset** (Festlegung 11) und **Logo-Upload** (Festlegung 8) folgen mit der Medienverarbeitung.
6. **Stufe 4** bindet Sitzung, CSRF und Kiosk-Personensitzung an: der Transport über `HttpClient` sendet Cookies (`withCredentials`), Interceptoren kommen dazu; nach neuen Endpunkten `npm run api:export` und `npm run api:generate` (README).
7. Die offenen Punkte aus [Stufe 1](stufe-1.md), Abschnitt 6, [Stufe 2](stufe-2.md), Abschnitt 7, und [Stufe 3](stufe-3.md), Abschnitt 7, bleiben unverändert.
