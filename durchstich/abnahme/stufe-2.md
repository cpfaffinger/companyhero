# Abnahmeprotokoll Stufe 2 – Isolation

**Stufe:** 2 Isolation gemäß [technischer-durchstich.md](../../concept/technischer-durchstich.md), Abschnitt 3: zwei Tenants, RLS mit Laufzeitrechten, Tenant-Kontext je Request und Job, Modulgrenzen mit Architekturtests.
**Nachweise aus:** Backend 11.1 bis 11.3 und 11.9; Domänenkarte 7; ergänzend Backend 5.1 bis 5.3 (Schutzschichten, Transaktionen, Plattformbereich), Datenschutz 7 und 9.10 (Isolationstest über alle Routen), A-011, A-012, A-021, A-022.
**Umgebung:** PostgreSQL 18 in Testcontainern mit dem Init-Skript und den Rollen des Compose-Projekts; alle Fachtests mit der Laufzeitrolle `ch_app` (kein Besitz, kein `BYPASSRLS`, kein `CREATE`). GitHub-Actions-Runner gemäß A-107; zusätzlich lokale Läufe unter Windows mit Docker in WSL.
**Stand:** abgenommen am 25.09.2026 mit dem grünen CI-Lauf 36170960386 (Abschnitt 5).

## 1. Was Stufe 2 liefert

- **Tenant-Kontext als Querschnitt der Plattforminfrastruktur** (Domänenkarte 3): `TenantContext` ist unveränderlich und hat zwei Arten, Tenant (mit Tenant, optional Person und Rollen) und Plattform (Operator-Anwendungsfälle, Backend 5.3). Ein Scope hat genau einen Kontext; Module lesen ihn über `ITenantContextAccessor`, setzen darf ihn nur die Plattform.
- **Kontext je Request:** `TenantContextMiddleware` nimmt Tenant- und Personenkennung aus der geprüften Identität, prüft Mitgliedschaft, Tenant-Zustand und Rollen bei Organisation (`IMembershipVerification`) und setzt erst dann den Kontext. Rollen kommen nie aus Claims. Ohne aktive Mitgliedschaft bleibt der Request ohne Kontext; Endpunkte antworten mit 401.
- **Kontext je Job:** `ITenantScopeFactory` erzeugt je Job einen eigenen Scope mit Kontext; der Worker der Stufe 3 verwendet genau diesen Baustein.
- **Durchsetzung an der Datenbankgrenze** (Backend 5.2), drei EF-Core-Interceptoren auf jedem Modulkontext: (1) beim Start jeder Transaktion wird der Kontext auf derselben Verbindung transaktionslokal per `set_config('app.tenant_id', …, true)` und `set_config('app.context', …, true)` gesetzt, ohne Kontext wird die Transaktion abgelehnt; (2) jeder Befehl ohne Transaktion wird abgelehnt, jeder Befehl gegen ein fremdes Schema ebenfalls, auch Raw SQL; (3) beim Speichern muss jede tenantbezogene Entität die `tenant_id` des Kontexts tragen.
- **Row Level Security in versionierten Migrationen** (A-011): jede Tabelle der Modulschemata hat `ENABLE` und `FORCE ROW LEVEL SECURITY` und eine Policy `tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid`. Fehlender Kontext ergibt `NULL`, also keine sichtbare Zeile und keinen Schreibzugriff. Die Organisationstabelle (Plattformdatum) zeigt einem Tenant nur seine eigene Zeile, dem Plattformkontext alle.
- **Vier Modulschemata mit eigenem DbContext und eigener Migrationshistorie** (A-012): `organisation` (Organisation, Mitgliedschaft, Rollenzuweisung mit zusammengesetztem Fremdschlüssel `(tenant_id, person_id)`), `identity` (Person), `privacy` (Sichtbarkeitsstufe), `progress` (Aktivitätsereignis). Personen werden modulübergreifend nur über ihre Kennung referenziert; kein Fremdschlüssel überschreitet eine Schemagrenze. Der Migrationslauf migriert alle Kontexte; CI prüft je Kontext, dass keine Modelländerung aussteht.
- **Sichtbarkeitsregel als Querschnitt der Domäne Datenschutz** (Datenschutz 3.3, A-021 Regel 1, A-022): reine Fachregel `VisibilityRule` und Anwendungsdienst `IVisibilityRule`; Funktionsrollen des Arbeitgebers (Tenant-Admin, Programm-Manager, Redakteur, Einsichtsrolle) erhalten nie Einzelwerte anderer Personen; „Nur für mich“ und fehlende Wahl sind geschlossen; „Ganze Firma“ ist für Mitglieder offen; „Mein Team“ braucht eine gemeinsame Gruppe.
- **Drei Endpunkte als dauerhafte Produkt-API:** `GET /api/me` (Identität, Tenant, Rollen laut Organisation), `GET /api/members` (nur Tenant-Admin; Anzeigename, Rollen, Beitritt, keine Aktivitätsdaten, Organisation 3.2), `GET /api/persons/{personId}/activities` (hinter der Sichtbarkeitsregel; fremder Tenant, unbekannte Person und unsichtbare Werte antworten gleich mit 404, Datenschutz 7).
- **Tests:** 41 Integrationstests mit Laufzeitrechten, 39 Architekturtests, 11 Fachtests ohne Infrastruktur (neues Projekt `CompanyHero.Domain.Tests`).

## 2. Nachweise

| Nr. | Nachweis aus dem Konzept | Test oder Prüfung | Ergebnis | Lauf |
|---|---|---|---|---|
| 1 | Backend 11.1: zwei Tenants, mehrere Rollen, persönliche Daten; tenantfremdes Lesen scheitert | `TenantIsolationTests.Lesen_im_Kontext_eines_Tenants_liefert_nur_dessen_Zeilen`, `Raw_SQL_im_Kontext_A_sieht_keine_Zeilen_von_B_auch_nicht_mit_explizitem_Filter` (auch `where tenant_id = B` liefert nichts) | grün | CI-Job „Backend“ |
| 2 | Backend 11.1: tenantfremdes Schreiben scheitert, auch per Raw SQL | `Schreiben_mit_fremder_tenant_id_wird_von_Anwendung_und_Datenbank_abgelehnt` (Anwendung: `TenantMismatchException`; Datenbank: SQLSTATE 42501), `Update_und_Delete_fremder_Zeilen_treffen_keine_Zeile` (0 Zeilen) | grün | CI-Job „Backend“ |
| 3 | Backend 11.1, A-011: Beziehungen über die Tenantgrenze scheitern | `Beziehung_ueber_die_Tenantgrenze_scheitert_am_zusammengesetzten_Fremdschluessel`: Rolle für fremde Person im eigenen Tenant scheitert am Fremdschlüssel (23503), Rolle mit fremder `tenant_id` an RLS (42501); Navigation liefert nur eigene Rollen | grün | CI-Job „Backend“ |
| 4 | Backend 11.2, 5.2: fehlender Kontext führt zu Ablehnung, nie zu unbeschränktem Zugriff | `Ohne_Kontext_wird_jede_Operation_abgelehnt_statt_unbeschraenkt_zu_lesen` (Anwendung: `TenantContextMissingException` vor dem ersten Befehl; Datenbank ohne `set_config`: 0 Zeilen, Insert 42501), `Lesen_ausserhalb_der_Kontexttransaktion_wird_abgelehnt` | grün | CI-Job „Backend“ |
| 5 | Backend 11.2, 5.2: Pool-Wiederverwendung trägt keinen Kontext weiter | `Kontext_endet_mit_der_Transaktion_und_bleibt_nicht_auf_der_Poolverbindung`: Poolgröße 1, nach Commit ist `current_setting('app.tenant_id')` auf derselben physischen Verbindung leer, der nächste Tenant sieht nur seine Zeilen | grün | CI-Job „Backend“ |
| 6 | Backend 11.2, 5.1 Nr. 2: parallele Requests | `RequestIsolationTests.Parallele_Requests_zweier_Tenants_vermischen_keine_Kontexte`: 40 gleichzeitige Requests zweier Tenants, jede Antwort trägt Tenant, Person und Tenant-Name der eigenen Sitzung | grün | CI-Job „Backend“ |
| 7 | Backend 11.2, 5.2: Rollback und Wiederholung bauen Transaktion und Kontext gemeinsam neu auf | `Rollback_verwirft_die_Aenderung_und_die_naechste_Transaktion_erhaelt_frischen_Kontext`, `Wiederholung_nach_Transaktionsfehler_baut_Transaktion_und_Kontext_gemeinsam_neu_auf` (Ausführungsstrategie mit simuliertem Fehler; zweiter Versuch hat frischen Kontext, genau eine Zeile) | grün | CI-Job „Backend“ |
| 8 | Backend 5.1 Nr. 2: Worker setzen je Job einen neuen Kontext | `JobContextTests`: vier Jobs abwechselnd zweier Tenants auf einer Poolverbindung sehen jeweils nur eigene Zeilen; nach dem Job existiert der Scope nicht mehr; Job ohne Person hat keine Rollen | grün | CI-Job „Backend“ |
| 9 | Backend 11.3, Domänenkarte 7, Datenschutz 9.1: Tenant-Admin erhält trotz gleicher `tenant_id` keine privaten Aktivitäten | `VisibilityTests.Tenant_Admin_erhaelt_keine_Aktivitaeten_anderer_Personen_trotz_gleicher_tenant_id` (404 für „Ganze Firma“ und „Nur für mich“), `Programm_Manager_erhaelt_ebenfalls_keine…`, `Tenant_Admin_erhaelt_auch_ueber_die_Anwendungsfunktion_keine_fremden_Aktivitaeten`, `Mitgliederliste_nur_fuer_den_Tenant_Admin_und_ohne_Aktivitaetsdaten` (Felder genau Anzeigename, Beitritt, Kennung, Rollen; Mitglied erhält 403) | grün | CI-Job „Backend“ |
| 10 | Datenschutz 3.3, A-022: die restriktivere Einstellung gewinnt | `Mitglied_sieht_Aktivitaeten_bei_Ganze_Firma_und_nicht_bei_Nur_fuer_mich`, `Person_sieht_ihre_eigenen_Aktivitaeten_immer`; Fachtests `VisibilityRuleTests` (Matrix aller Stufen und Rollen) | grün | CI-Jobs „Backend“ |
| 11 | Backend 5.1 Nr. 1, Domänenkarte 5: Tenant aus geprüfter Sitzung und Mitgliedschaft; Rollen aus Organisation | `Sitzung_mit_Person_eines_anderen_Tenants_ergibt_keinen_Kontext` (401), `Rollen_kommen_aus_Organisation_und_nicht_aus_der_Sitzung`, `Eingerichteter_Tenant_ohne_Tenant_Admin_gewaehrt_noch_keinen_Zugang` (Organisation 1.3: aktiv erst mit dem ersten Tenant-Admin), `Ohne_Sitzung_gibt_es_keinen_Kontext_und_keine_Daten` | grün | CI-Job „Backend“ |
| 12 | Datenschutz 7, 9.10, A-026: Isolationstest über alle Routen, Antwort „nicht gefunden“ | `Jede_Route_mit_Personenbezug_antwortet_fremdem_Tenant_mit_nicht_gefunden`: alle Routen mit `{personId}` aus dem `EndpointDataSource`, Sitzung von Tenant A (Mitglied und Tenant-Admin) auf Person von Tenant B ergibt 404, eigener Tenant 200 | grün | CI-Job „Backend“ |
| 13 | Domänenkarte 7: Datenbanktests scheitern, wenn ein Modul ein fremdes Schema liest oder schreibt | `ModuleBoundaryDatabaseTests.Modulkontext_lehnt_SQL_gegen_ein_fremdes_Schema_ab` (`SchemaBoundaryViolationException` für Lesen und Schreiben, eigenes Schema erreichbar) | grün | CI-Job „Backend“ |
| 14 | Backend 11.9, A-012: Schemata belegen, dass kein fremder Zugriff existiert | `Jede_Tabelle_eines_Modulschemas_hat_Row_Level_Security_mit_FORCE_und_Policy` (sechs Tabellen), `Kein_Fremdschluessel_ueberschreitet_eine_Schemagrenze` (0 im Katalog), `Jedes_Modulschema_fuehrt_seine_eigene_Migrationshistorie_ohne_Rechte_der_Laufzeitrolle`, `Plattformkontext_sieht_Organisationen_aber_keine_tenantbezogenen_Zeilen`, `Tenantkontext_sieht_nur_die_eigene_Organisation` | grün | CI-Job „Backend“ |
| 15 | Domänenkarte 7, A-012: Architekturtests scheitern bei jeder Referenz außerhalb der erlaubten Richtung | `DependencyDirectionTests` (Matrix, unverändert), `ModuleBoundaryTests`: jeder Modulkontext bildet nur Tabellen seines Schemas ab und ist registriert; kein Modul verwendet `*.Infrastructure` eines anderen Moduls oder den Plattformkontext; Schemata eindeutig; `*.Domain` ohne EF Core und ASP.NET Core | grün (39 Tests) | CI-Job „Backend“ |
| 16 | Backend 5.1 Nr. 4, Betrieb 9.4: Migrationslauf mit eigener Rolle, Laufzeitrolle ohne Besitz und Historienrechte, für alle Modulschemata | `MigrationsAndRolesTests` (unverändert grün), CI-Schritt „ausstehende Modelländerungen“ je Kontext, Nachweis 14 (Historie je Schema) | grün | CI-Job „Backend“ |

## 3. Trockenübung: Herauslösung eines Moduls (Backend 11.9, A-012)

Beispiel Fortschritt (`CompanyHero.Modules.Progress`, Schema `progress`):

| Prüfung | Befund |
|---|---|
| Projektreferenzen | Plattform, Privacy (Sichtbarkeitsregel), Organisation; keine Referenz auf Identity-, Challenges- oder Feed-Infrastruktur (`DependencyDirectionTests`, `ModuleBoundaryTests`) |
| Tabellen | nur `progress.activity_event`; Migrationshistorie `progress.__ef_migrations` (Nachweis 14) |
| Fremdschlüssel | keine über die Schemagrenze; `person_id` ist eine Kennung ohne Fremdschlüssel (Domänenkarte 2) |
| Datenzugriff | `ProgressDbContext` erreicht nur `progress`; Raw SQL gegen andere Schemata wird abgelehnt (Nachweis 13) |
| Eingehende Abhängigkeiten | keine Projektreferenz aus Feed, Notifications oder Metering auf Progress (Matrix); spätere Abonnenten koppeln über Fachereignisse (Backend 6.4, Stufe 3) |
| Querschnitte | Tenant-Kontext, Transaktion und Sichtbarkeitsregel werden bezogen, nicht nachgebaut |

Ergebnis: Das Modul ließe sich mit seinem Schema, seiner Historie und seinen Anwendungsfunktionen herauslösen; der einzige Kopplungspunkt wäre der synchrone Aufruf der Sichtbarkeitsregel, der dann über eine Schnittstelle des Datenschutzmoduls liefe.

## 4. Roter Ausgangspunkt und Gegenproben (TDD, Backend 9)

Die Tests sind so geschrieben, dass sie an der fachlichen Grenze scheitern, nicht an fehlender Infrastruktur. Zwei Gegenproben am fertigen Stand (lokal, 25.09.2026):

**Ohne RLS-Policies** (Aufrufe von `RowLevelSecurity` in den Migrationen entfernt): 13 von 41 Integrationstests rot, alle aus fachlichem Grund, Auszug:

```
fehlerhaft Lesen_im_Kontext_eines_Tenants_liefert_nur_dessen_Zeilen                       Expected: 4  Actual: 5
fehlerhaft Raw_SQL_im_Kontext_A_sieht_keine_Zeilen_von_B_auch_nicht_mit_explizitem_Filter Expected: 4  Actual: 5
fehlerhaft Schreiben_mit_fremder_tenant_id_wird_von_Anwendung_und_Datenbank_abgelehnt     Expected: typeof(Npgsql.PostgresException)
fehlerhaft Update_und_Delete_fremder_Zeilen_treffen_keine_Zeile                           Expected: 0  Actual: 2
fehlerhaft Ohne_Kontext_wird_jede_Operation_abgelehnt_statt_unbeschraenkt_zu_lesen        Expected: 0  Actual: 6
fehlerhaft Plattformkontext_sieht_Organisationen_aber_keine_tenantbezogenen_Zeilen        Expected: 0  Actual: 7
fehlerhaft Jede_Tabelle_eines_Modulschemas_hat_Row_Level_Security_mit_FORCE_und_Policy
gesamt: 41, fehlgeschlagen: 13, erfolgreich: 28
```

**Ohne Kontext-Interceptoren** (`EnforceTenantContext = false` im Laufzeithost): Schon das Anlegen der zwei Test-Tenants scheitert, weil die Datenbank ohne gesetzten Kontext nichts zulässt:

```
Npgsql.PostgresException : 42501: new row violates row-level security policy for table "organisation"
gesamt: 41, fehlgeschlagen: 41, erfolgreich: 0
```

Mit beiden Schichten: 41 von 41 grün.

## 5. CI-Lauf

| Feld | Wert |
|---|---|
| Lauf | [36170960386](https://github.com/cpfaffinger/companyhero/actions/runs/36170960386), Workflow `CI`, Push auf `master`, 25.09.2026 |
| Commit | `334427a` „Stufe 2 Isolation: Tenant-Kontext je Request und Job, RLS auf allen Modultabellen, Modulschemata mit eigener Historie“ |
| Jobs | Backend 1 min 3 s grün (11 Fachtests, 39 Architekturtests, 41 Integrationstests; fünf Kontexte ohne ausstehende Modelländerungen); Frontend 30 s grün (unverändert, Ladebudget 58,9 KB gzip am Gerüst); Images, Trivy, Betriebsnachweise, Push 5 min 3 s grün; Wiederherstellungsübung 1 min 18 s grün |
| Tests | 91: 41 Integration (Testcontainer, Laufzeitrolle), 39 Architektur, 11 Fachtests |

Ergebnis je Nachweis aus Abschnitt 2: Nr. 1 bis 16 grün in diesem Lauf.

## 6. Festlegungen dieser Stufe (kein Registerbedarf)

1. **Sitzung vor Stufe 4.** Die serverseitige Cookie-Sitzung (A-007) entsteht in Stufe 4. Die Tests liefern Tenant- und Personenkennung über einen Test-Authentifizierungs-Handler, der nur im Testprojekt registriert ist; der Produkt-Host registriert kein Schema und behandelt jeden Request als nicht angemeldet. Mitgliedschaft, Tenant-Zustand und Rollen prüft in beiden Fällen derselbe produktive Pfad.
2. **Plattformkontext.** Operator-Anwendungsfälle (Backend 5.3) laufen mit `app.context = 'platform'` ohne `app.tenant_id`; tenantbezogene Tabellen bleiben dabei unsichtbar. Ein normaler Request kann diesen Kontext nicht erhalten, weil ihn nur die Plattforminfrastruktur aus einem entsprechenden Claim setzt; die Berechtigungsprüfung für Operator-Personen folgt mit der Operator-Konsole.
3. **„Mein Team“ ohne Gruppen.** Gruppen entstehen mit dem Fachpfad (Stufe 6, Organisation 2). Bis dahin teilt niemand eine Gruppe; die Regel behandelt „Mein Team“ als geschlossen (die restriktivere Einstellung gewinnt). Die reine Fachregel ist für die Gruppenprüfung bereits vollständig getestet.
4. **Funktionsrollen des Arbeitgebers** sind Tenant-Admin, Programm-Manager, Redakteur und Einsichtsrolle (Organisation 4.1: Klarname). Gesundheitsbotschafter handeln unter Anzeigename und folgen der Mitgliederregel; ihre Rollenfunktionen (Quoten ab fünf) liefern ohnehin keine Einzelwerte.
5. **Fremdschlüssel Mitgliedschaft → Organisation** liegt in der Migration statt im EF-Modell, weil EF Core den Werttyp `TenantId` nicht auf den `Guid`-Schlüssel der Organisation binden kann. Der Katalogtest prüft den Fremdschlüssel unabhängig vom Modell.

## 7. Offene Punkte und Entscheidungsvorschläge

1. **Stufe 3** baut auf `ITenantScopeFactory` (Kontext je Job) und den Modulkontexten auf; die Jobbibliothek wird dort gegen die Anforderungen aus A-006 geprüft.
2. Die offenen Punkte aus [Stufe 1](stufe-1.md), Abschnitt 6, bleiben unverändert (Repository-Sichtbarkeit, Hosts, Backup-Repository, Garage ohne Root, Backup-Metriken).
