# Abnahmeprotokoll Stufe 3 – Queue und Idempotenz

**Stufe:** 3 Queue und Idempotenz gemäß [technischer-durchstich.md](../../concept/technischer-durchstich.md), Abschnitt 3: logische Queue, Outbox-Kopplung, Worker-Replikate, Kiosk-Vorgangskennung und Offline-Idempotenzschlüssel.
**Nachweise aus:** Backend 11.4 und 11.5; Entscheidungen A-005, A-006, A-009; ergänzend Backend 3.2, 5.2, 6.1 bis 6.4, Domänenkarte 3 und 7, Challenges 3 und 9.3, Metering 3.3.
**Umgebung:** PostgreSQL 18 in Testcontainern mit dem Init-Skript und den Rollen des Compose-Projekts; alle Fachzugriffe mit der Laufzeitrolle `ch_app`. Worker-Replikate laufen als eigene Hosts gegen dieselbe Datenbank. GitHub-Actions-Runner gemäß A-107; zusätzlich lokale Läufe unter Windows mit Docker in WSL.
**Stand:** abgenommen am 25.09.2026 mit dem grünen CI-Lauf 36182567484 (Abschnitt 5).

## 1. Was Stufe 3 liefert

- **Kontexttransaktion als Plattformquerschnitt** (Domänenkarte 3, Backend 3.2 und 5.2): Alle Modulkontexte eines Scopes (Request oder Job) arbeiten auf derselben Verbindung. `IContextTransaction` ist die einzige Stelle, die Transaktionen beginnt; sie setzt den geprüften Kontext transaktionslokal per `set_config`, ist verschachtelbar (innere Abschnitte treten bei, der äußerste bestätigt oder verwirft) und gibt die Verbindung nach dem Ende an den Pool zurück. Modulkontexte beginnen keine eigenen Transaktionen mehr (Interceptor lehnt ab); Befehle außerhalb der Kontexttransaktion bleiben abgelehnt. Damit sind Beitrag, Fachereignis, Aktivitätsereignis, Metering-Emission und Folgejobs eine einzige Transaktion (Challenges 3, A-006).
- **Logische Queue in PostgreSQL** (A-006, A-108): `platform.job` ohne Tenant-RLS mit Job-ID, Tenant-ID, Jobtyp, fachlicher Referenz, Idempotenzschlüssel, Ausführungszeitpunkt, Lease-Ablauf, Versuchen, Status; keine Nutzdaten. Einreihung über `IJobQueue` nur innerhalb der laufenden Kontexttransaktion (Outbox-Garantie ohne Relay); `on conflict do nothing` auf `(job_type, idempotency_key, tenant_id)`. Beanspruchung mit `FOR UPDATE SKIP LOCKED` und Lease; abgelaufene Leases werden erneut beansprucht. Der Worker führt jeden Job in eigenem Scope mit Kontext (`ITenantScopeFactory`) und eigener Kontexttransaktion aus und bestätigt den Job **in derselben Transaktion wie seine Wirkung**; das Lease-Token ist das Paar aus Worker-Kennung und Versuchszähler, ein verspäteter Versuch kann den Versuch eines Nachfolgers nicht bestätigen und verwirft seine eigene Wirkung. Fehlversuche warten mit verdoppelter Wartezeit bis zu einem Maximum, danach Dead-Letter; Replay ist eine Operator-Aktion im Plattformkontext mit Begründung und Protokolleintrag. Geordnetes Beenden gibt laufende Leases frei, ohne den Versuch zu zählen.
- **Fairness** (A-006): begrenzte gleichzeitige Jobs je Tenant über alle Replikate; Reihenfolge „je Tenant der älteste wartende Job, unter diesen zuerst der am längsten nicht bediente Tenant“.
- **Zeitgesteuerte Aufgaben** (`platform.job_schedule`): Registrierung beim Start, Fälligkeit je Intervall, Zeilensperre `FOR UPDATE SKIP LOCKED` während des Laufs gegen parallele Doppelausführung; Ausführung im Plattformkontext.
- **Fachereignisse zwischen Modulen** (Backend 6.4): Challenges schreibt „Beitrag erfasst“ (Version 1) in `challenges.domain_event`; `IDomainEventDispatcher` reiht je Abonnent einen Job mit der Ereigniskennung ein; Abonnenten lesen über `IChallengeEvents`, nie über die Tabelle.
- **Metering-Emission als Plattformschnittstelle** (Domänenkarte 3, Metering 3.3): `IMeteringEmitter` liegt in der Plattform, die Umsetzung im Modul Metering schreibt `metering.ledger_event` in derselben Kontexttransaktion und dedupliziert über den Idempotenzschlüssel `{Fachereignis-ID}:{Metrik}`; außerhalb einer laufenden Transaktion wird die Emission abgelehnt. Kein Ereignis trägt eine Personen-ID; Bezug ist die Challenge.
- **Challenges minimal** (Challenges 2, 3): laufende Challenge mit Sammelziel (Häkchen oder Zahl), Beitrag mit Erfassungs- und Eingangszeitpunkt, Fachregeln (nie Zukunft, Rückdatierung bis drei Kalendertage in der Tenant-Zeitzone, Zeitraum, Nachfrist 48 Stunden, Wert je Erfassungsart), Kollektivstand als idempotenter Folgejob `challenges.collective.recalculate`.
- **Zwei Idempotenzregime, ein Namensraum** (A-005, A-009, Domänenkarte 3): `challenges.contribution_key` mit Primärschlüssel `(tenant_id, person_id, key)`. Kiosk: das Backend reserviert eine Vorgangskennung vor dem Absenden, gebunden an Tenant und Person; offene Vorgänge sind nach erneuter Anmeldung auffindbar; ein terminal abgebrochener Vorgang nimmt keinen verspäteten Beitrag mehr an. Handy: der Client erzeugt einen Idempotenzschlüssel als UUIDv7. Gleiche Kennung mit gleichem Inhalt (SHA-256 über Challenge, Wert, Erfassungszeitpunkt, Kanal) liefert denselben Beitrag; gleiche Kennung mit abweichendem Inhalt wird abgelehnt; Speicherung und Nachweis sind atomar (Zeilensperre, Wiederholungen laufen nacheinander).
- **Endpunkte** (Vertrag nach A-009: Kennungen und Dezimalwerte als Strings): `POST /api/challenges/{id}/contribution-operations` (Vorgangskennung), `POST /api/challenges/{id}/contributions` (Kanal `kiosk` mit `operationId`, Kanal `mobile` mit `idempotencyKey`; 201 neu, 200 wiederholt, 409 abweichender Inhalt, 404 unbekannte Kennung oder Challenge, 410 abgebrochen, 422 Fachregel), `GET /api/challenges/{id}/collective`.
- **Metriken** (Backend 6.3): `companyhero.jobs.queued`, `companyhero.jobs.oldest_pending_age_seconds`, `companyhero.jobs.dead_letter`, `companyhero.jobs.processed{outcome}`; die vorbereitete Alarmregel „ältester Job über 15 Minuten“ ist damit aktiv, eine Regel für Dead-Letter ist ergänzt.
- **Tests:** 59 Integrationstests mit Laufzeitrechten (18 neu), 39 Architekturtests, 18 Fachtests ohne Infrastruktur (7 neu). Migrationen: `platform` (Jobs), `challenges` und `metering` (Initial, RLS mit FORCE auf allen sechs neuen Tabellen); CI prüft sieben Kontexte auf ausstehende Modelländerungen.

## 2. Nachweise

| Nr. | Nachweis aus dem Konzept | Test oder Prüfung | Ergebnis | Lauf |
|---|---|---|---|---|
| 1 | Backend 6.1, A-006: Job in derselben Transaktion wie die fachliche Änderung; kein bestätigter Vorgang ohne Folgearbeit, kein Job ohne Vorgang | `JobQueueTests.Job_und_fachliche_Aenderung_werden_gemeinsam_dauerhaft_oder_gar_nicht` (Rollback: weder Aktivitätsereignis noch Job; Commit: beides), `Einreihung_nur_innerhalb_der_Kontexttransaktion` (`TenantContextMissingException`) | grün | CI-Job „Backend“ |
| 2 | A-006 Idempotenzschlüssel je Tenant und Jobtyp | `Gleicher_Idempotenzschluessel_erzeugt_keinen_zweiten_Job_und_Plattformjobs_haben_einen_eigenen_Namensraum` | grün | CI-Job „Backend“ |
| 3 | Backend 5.1 Nr. 2, 6.3: jeder Job in eigenem Tenant-Kontext, Bestätigung erst nach dauerhafter Verarbeitung | `Worker_verarbeitet_den_Job_im_Kontext_seines_Tenants_und_bestaetigt_in_derselben_Transaktion` (Wirkung nur im Tenant des Jobs, Status bestätigt, Versuch 1) | grün | CI-Job „Backend“ |
| 4 | Backend 6.3, A-006: Wiederholung mit wachsender Wartezeit, Dead-Letter, Replay als Operator-Aktion mit Protokoll | `Fehlversuche_warten_mit_wachsender_Wartezeit_bis_zum_Dead_Letter_und_Replay_ist_eine_Operator_Aktion` (3 Versuche, `last_error` ohne Personenbezug, keine Wirkung der Fehlversuche; Replay im Tenant-Kontext abgelehnt, im Plattformkontext erfolgreich, danach genau eine Wirkung) | grün | CI-Job „Backend“ |
| 5 | Backend 11.5: Worker während eines Jobs beenden und neu starten, keine verlorene bestätigte Aufgabe, keine doppelte Wirkung | `Geordnetes_Beenden_gibt_den_Lease_frei_und_der_Neustart_verarbeitet_genau_einmal` (Job wartet nach dem Beenden ohne gezählten Versuch, Nachfolger verarbeitet, genau ein Aktivitätsereignis) | grün | CI-Job „Backend“ |
| 6 | Backend 6.3: abgelaufene Leases werden erneut beansprucht (Absturz ohne Bestätigung) | `Abgelaufener_Lease_eines_abgestuerzten_Workers_wird_erneut_beansprucht_und_genau_einmal_verarbeitet` (Zeile eines abgestürzten Workers mit abgelaufenem Lease; Nachfolger verarbeitet einmal, Versuch 2) | grün | CI-Job „Backend“ |
| 7 | Backend 11.5, A-006: parallele Worker ohne Doppelwirkung, verlorener Lease | `Verlorener_Lease_verwirft_die_Wirkung_des_langsamen_Workers_keine_doppelte_Wirkung` (Lease 1 s; langsamer Worker hängt in seiner Transaktion, zweiter Worker übernimmt und bestätigt; die Bestätigung des langsamen scheitert am Lease-Token und seine Wirkung wird verworfen; genau ein Aktivitätsereignis) | grün | CI-Job „Backend“ |
| 8 | Backend 11.5, A-006: Fairness über zwei Tenants mit ungleicher Last | `Fairness_zwei_Tenants_mit_ungleicher_Last_der_kleine_Tenant_wird_abwechselnd_bedient` (40 Jobs gegen 5, ein Worker mit Parallelität 1: die fünf Jobs des kleinen Tenants liegen an den Positionen 2, 4, 6, 8, 10) | grün | CI-Job „Backend“ |
| 9 | Backend 6.3, A-006: zeitgesteuerte Aufgaben ohne Doppelausführung über Replikate | `Zeitgesteuerte_Aufgabe_laeuft_je_Faelligkeit_genau_einmal_trotz_zweier_Replikate` (zwei Hosts, Intervall 1 s, kein Abstand unter 900 ms, Zeile mit letztem Lauf ohne Fehler) | grün | CI-Job „Backend“ |
| 10 | Backend 11.4, Challenges 9.3, A-009: Offline-Beitrag mit Client-Idempotenzschlüssel mehrfach übertragen | `ContributionIdempotencyTests.Offline_Beitrag_mit_Idempotenzschluessel_mehrfach_uebertragen_ergibt_genau_einen_Beitrag_ein_Aktivitaets_und_ein_Metering_Ereignis` (erste Übertragung 201, verlorene Antwort und Wiederholung 200 mit derselben Kennung, sechs parallele Wiederholungen 200; genau ein Beitrag, ein Aktivitätsereignis, ein Metering-Ereignis `challenge.participant_day`, ein Fachereignis) | grün | CI-Job „Backend“ |
| 11 | Backend 11.4, A-005: Kiosk-Beitrag mit Vorgangskennung mehrfach übertragen, korrekte Verbrauchsbuchung | `Kiosk_Beitrag_mit_Vorgangskennung_mehrfach_uebertragen_ergibt_genau_einen_Beitrag_und_eine_Verbrauchsbuchung` (Reservierung 201, fünf parallele Übertragungen: einmal 201, viermal 200, eine Beitragskennung; genau ein Beitrag, ein Aktivitäts- und ein Metering-Ereignis; Vorgang danach nicht mehr offen) | grün | CI-Job „Backend“ |
| 12 | A-005, A-009: gleiche Kennung mit abweichendem Inhalt wird abgelehnt | `Gleiche_Kennung_mit_abweichendem_Inhalt_wird_in_beiden_Regimen_abgelehnt` (409 in beiden Regimen; Bestände unverändert) | grün | CI-Job „Backend“ |
| 13 | A-005: Bindung an Tenant und Person, Wiederaufnahme, terminal abgebrochener Vorgang | `Vorgangskennung_ist_an_Tenant_und_Person_gebunden_und_ein_abgebrochener_Vorgang_nimmt_nichts_mehr_an` (fremde Person 404, nach Abbruch 410, keine Wirkung) | grün | CI-Job „Backend“ |
| 14 | A-009, Domänenkarte 3: ein Namensraum je Tenant und Person; Regime je Kanal | `Vorgangskennung_und_Client_Schluessel_liegen_in_einem_Namensraum_je_Tenant_und_Person` (Vorgangskennung als Client-Schlüssel 409; Client-Schlüssel am Kiosk 400; nicht sortierbare Kennung 400) | grün | CI-Job „Backend“ |
| 15 | Challenges 3, Domänenkarte 7: ein Beitrag erzeugt in einer Transaktion Beitrag, Fachereignis, Aktivitätsereignis, Metering-Ereignis und Folgejob; Abonnent erhält das Ereignis als Job | `Beitrag_und_Folgen_entstehen_in_einer_Transaktion_und_der_Job_berechnet_den_Kollektivstand` (drei Beiträge zweier Personen, zweiter Beitrag am selben Tag ohne zweiten Teilnehmertag; drei Kollektivstand-Jobs, Kollektivstand 4,0000 mit zwei Beitragenden; Testabonnent eines fremden Moduls erhält je Ereignis einen Job und liest über `IChallengeEvents`) | grün | CI-Job „Backend“ |
| 16 | Metering 3.3, A-006: Emission in derselben Transaktion, Doppelzählung ausgeschlossen | `Metering_Emission_ausserhalb_der_Transaktion_der_fachlichen_Aenderung_wird_abgelehnt` (außerhalb: `InvalidOperationException`; innerhalb: `Recorded`, Wiederholung `Duplicate`) | grün | CI-Job „Backend“ |
| 17 | Challenges 3: Zukunft, Rückdatierung, Zeitraum, Nachfrist, Wert | Fachtests `ContributionRulesTests` (7 Tests, unter anderem 40 Stunden nach Ende angenommen, 50 abgelehnt) und `Fachregeln_der_Erfassung_gelten_am_Endpunkt` (422, fremder Tenant 404) | grün | CI-Job „Backend“ |
| 18 | Backend 11.1, 11.2, 11.9 unverändert nach dem Umbau der Kontexttransaktion; RLS auf allen neuen Tabellen | Stufe-2-Tests unverändert grün (Pool-Wiederverwendung, Rollback, Wiederholung, Schemagrenze); `Jede_Tabelle_eines_Modulschemas_hat_Row_Level_Security_mit_FORCE_und_Policy` prüft nun zwölf Tabellen | grün | CI-Job „Backend“ |

## 3. Roter Ausgangspunkt und Gegenproben (TDD, Backend 9)

Die Tests scheitern an der fachlichen Grenze, nicht an fehlender Infrastruktur. Drei Gegenproben am fertigen Stand (lokal, 25.09.2026), jeweils ein Schutzmechanismus entfernt:

**Ohne Idempotenznachweis** (eine festgeschriebene Kennung wird wie eine reservierte behandelt):

```
fehlerhaft Gleiche_Kennung_mit_abweichendem_Inhalt_wird_in_beiden_Regimen_abgelehnt        Expected: Conflict  Actual: Created
fehlerhaft Offline_Beitrag_mit_Idempotenzschluessel_mehrfach_uebertragen_...               Expected: OK        Actual: Created
fehlerhaft Kiosk_Beitrag_mit_Vorgangskennung_mehrfach_uebertragen_...                      Expected: 1         Actual: 5
gesamt: 8, fehlgeschlagen: 3, erfolgreich: 5
```

**Ohne Fairness** (Beanspruchung streng nach Einreihung, `order by id`):

```
fehlerhaft Fairness_zwei_Tenants_mit_ungleicher_Last_der_kleine_Tenant_wird_abwechselnd_bedient
  Der kleine Tenant wurde nicht abwechselnd bedient: Positionen 41, 42, 43, 44, 45
gesamt: 10, fehlgeschlagen: 1, erfolgreich: 9
```

**Ohne Lease-Prüfung bei der Bestätigung** (`update platform.job set status = 3 where id = $1`):

```
fehlerhaft Verlorener_Lease_verwirft_die_Wirkung_des_langsamen_Workers_keine_doppelte_Wirkung
  Expected: 1  Actual: 2   (zwei Aktivitätsereignisse für einen Job)
gesamt: 10, fehlgeschlagen: 1, erfolgreich: 9
```

Mit allen Mechanismen: 59 von 59 Integrationstests grün.

## 4. Produktwahl Jobbibliothek (A-108)

Geprüft gegen A-006: Hangfire.PostgreSql 1.21.1, Quartz.NET 4.1.1, Wolverine (JasperFx, Releases 2026), MassTransit v9 (SQL-Transport, kommerziell). Keine Bibliothek reiht in der Transaktion des Fachvorgangs ein **und** bietet Tenant-Fairness ohne Umgehung; Wolverine und MassTransit bringen ein Messaging-Framework beziehungsweise eine kommerzielle Lizenz mit. Entscheidung: eigene Umsetzung auf `SKIP LOCKED` ohne zusätzliche Abhängigkeit; Begründung, Umfang und Folgen im Register A-108.

## 5. CI-Lauf

| Feld | Wert |
|---|---|
| Lauf | [36182567484](https://github.com/cpfaffinger/companyhero/actions/runs/36182567484), Workflow `CI`, Push auf `master`, 25.09.2026 |
| Commit | `b0d4fac` „Stufe 3 Queue und Idempotenz: Kontexttransaktion je Scope, logische Queue mit Leases und Fairness, Vorgangskennung und Idempotenzschlüssel, Register A-108“ |
| Jobs | Backend 1 min 16 s grün (18 Fachtests, 39 Architekturtests, 59 Integrationstests; sieben Kontexte ohne ausstehende Modelländerungen); Frontend 29 s grün (unverändert, Ladebudget 58,9 KB gzip am Gerüst); Images, Trivy, Betriebsnachweise, Push 5 min 13 s grün (Worker mit Job-Queue im Compose-Stack gesund); Wiederherstellungsübung 1 min 24 s grün (RTO 9 s, RPO 2 s) |
| Tests | 116: 59 Integration (Testcontainer, Laufzeitrolle), 39 Architektur, 18 Fachtests |

Ergebnis je Nachweis aus Abschnitt 2: Nr. 1 bis 18 grün in diesem Lauf.

## 6. Festlegungen dieser Stufe (kein Registerbedarf)

1. **Kontexttransaktion je Scope.** Backend 3.2 erlaubt gemeinsame Transaktionen über Modulgrenzen; Domänenkarte 3 macht die Plattform zum Eigentümer von Kontext und Transaktion. Die Umsetzung teilt deshalb die Verbindung je Scope; Modulkontexte behalten Schema, Historie und Interceptoren. Die Stufe-2-Tests wurden auf `IContextTransaction` umgestellt, ihre Aussagen sind unverändert.
2. **Bestätigung in der Transaktion der Wirkung.** A-006 verlangt nur „Bestätigung erst nach dauerhafter Verarbeitung“ und erwartet mehrfache Zustellung. Die Umsetzung bestätigt den Job in derselben Transaktion wie die Datenbankwirkung; Handler bleiben trotzdem idempotent (Kollektivstand wird aus der Quelle berechnet, nicht addiert), weil äußere Wirkungen wie Push oder E-Mail (Stufe 6) weiterhin mehrfach zugestellt werden können.
3. **Tenant-Zeitzone.** Organisation 2 nennt `Europe/Vienna` als Voreinstellung; bis die Tenant-Einrichtung im Fachpfad die Zeitzone je Tenant führt, rechnen Kalendertage, Nachfrist und Abrechnungsperiode mit dieser Voreinstellung (`TenantTimeZone`).
4. **Teilnehmertag.** Challenges 3 verlangt das Metering-Ereignis „beim ersten Beitrag einer Person am Tag“; die Umsetzung prüft in der Transaktion, ob die Person in dieser Challenge an diesem Kalendertag bereits beigetragen hat. Bezug ist die Challenge-Kennung; der periodengesalzene Zähler-Slot für personennahe Metriken kommt mit Stufe 7.
5. **Rollen für Challenges.** Anlegen nur durch Programm-Manager oder Tenant-Admin (Challenges 4.2); der Wizard und der Lebenszyklus folgen mit dem Fachpfad. Beiträge nehmen alle Mitglieder mit aktiver Mitgliedschaft; die Kiosk-Personensitzung (A-018) kommt in Stufe 4, bis dahin reserviert jede Mitgliedssitzung Vorgangskennungen.
6. **Test-Abonnent.** Die Zustellung an Abonnenten ist mit einem Testhandler eines fremden Moduls belegt, weil Feed und Benachrichtigungen erst in Stufe 6 abonnieren. Die Registrierung liegt im Testprojekt; der Produkt-Host kennt sie nicht.
7. **Aufräumen erledigter Jobs** bleibt bis zum Fachpfad offen; erfolgreiche Jobs bleiben in der Tabelle und liefern die Fairness-Reihenfolge („zuletzt bedient“).

## 7. Offene Punkte und Entscheidungsvorschläge

1. **Stufe 4** baut die Kiosk-Gerätesitzung und Personensitzung (A-005, A-018) auf die Vorgangskennung auf; `ReserveOperationAsync` wird dann an die Kiosk-Personensitzung gebunden.
2. **Stufe 5** prüft die beiden Idempotenzregime durch den generierten Client (A-009 Nachweise).
3. **Backup-Metriken** (Stufe 1, Punkt 5): der Worker liefert nun Metriken; der Backup-Exporter für `companyhero_backup_last_success_timestamp_seconds` steht weiterhin aus.
4. Die offenen Punkte aus [Stufe 1](stufe-1.md), Abschnitt 6, und [Stufe 2](stufe-2.md), Abschnitt 7, bleiben unverändert (Repository-Sichtbarkeit, Hosts, Backup-Repository, Garage ohne Root).
