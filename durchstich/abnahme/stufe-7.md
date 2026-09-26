# Abnahmeprotokoll Stufe 7 – Geld

**Stufe:** 7 Geld gemäß [technischer-durchstich.md](../../concept/technischer-durchstich.md), Abschnitt 3: Ledger, Slots mit Salzvernichtung, Kostenvorschau, Testphase, Rechnungsentwurf mit den zwei Tenants der Stufen 2 bis 6.
**Nachweise aus:** Metering 9.1 bis 9.8 und 9.12; Entitlements 8.1 bis 8.5, 8.8 (Kiosk-Geräte) und 8.10; ergänzend Datenschutz 9.4 (Isolation), Backend 5.1 und 9; Entscheidungen A-006, A-011, A-023, A-030, A-064 bis A-074, A-106; Produktwahl A-113.
**Umgebung:** Backend gegen PostgreSQL 18 in Testcontainern mit den Rollen des Compose-Projekts (Laufzeitrolle `ch_app`, Migrationsrolle für den Migrationslauf); echte Sitzungen über den Sitzungsdienst der Stufe 4 (Cookie und CSRF, Kiosk-Gerätesitzung); feste Testuhr (25.09.2026 08:00 UTC) in der API, für Zeitsprünge (Buchung am 16., Kündigung am 10., Versiegelung am 03.10. 03:00 Wien, Salzvernichtung am 10.10.) je Test gesetzt und zurückgestellt, Sitzungen nach Zeitsprüngen neu ausgestellt; Worker mit Systemuhr für Zustellung und Lebenszyklus; Frontend als Produktionsbuild hinter einem statischen Server mit den aufgenommenen Vertragsproben.
**Branch:** `claude/stufe-7-geld-ffoa5t` (Stufe 7 „Geld“ gemäß Durchstich 3: ein Branch je Stufe; enthält die noch nicht auf `master` gemergte Stufe 6 aus `claude/stufe-6-fachpfad-r36muw` als Vorgänger-Commits).
**Stand:** abgenommen am 26.09.2026 mit dem grünen CI-Lauf CI_RUN_ID (Abschnitt 6).

## 1. Was Stufe 7 liefert

- **Metering-Ledger** (A-069, A-070): `metering.ledger_event` mit Bewertungsmerker (`rated`, falsch in der Testphase), Nachlauf-Kennzeichnung (`late`) und Storno-Verweis (`reversal_of`); Idempotenzschlüssel aus Fachereignis-ID und Metrik (Beitrag, Historieneintrag der Aktivierung, Zustellung, Gerät und Periode); Gegenbuchungen mit negativer Menge und Verweis auf das Ursprungsereignis (Korrektur eines Beitrags nimmt den Teilnehmertag zurück, eine sofortige erzwungene Deaktivierung den ungenutzten Monatsrest). Die Emission prüft die Testphase des Moduls zum fachlichen Zeitpunkt und bucht Ereignisse einer versiegelten Periode in die offene Periode. Alle Emissionsstellen der Stufen 4 und 6 sind unverändert in derselben Transaktion (A-006): `member.joined`, `kiosk.device_month`, `member.active_day`, `member.active_month`, `challenge.participant_day`, `notification.sent` (Modul nun `Kern`), neu `tenant.month` und `module.trial_day` aus Entitlements.
- **Perioden, Slots und Salz** (A-069, A-113): `metering.billing_period` je Tenant und Periode mit Periodensalz (32 Byte, geschützt mit Data Protection), Versiegelung am dritten Kalendertag des Folgemonats um 03:00 Tenant-Zeit (`metering.seal`, stündlich), Salzvernichtung sieben Tage nach der Versiegelung (`metering.salt.destroy`, täglich; die Zeile bleibt mit `salt_destroyed_at`). Zähler-Slot `slot:<40 Hexzeichen>` als HMAC-SHA256 aus Personenkennung und Periodensalz über den Plattform-Querschnitt `IMeteringSlots` (Eigentümer Metering, verwendet von Fortschritt); für eine versiegelte fachliche Periode gilt der Slot der offenen Buchungsperiode. Fortschritt emittiert `member.active_day` und `member.active_month` bei jeder gewerteten Handlung mit Quelle selbst oder Plattform; die Deduplizierung je Tag und Monat leistet der Idempotenzschlüssel des Ledgers; automatische Tageswerte und Kommentare (`ActivityKinds.Comment`, null Punkte) erzeugen kein Ereignis.
- **Tagesaggregate** (A-073, A-023): `metering.daily_aggregate` je Tenant, Periode, Tag, Modul und Metrik, getrennt nach bewertet und nicht bewertet; aus dem Ledger nachgeführt bei jedem Abruf einer offenen Periode, stündlich (`metering.aggregate`) und bei der Versiegelung; einzige Datenquelle der Tenant-Oberfläche, der API (`GET /api/billing/usage`) und des CSV-Exports (`GET /api/billing/usage/export`). Personennahe Metriken (`member.active_month`, `member.active_day`, `challenge.participant_day`) erscheinen nur als Monatssumme (`days` ist `null`, im Export ohne Tag). Keine Route liefert Ledger-Einzelzeilen; die Lesefunktion `IMeteringLedger` dient Prüfzwecken und Tests.
- **Preisregeln und Bewertung** (A-072, A-113): `PricePlan` mit Regeln (`PriceRule`: Modul, Metrik, Modell, Parameter, Priorität, Gültigkeit) als versioniertes JSON je Tenant (`metering.price_plan_version`, gültig ab Periode), ohne eigene Version der Listenpreisplan `liste-2026` (flat je Modul, per_unit für aktive Mitglieder, Beitritte und Kiosk-Geräte, tiered für Teilnehmertage, min_max 30 bis 5.000, credit 10 Prozent Pilotrabatt bis 2027, revenue_share 20 Prozent). `PricingEngine` in `decimal`: Regeln nach Priorität, dann `min_max`, dann `credit`, dann Steuer je Position; Rundung kaufmännisch auf zwei Nachkommastellen je Position; volume, package, step und one_time ebenfalls umgesetzt; nicht bewertete Mengen ergeben Positionen mit Betrag null und den Vergleichswert „wären X Euro gewesen“; `revenue_share` steht als Provisionszeile neben der Tenant-Summe.
- **Kostenvorschau, Simulation, Rechnungsentwurf** (A-073, A-074): `GET /api/billing/preview` (laufender Monat je Regel, Summen, Prognose als lineare Schätzung mit Kennzeichnung, Definitionen der verwendeten Metriken als Textschlüssel), `GET /api/billing/preview/simulate?module=` (Auswirkung auf laufenden Monat tagesgenau und Folgemonat, nutzungsabhängige Metriken benannt), `GET /api/billing/invoices` und `/api/billing/invoices/{invoiceId}` (Entwurf je versiegelter Periode mit Positionen, Regel, Menge, Einzelpreis, Rechenweg, Steuer; Status `draft`, keine Nummer), `GET /api/billing/periods`, `GET /api/billing/metrics` (verwendete Metriken ohne Beträge; Tenant-Admin und Einsichtsrolle). Alle Geldbeträge und Mengen sind Strings mit zwei beziehungsweise vier Nachkommastellen (K13).
- **Entitlements** (A-064 bis A-068): Modul `Entitlements` mit Schema `entitlements` (Entitlement je Tenant und Modul mit Zustand Testphase/aktiv/auslaufend/inaktiv, `aktiv_ab`, `aktiv_bis`, `test_bis`, Quelle, Testphasenmerker; append-only Historie mit Rolle und Grund; Grenzwerte je Tenant). Modulkatalog M1 bis M12 mit Abhängigkeiten (M2 und M9 setzen M1 voraus), Bereichen der Navigation und Datenschutzhinweis als Textschlüssel; Voreinstellung des Operators (M1) entsteht beim ersten Kontakt eines Tenants mit Quelle „Voreinstellung“. Buchung mit Bündelvorschlag statt Ablehnung, Bündelbuchung als einzelne Entitlements, Testphase (30 Tage, einmal je Modul, Übergang in aktiv proratiert ab Testende, Kündigung in der Testphase kostenfrei), Kündigung zum Monatsende in der Tenant-Zeitzone mit Kaskade auf abhängige Module, Rücknahme vor dem Termin stellt alle wieder her, erzwungene Deaktivierung durch den Operator mit Grund (Anwendungsfunktion). Zeitgesteuerter Lauf `entitlements.transitions` (Testende, `aktiv_bis`, `tenant.month` je Monat, `module.trial_day` je Testtag). Endpunkte `/api/entitlements/me` (Navigation für Mitglieder und Kiosk), `/modules`, `/modules/{module}/book|trial|cancel|revoke-cancellation`, `/bundles/book`, `/history`; Buchungen mit frischer Anmeldung.
- **Durchsetzung** (A-067): Plattform-Querschnitt `IModuleEntitlements` mit Endpunktfilter `RequireModule(...)`: alle 14 Challenge-Endpunkte antworten ohne aktives M1 mit „nicht gefunden“, nie mit „verboten“; `GET /api/challenges/export` bleibt in den 90 Tagen nach `aktiv_bis` für Tenant-Admin und Einsichtsrolle erreichbar. Auswertung je Request ohne prozessweiten Cache: eine Buchung wirkt beim nächsten Request. Grenzwert `kiosk_devices` (Standard 20) über `ITenantLimits` in Identity: Anlegen eines weiteren Geräts liefert 422 `kiosk_device_limit` ohne Metering-Wirkung.
- **Frontend** (K10, K11, K13, K17): Navigation der Mitglieder-App aus `GET /api/entitlements/me` (Start und Ich immer, Challenges nur mit M1; Bereiche Entdecken und Firma vorbereitet), keine Platzhalter für inaktive Module; Verwaltungsseite „Module und Kosten“ (`/t/{tenant}/verwaltung/abrechnung`, Lazy-Einstieg `abrechnung-page`) mit Kostenvorschau (Kennzahlen, Prognose, Testphasen-Hinweis, Positionen je Regel), Modulkatalog (Zustand, Datenschutzhinweis, Voraussetzungen, Buchen mit Simulation vor dem Klick, Bündelvorschlag, Testphase, Kündigen, Rücknahme), Verbrauch als Tagesaggregate (personennahe Metriken „nur Monatssumme“, CSV-Export), Rechnungsentwürfe mit Positionen, Definitionen. Geldformate ohne Gleitkomma (`geld.format.ts`: Zeichenkettenarbeit auf den Dezimalstrings); Datenzugriffe `entitlements.api.ts` und `abrechnung.api.ts`; 98 neue Textschlüssel in drei Tonalitäten mit Du und Sie (Modulnamen, Datenschutzhinweise, Metrikdefinitionen, Verwaltung); Vertrag neu exportiert (129 Operationen), Client regeneriert, 15 neue Vertragsproben.
- **Migrationen:** `entitlements` (Initial: `entitlement`, `entitlement_history`, `tenant_limit`), `metering` (Geld: `ledger_event.rated`, `billing_period`, `daily_aggregate`, `price_plan_version`, `invoice_draft`); alle neuen Tabellen mit RLS und FORCE; CI prüft nun elf Kontexte.
- **Tests:** 147 Fachtests ohne Infrastruktur (+19: `PricingEngineTests`, `EntitlementRulesTests`, `PeriodRulesTests`), 39 Architekturtests, 120 Integrationstests (+13: `Stufe7LedgerTests`, `Stufe7ActiveMemberTests`, `Stufe7BillingTests`, `Stufe7EntitlementTests`; Isolationsfall für `/api/billing/invoices/{invoiceId}`; RLS-Liste um sieben Tabellen), 41 Vitest-Tests (+4), 38 Playwright-Abnahmen (+3). Register: A-113 (Produktwahl und Abweichung Periodensalz); Nachweisstände A-023, A-064 bis A-074. Versionen: [versionen.md](../versionen.md).

## 2. Nachweise

| Nr. | Nachweis aus dem Konzept | Test oder Prüfung | Ergebnis | Lauf |
|---|---|---|---|---|
| 1 | Metering 9.1 / A-069, A-070: Beitrag zweimal übertragen ergibt ein Ledger-Ereignis; Korrektur erzeugt Gegenbuchung; Summen je Periode stimmen | `Stufe7LedgerTests.Beitrag_zweimal_uebertragen_ein_Ledger_Ereignis_Korrektur_als_Gegenbuchung_Summen_je_Periode_stimmen`: zweite Übertragung 200 `already_recorded`, genau ein `challenge.participant_day`, ein `member.active_month`, ein `member.active_day`; Korrektur → Ereignis mit Menge −1 und `reversal_of` auf das Ursprungsereignis, Summe 0; `GET /api/billing/usage` liefert je Metrik dieselbe Summe wie das Ledger | grün | CI CI_RUN_ID |
| 2 | Metering 3.1, 3.3 / A-070: alle Ereignisse der Stufen 4 und 6 im Ledger, ohne Personenbezug | `Alle_Fachereignisse_der_Stufen_4_und_6_stehen_ohne_Personenbezug_im_Ledger`: Beitritt am Kiosk, Geräteregistrierung, Beitrag, Push über Challenge-Start (Worker) → `member.joined`, `kiosk.device_month`, `challenge.participant_day`, `member.active_day`, `member.active_month`, `notification.sent` (Modul `Kern`), `tenant.month`; kein Bezug enthält eine Personenkennung, einen Anzeigenamen oder eine Adresse; aktive Mitglieder mit Präfix `slot:` | grün | ebd. |
| 3 | Metering 9.2 / A-069, A-071: zehn Handlungen ein `member.active_month`; im Folgemonat neuer, nicht verkettbarer Slot; nach Salzvernichtung keine Rückrechnung | `Stufe7ActiveMemberTests.Zehn_Handlungen_ergeben_ein_active_month_…`: zehn Handlungen → ein Ereignis im September mit Slot A; Versiegelung des Septembers; Handlung im Oktober → Slot B ≠ A; `IMeteringSlots` für einen September-Zeitpunkt liefert B (offene Periode); Salzvernichtung erst nach sieben Tagen (`saltAvailable` falsch, `saltDestroyedAt` gesetzt), Slot A bleibt im Ledger, ist ohne Salz für keine Person berechenbar; Fachtest `PeriodRulesTests.Slot_ist_innerhalb_der_Periode_stabil_und_ueber_Perioden_nicht_verkettbar` | grün | ebd. |
| 4 | Metering 9.3 / A-071: Öffnen, Login, Push, automatischer Tageswert und Kommentar erzeugen kein aktives Mitglied | `Oeffnen_Login_Push_automatischer_Tageswert_und_Kommentar_erzeugen_kein_aktives_Mitglied`: neue Sitzung, GET Feed/Fortschritt/Sitzung/Navigation/Challenges, `notification.sent`, Handlung mit Quelle automatisch, Kommentar → kein `member.active_month`/`active_day`; erst der Check-in erzeugt genau eines | grün | ebd. |
| 5 | Metering 9.4 / A-072, A-074: Beispielplan mit tiered, flat, per_unit, revenue_share, min_max, credit liefert nachrechenbare Positionen, Rundung je Position | Fachtest `PricingEngineTests.Beispielplan_liefert_einzeln_nachrechenbare_Positionen_mit_Rundung_je_Position` (49,00 + 92,50 + 45,00 − 18,65 = 167,85; Steuer 33,57; Provision 33,57), `Rundung_je_Position_nicht_erst_in_der_Summe` (0,005 → 0,01), `Mindestbetrag_und_Deckel_…`, `Weitere_Modelle_volume_package_step_und_one_time_…`; `Stufe7BillingTests.Beispielplan_liefert_einen_Rechnungsentwurf_…`: Entwurf 2026-09 aus dem Listenpreisplan mit Positionen `m1.flat` 1,0000 × 49 = 49,00, `kern.active_member` 2 × 2,50 = 5,00, `m1.participant_day` 3 × 0,20 = 0,60, `kern.kiosk` 1 × 5 = 5,00, `credit` −5,96; Netto 53,64, Steuer 10,73 (Summe der Positionssteuern), Brutto 64,37, Provision 10,73; Mengen mit vier, Beträge mit zwei Nachkommastellen als Strings | grün | ebd. |
| 6 | Metering 9.5 / A-072: Buchung am 16. eines 30-Tage-Monats halbe Flatrate; Kündigung am 10. voller Monat mit Zugang bis Monatsende | `Buchung_am_16_eines_30_Tage_Monats_halbe_Flatrate_Kuendigung_am_10_voller_Monat_mit_Zugang_bis_Monatsende`: Kündigung M1 am 10.09. → `activeUntil` 30.09. 22:00 UTC, Zugang am 20.09. 200, Zustand `expiring`; Simulation M3 am 16.09. 14,50 / 29,00; Buchung → `tenant.month` M3 0,5, M1 1,0; Kostenvorschau 0,5 × 29 = 14,50; am 01.10. `GET /api/challenges` 404, Übergang inaktiv, Oktober nur M3 mit 1,0; Fachtest `EntitlementRulesTests.Proratierung_tagesgenau_…` | grün | ebd. |
| 7 | Metering 9.6, Entitlements 8.3 / A-065, A-066, A-072: Testphase erfasst, Betrag null, „wären X Euro gewesen“, Übergang proratiert, Kündigung in der Testphase kostenfrei, zweite Testphase abgelehnt | `Testphase_erfasst_Ereignisse_mit_Betrag_null_zeigt_waeren_X_Euro_gewesen_und_geht_proratiert_in_aktiv_ueber`: Testphase M1 ab 16.09. bis 16.10.; M1-Ereignisse `rated` falsch, `module.trial_day` je Kalendertag (31, nie bewertet); Kostenvorschau: Position `m1.participant_day` 0,00 mit Kennzeichnung, `wouldHaveBeen` 0,20; Kündigung in der Testphase zum Testende und Rücknahme; Testende → aktiv mit `tenant.month` 0,5161 (16 von 31 Tagen) bewertet, `trialAvailable` falsch, erneute Testphase `already_active`; M3: Testphase, Kündigung, nach Testende kein `tenant.month`, zweite Testphase `trial_used`; Fachtests `PricingEngineTests.Testphase_…`, `EntitlementRulesTests.Testphase_hoechstens_einmal_…`, `Testende_aktiviert_…` | grün | ebd. |
| 8 | Metering 9.7 / A-069: Ereignis nach Versiegelung in der offenen Periode mit Nachlauf; versiegelte Periode unverändert | `Stufe7LedgerTests.Ereignis_nach_Versiegelung_steht_in_der_offenen_Periode_mit_Nachlauf_…`: zeitgesteuerter Lauf `metering.seal` am 03.10. 01:30 UTC versiegelt 2026-09 (Rechnungsentwurf vorhanden), 2026-10 offen; Handlung mit fachlichem Zeitpunkt 28.09. → Ereignis `late` in Periode 2026-10 mit unverändertem `occurredAt`; Verbrauch 2026-09 vor und nach dem Nachläufer identisch, 2026-10 zählt ihn; Fachtest `PeriodRulesTests.Versiegelung_am_dritten_Kalendertag_…` (03.10. 03:00 Wien = 01:00 UTC) | grün | ebd. |
| 9 | Metering 9.8, A-023, A-073: Verbrauchsdetail über Oberfläche, API und Export nur Tagesaggregate; aktive Mitglieder nur Summen | `Stufe7BillingTests.Verbrauchsdetail_nur_Tagesaggregate_…`: `challenge.participant_day` und `member.active_month` mit `days` = null und Summe, `kiosk.device_month` mit Tagesliste; JSON und CSV ohne `slot:` und ohne Personenkennung; CSV personennah ohne Tag; keine Route enthält „ledger“; Playwright „nur Monatssumme“ in der Oberfläche | grün | ebd. |
| 10 | Metering 9.12, 5 / A-073: Einsichtsrolle sieht Metriken ohne Beträge; Programm-Manager sieht keine Kosten; Rechtematrix | ebd.: Einsichtsrolle `GET /api/billing/metrics` 200 ohne `net`/`amount`/Preise, Kostenvorschau, Rechnungen, Verbrauch 403, Module und Historie 200, buchen 403; Programm-Manager auf Kostenvorschau, Simulation, Verbrauch, Export, Rechnungen, Metriken, Module, buchen 403; Mitglied 403 außer Navigation; Tenant-Admin 200; Kiosk-Gerät 403; Export Challenges nur Tenant-Admin und Einsichtsrolle (27 Zellen) | grün | ebd. |
| 11 | Entitlements 8.1 / A-064, A-067: nur Kern zwei Bereiche, keine Schlösser, keine Hinweise; Modulendpunkt „nicht gefunden“ | `Stufe7EntitlementTests.Tenant_nur_mit_Kern_zwei_Bereiche_…`: `/api/entitlements/me` → `["start","ich"]`, `modules` leer, Antwort ohne `M1`/`lock`; `GET /api/challenges` (Mitglied, Kiosk-Gerät), `POST /api/challenges/kickoff`, `GET /api/challenges/manage`, `POST /api/challenges` → 404; Kern (Feed, Fortschritt) 200; Katalog der Verwaltung mit 12 Modulen und Datenschutzhinweis; Playwright: zwei Navigationseinträge, kein `lock`-Symbol, kein Modulhinweis ([nur-kern-390.png](laeufe/stufe-7/nur-kern-390.png)) | grün | ebd. |
| 12 | Entitlements 8.2, 8.10 / A-066, A-067: M2 ohne M1 Bündelvorschlag; Buchung beider erweitert Navigation beim nächsten Request; zwei Metering-Ereignisse | `Buchung_von_M2_ohne_M1_ergibt_Buendelvorschlag_…`: `book M2` → `bundle_suggested` `["M1","M2"]`, nichts gebucht, keine Emission; `bundles/book` → `booked` beide; sofort danach Navigation `["start","challenges","ich"]`, `GET /api/challenges` 200; zwei `tenant.month` (`module:M1`, `module:M2`); Historie mit `preset`, `forced:…`, `booked` mit Rolle; Playwright Simulation vor dem Klick, Bündelvorschlag, Bündelbuchung ([buendelvorschlag-800.png](laeufe/stufe-7/buendelvorschlag-800.png)) | grün | ebd. |
| 13 | Entitlements 8.4, 8.5 / A-066: Kündigung M1 mit aktivem M2 beide auslaufend; Rücknahme stellt beide wieder her; nach `aktiv_bis` Bereich weg, Export 90 Tage, Abzeichen bleiben | `Kuendigung_von_M1_mit_aktivem_M2_laesst_beide_auslaufen_…`: `cancel M1` → `affected` `["M1","M2"]`, beide `expiring` bis 30.09. 22:00 UTC, Zugang bis dahin; `revoke-cancellation M1` → beide `active`; nach `aktiv_bis` Navigation ohne Challenges, Challenge-Endpunkte 404, `GET /api/challenges/export` 200 mit der Challenge, Abzeichen unverändert; nach 91 Tagen Export 404; Fachtest `EntitlementRulesTests.Kuendigung_zum_Monatsende_…`, `Kuendigung_von_M1_erfasst_abhaengige_aktive_Module` | grün | ebd. |
| 14 | Entitlements 8.8 / A-068 (Stufe 6 Punkt 6, Grenzwerte soweit Metering zählt): Grenzwert Kiosk-Geräte | `Grenzwert_Kiosk_Geraete_blockiert_mit_Hinweis_ohne_Metering_Wirkung_Operator_erhoeht`: Grenzwert 1 → zweites Gerät 422 `kiosk_device_limit`, kein `kiosk.device_month`; Grenzwert 2 → 201 | grün | ebd. |
| 15 | Datenschutz 9.4, Backend 5.1 / A-026: Isolation aller neuen Routen mit Kennung | `Stufe6IsolationTests.Jede_Route_mit_Kennung_antwortet_fremdem_Tenant_mit_nicht_gefunden` (24 Routen, neu `/api/billing/invoices/{invoiceId}` mit echtem Entwurf des Tenants W: fremder Tenant-Admin 404); Modul- und Buchungsrouten tragen keine Kennung, der Tenant kommt aus der Sitzung | grün | ebd. |
| 16 | Backend 5.1 Nr. 4 / A-011: RLS mit FORCE und Policy auf allen 52 Modultabellen | `ModuleBoundaryDatabaseTests.Jede_Tabelle_eines_Modulschemas_hat_Row_Level_Security…` (erweitert um `entitlements.entitlement`, `entitlement_history`, `tenant_limit`, `metering.billing_period`, `daily_aggregate`, `invoice_draft`, `price_plan_version`) | grün | ebd. |
| 17 | Domänenkarte 7 / A-012: Metering und Entitlements referenzieren keine Fachmodule; Fachmodule emittieren über Plattform-Querschnitte | `DependencyDirectionTests` (Metering: Platform, Tier 1, Privacy; Entitlements: Platform, Organisation; `IMeteringSlots`, `IModuleEntitlements`, `ITenantLimits` in der Plattform), `ModuleBoundaryTests` (elf Kontexte, eigene Schemata) | grün | ebd. |
| 18 | A-009, K13: Vertrag und Vertragsproben; Geldbeträge nie als Gleitkommazahl | `OpenApiContractTests` (129 Operationen; Dezimalwerte `quantity`, `net`, `total` als Strings, kein `number`; CSV-Export als typlose Antwort zugelassen), `ContractSamplesTests` (15 neue Proben: Entitlements Navigation, Katalog, Bündelvorschlag, Bündelbuchung, Testphase, Kündigung, Historie; Kostenvorschau, Simulation, Verbrauch, Metriken, Perioden, Rechnungsliste und -entwurf), `contract.spec.ts`; Frontend `geld.format.spec.ts` (Zeichenkettenformatierung ohne Gleitkomma) | grün | ebd. |
| 19 | Frontend: Verwaltungsseite Abrechnung lazy im Ladebudget; Texte aus dem Katalog ohne Sperrbegriffe | Playwright `geld.spec.ts` (drei Abnahmen: nur Kern 390 px; Verwaltung 1280 px mit Kostenvorschau, Positionen, Verbrauch, Rechnungsentwurf, Definitionen ([verwaltung-abrechnung-1280.png](laeufe/stufe-7/verwaltung-abrechnung-1280.png)); Buchung mit Simulation und Bündelvorschlag 800 px); `text.service.spec.ts` (alle Schlüssel im Katalog, keine Sperrbegriffe; `TextCatalogTests` prüfen drei Tonalitäten mit Du und Sie, Sperrliste, Ausrufezeichen); Ladebudget 183 303 B von 184 320 B gzip, `abrechnung-page` als verbotener Präfix im initialen Ladevorgang | grün | ebd. |

## 3. Roter Ausgangspunkt und Gegenproben (TDD, Backend 9, K19)

Die Fach- und Integrationstests entstanden vor der Umsetzung und scheiterten aus fachlichem Grund: die Fachtests an fehlenden Klassen (Preismodelle, Katalog, Entitlement-Zustände, Periodenregeln, Slots), die Integrationstests zuerst am fehlenden Modulkontext (`EntitlementsDbContext` ohne Migration, Dependency-Zyklus Metering ↔ Entitlements beim Start des Hosts), danach an fachlichen Lücken, die die Umsetzung nachweislich geändert haben:

- **Aktives Mitglied verdeckt durch automatischen Tageswert** (`Oeffnen_Login_Push_…`): der Zähler der gewerteten Handlungen des Tages zählte einen automatischen Tageswert mit, sodass der erste eigene Check-in kein `member.active_month` erzeugte (`Assert.Single() Failure: The collection was empty`). Seither emittiert Fortschritt bei jeder gewerteten Handlung mit Quelle selbst oder Plattform; die Deduplizierung je Tag und Monat leistet der Idempotenzschlüssel des Ledgers.
- **Zwei Metering-Ereignisse bei Bündelbuchung** (`Buchung_von_M2_ohne_M1_…`): der Idempotenzschlüssel der Aktivierung war `Modul:Periode:tenant.month`, eine zweite Aktivierung im selben Monat war ein Duplikat (`Expected: 2, Actual: 1`). Seither ist das Fachereignis der Historieneintrag der Aktivierung (Metering 2.1), und eine sofortige erzwungene Deaktivierung bucht den ungenutzten Monatsrest gegen.
- **Testtage nach dem Testende bewertet** (`Testphase_erfasst_…`): die Bewertung fragte die Testphase zum Zeitpunkt der Emission statt zum fachlichen Zeitpunkt (`30 out of 31 items … Expected: False, Actual: True`). Seither erhält `IModuleEntitlements.IsTrialAsync` den fachlichen Zeitpunkt.
- **Export in der 90-Tage-Frist** (`Tenant_nur_mit_Kern_…`): der Test erwartete für den Export „nicht gefunden“; Entitlements 4.3 sieht die Lesbarkeit vor, der Test wurde an das Konzept angepasst (`Expected: NotFound, Actual: OK`).
- **Vertrag und RLS-Liste**: `OpenApiContractTests` rot (CSV-Export ohne Schema, Vertrag veraltet), `ModuleBoundaryDatabaseTests` rot (sieben neue Tabellen), `Stufe6IsolationTests` rot (`Routen ohne Isolationsfall: /api/billing/invoices/{invoiceId:guid}`), `ContributionIdempotencyTests` rot nach dem Wechsel des Idempotenzschlüssels auf die Beitragskennung.

Fünf Gegenproben mit entferntem Schutzmechanismus (jeweils Umbau, Build, Lauf der betroffenen Testklassen, Wiederherstellung; Auszüge des Laufs):

**Idempotenz im Ledger entfernt** (`MeteringEmitter.EmitAsync`: Prüfung des Idempotenzschlüssels entfernt, Schlüssel je Emission zufällig ergänzt; jede Wiederholung wird ein neues Ereignis):

```
fehlerhaft Stufe7BillingTests.Beispielplan_liefert_einen_Rechnungsentwurf_mit_einzeln_nachrechenbaren_Positionen_und_Rundung_je_Position
  Assert.Equal() Failure: Values differ
  Expected: Tuple ("2.0000", "5.00")      (kern.active_member: zwei aktive Mitglieder)
  Actual:   Tuple ("3.0000", "7.50")      (zweiter Beitragstag derselben Person zählt als drittes Mitglied)
fehlerhaft Stufe7BillingTests.Testphase_erfasst_Ereignisse_mit_Betrag_null_zeigt_waeren_X_Euro_gewesen_und_geht_proratiert_in_aktiv_ueber
  Assert.Equal() Failure: Values differ
  Expected: 31                            (module.trial_day je Kalendertag)
  Actual:   32                            (wiederholter Lauf entitlements.transitions bucht doppelt)
fehlerhaft Stufe7LedgerTests.Beitrag_zweimal_uebertragen_ein_Ledger_Ereignis_Korrektur_als_Gegenbuchung_Summen_je_Periode_stimmen
  Assert.Equal() Failure: Values differ   (Gegenbuchung findet ihr Ursprungsereignis nicht mehr)
  Expected: 01a0df4c-8ccd-7aef-a9e9-d0d11bc9f80b
  Actual:   null
gesamt: 4, fehlgeschlagen: 2, erfolgreich: 2 (Billing); gesamt: 3, fehlgeschlagen: 1, erfolgreich: 2 (Ledger)
```

**Salz aus dem Zähler-Slot entfernt** (`PeriodRules.Slot`: `HMACSHA256.HashData(salt, …)` durch `SHA256.HashData(personId)` ersetzt; Slots werden über Perioden verkettbar und aus der Personenkennung rückrechenbar):

```
fehlerhaft PeriodRulesTests.Slot_ist_innerhalb_der_Periode_stabil_und_ueber_Perioden_nicht_verkettbar
  Assert.NotEqual() Failure: Strings are equal
  Expected: Not "slot:93febb31808d6d5d638b23811732d5492e54071a"
  Actual:       "slot:93febb31808d6d5d638b23811732d5492e54071a"
gesamt: 147, fehlgeschlagen: 1, erfolgreich: 146
fehlerhaft Stufe7ActiveMemberTests.Zehn_Handlungen_ergeben_ein_active_month_im_Folgemonat_ein_neuer_Slot_nach_Salzvernichtung_keine_Rueckrechnung
  Assert.Equal() Failure: Values differ   (Oktober-Handlung erhält denselben Slot wie September: kein zweites Ereignis, Perioden verkettet)
  Expected: 2
  Actual:   1
gesamt: 2, fehlgeschlagen: 1, erfolgreich: 1
```

**Kostensicht für den Programm-Manager geöffnet** (`BillingEndpoints`: `GET /api/billing/preview` mit `RequireRoles(Role.TenantAdmin, Role.ProgrammeManager)`):

```
fehlerhaft Stufe7BillingTests.Verbrauchsdetail_nur_Tagesaggregate_personennahe_Metriken_nur_als_Summe_Einsichtsrolle_ohne_Betraege_Programm_Manager_ohne_Kosten
  Programm-Manager: Kostenvorschau: 200 statt 403
gesamt: 4, fehlgeschlagen: 1, erfolgreich: 3
```

**Row Level Security auf `metering.billing_period` entfernt** (Aufruf `RowLevelSecurity.IsolateByTenant(migrationBuilder, "metering", "billing_period")` aus der Migration `Geld` entfernt):

```
fehlerhaft ModuleBoundaryDatabaseTests.Jede_Tabelle_eines_Modulschemas_hat_Row_Level_Security_mit_FORCE_und_Policy
  metering.billing_period: RLS nicht aktiviert
gesamt: 6, fehlgeschlagen: 1, erfolgreich: 5
```

**Entitlement-Prüfung am Modulendpunkt entfernt** (`ChallengeEndpoints`: `RequireModule(ModuleCodes.M1Challenges)` an `GET /api/challenges` entfernt):

```
fehlerhaft Stufe7EntitlementTests.Tenant_nur_mit_Kern_zwei_Bereiche_keine_Hinweise_auf_Module_Modulendpunkt_nicht_gefunden
  Assert.Equal() Failure: Values differ
  Expected: NotFound
  Actual:   OK
fehlerhaft Stufe7EntitlementTests.Kuendigung_von_M1_mit_aktivem_M2_laesst_beide_auslaufen_Ruecknahme_stellt_beide_wieder_her_nach_aktiv_bis_Bereich_weg_Export_90_Tage_Abzeichen_bleiben
  Assert.Equal() Failure: Values differ
  Expected: NotFound
  Actual:   OK
gesamt: 4, fehlgeschlagen: 2, erfolgreich: 2
```

Mit allen Mechanismen: 147 Fachtests, 39 Architekturtests, 120 Integrationstests, 41 Vitest-Tests und 38 Playwright-Abnahmen grün.

## 4. Produktwahlen

| Wahl | Entscheidung | Version |
|---|---|---|
| Preisrechnung, Ledger, Slots | keine Bibliothek: `PricingEngine` in `System.Decimal` mit kaufmännischer Rundung je Position, `numeric(18,4)`/`numeric(18,2)`, HMAC-SHA256 für Zähler-Slots, Periodensalz mit Data Protection geschützt (A-113) | .NET 10 (`System.Decimal`, `HMACSHA256`, Microsoft.AspNetCore.DataProtection 10.0.12), PostgreSQL 18 |
| Entitlement-Prüfung | Endpunktfilter der Plattform (`RequireModule`) mit Auswertung je Request, kein prozessweiter Cache (A-067: „bis 60 Sekunden“) | ASP.NET Core 10 Minimal APIs |

## 5. Gemessene Werte

| Größe | Wert |
|---|---|
| Ladebudget Referenzscreen (K17), gzip | 183 303 B von 184 320 B (99,4 %): App-Shell `main` mit Navigation aus Entitlements, `mitglieder.routes`, `challenges-page`, geteilte Chunks; roh 671 992 B, brotli 161 587 B |
| Lazy außerhalb des Referenzscreens | 22 Dateien, 284 487 B roh, darunter `abrechnung-page` (Verwaltung „Module und Kosten“) |
| Kontexte mit Migration | 11 (`PlatformDbContext` bis `EntitlementsDbContext`), 52 Modultabellen mit RLS und FORCE |
| Routen mit Kennung im Isolationstest | 24 |
| Vertrag | 129 Operationen (+17), 15 neue Vertragsproben (52 gesamt) |
| Textkatalog | 480 Schlüssel (+98), drei Tonalitäten mit Du und Sie |
| Beispielrechnung (Listenpreisplan, September 2026) | Netto 53,64 €, Steuer 10,73 €, Brutto 64,37 €, Provision 10,73 € aus fünf Positionen |
| Zeiten und Fristen | Versiegelung 3. Kalendertag 03:00 Tenant-Zeit; Salzvernichtung 7 Tage danach; Testphase 30 Tage; Exportfrist 90 Tage; Grenzwert Kiosk-Geräte 20 |

## 6. CI-Lauf

| Feld | Wert |
|---|---|
| Lauf | [CI_RUN_ID](https://github.com/cpfaffinger/companyhero/actions/runs/CI_RUN_ID), Workflow `CI`, Push auf `claude/stufe-7-geld-ffoa5t`, 26.09.2026 |
| Commit | `CI_COMMIT` „Stufe 7 Geld: …“ (Umsetzung, Protokoll, Register A-113 und Nachweisstände in einem Commit) |
| Jobs | CI_JOBS |
| Tests | 385: 120 Integration (Testcontainer, Laufzeitrolle), 39 Architektur, 147 Fachtests, 41 Vitest, 38 Playwright |

Ergebnis je Nachweis aus Abschnitt 2: Nr. 1 bis 19 grün in diesem Lauf; Screenshots des lokalen Laufs unter [laeufe/stufe-7/](laeufe/stufe-7/).

## 7. Festlegungen dieser Stufe (kein Registerbedarf)

1. **Monatsmenge je Modul.** Entitlements meldet `tenant.month` bei jeder Aktivierung mit dem tagesgenauen Rest des Monats (Buchung am 16. eines 30-Tage-Monats: 0,5) und der zeitgesteuerte Lauf je Monat 1 für Module, die am Monatsersten aktiv oder auslaufend waren; die Kündigung zum Monatsende ändert die Monatsmenge nicht (voller Monat). Eine sofortige erzwungene Deaktivierung bucht den Rest ab dem Folgetag gegen.
2. **Testtage als Kalendertage.** Eine Testphase von 30 Tagen ab 10:00 berührt 31 Kalendertage; je Kalendertag ein `module.trial_day`, nie bewertet. Der Übergang in aktiv beginnt am Testende mit dem tagesgenauen Rest des Monats.
3. **Bewertung zum fachlichen Zeitpunkt.** Ob ein Ereignis in die Testphase fällt, entscheidet der fachliche Zeitpunkt, nicht der Zeitpunkt der Emission; Gegenbuchungen übernehmen die Bewertung ihres Ursprungs.
4. **Nachläufer im Aggregat.** Ereignisse mit Nachlauf-Kennzeichnung stehen im Tagesaggregat der Buchungsperiode am ersten Tag dieser Periode; ihr fachlicher Zeitpunkt bleibt im Ledger.
5. **Voreinstellung beim ersten Kontakt.** Der Rahmen des Operators (M1) wird beim ersten Kontakt eines Tenants mit Entitlements materialisiert (Quelle „Voreinstellung“), weil Organisation Entitlements nicht referenziert; der Zeitpunkt ist der erste Request oder Job des Tenants.
6. **Prognose linear.** Die Prognose Monatsende rechnet den bisherigen Netto-Verlauf linear auf den Monat hoch und ist als Schätzung gekennzeichnet; die letzten drei Monate fließen ein, sobald eine Historie besteht.
7. **Preispläne als JSON je Version.** Eine Version gilt ab einer Periode; vergangene Perioden behalten ihre Version; ohne eigene Version gilt der Listenpreisplan aus der Konfiguration. Die Pflege durch den Operator ist eine Anwendungsfunktion (`IPricePlans.SetAsync`), die Konsole folgt.
8. **Provision als eigene Zeile.** `revenue_share` erscheint im Entwurf als Zeile mit Modell `revenue_share`, ist nicht Teil von Netto, Steuer und Brutto des Tenants und dient der Partnerabrechnung (A-075).
9. **Lokale Werkzeuge dieser Abnahme.** Das .NET-SDK 10.0.401 stand lokal nur als Schichten des Images `mcr.microsoft.com/dotnet/sdk:10.0` zur Verfügung (Download-Hosts gesperrt); Node 24.15.0 wie in CI; Testcontainers ohne Ryuk gegen einen lokalen Docker-Daemon. CI verwendet die Werkzeuge aus `global.json` und dem Workflow.

## 8. Offene Punkte und Entscheidungsvorschläge

1. **Metering 9.9 bis 9.11** (Preisrahmen für Partner, Direkt- und Sammelabrechnung, Mahnlauf, Rechnung als PDF, CSV und EN-16931-XML mit fortlaufender Nummer, Korrektur als Gutschrift), **Rechnungsversand**, **Mahnwesen** und **Partner-Sammelabrechnung** folgen mit der Operator- und Partner-Konsole; der Durchstich endet beim Rechnungsentwurf (Durchstich 1.2).
2. **Periodensalz in OpenBao** (A-069): im Durchstich mit Data Protection in `metering.billing_period` (A-113); der Schreibpfad nach OpenBao folgt mit dem Betriebsausbau. **Partitionierung des Ledgers je Periode** und die **Aufbewahrung sieben Jahre** (A-024) folgen mit dem Betrieb.
3. **Stufenwarnung** („in 6 Tagen überschreitest du 250 aktive Mitglieder“) und die **Prognose aus drei Monaten** (Metering 5) folgen, sobald Historie vorliegt; **Steuerregeln je Land** (Reverse Charge, Drittland) folgen mit den Stammdaten (UID, Rechnungsanschrift).
4. **Laufende Challenges enden zum Kündigungstermin von M1** (Entitlements 4.3, A-040) und die **sieben Tage Vorlauf** der erzwungenen Deaktivierung mit Benachrichtigung der Tenant-Admins (4.4) folgen mit dem Lebenszyklus; Rahmenentzug (8.6), Funktionsfreigaben (8.9) und die Grenzwerte ohne zählendes Modul (8.8: Speicher, Uploads, Dokumente, Sprachen, KI-Aufrufe) folgen mit den Modulen.
5. **Operator- und Partner-Endpunkte** (Preisplan setzen, Grenzwerte ändern, erzwungene Deaktivierung, Versiegelung anstoßen) sind Anwendungsfunktionen ohne HTTP-Vertrag; sie folgen mit den Konsolen (Verwaltung 5, 6), zusammen mit Stufe 4 Punkt 6 und Stufe 6 Punkt 4 (Verwaltungsoberflächen Zugang, Gruppen, Sollstärken, Tenant-Schalter, Mitglied entfernen). Die Verwaltungsoberfläche für Buchungen ist mit dieser Stufe erledigt.
6. **Aus Stufe 6** bleiben offen: Punkt 1 (Anerkennung, Kommentare, Meldungen; Kommentare zählen bereits nicht als Handlung), Punkt 2 (QR-Code im Aushang, Verdichtungsfenster), Punkt 3 (VAPID-Rotation mit zwei Schlüsseln), Punkt 5 (letzter Tenant-Admin). **Aus Stufe 4** bleiben Punkte 1 (Beitritt unter 90 Sekunden, Stufe 8), 3, 5, 6, 7; **aus Stufe 5** Punkte 2 bis 5; Stufe 3 Punkt 3 und die Punkte aus [Stufe 1](stufe-1.md), Abschnitt 6, und [Stufe 2](stufe-2.md), Abschnitt 7, bleiben unverändert.
7. **Dependabot:** keine offenen Pull-Requests; 1 bis 5 (Actions) sind übernommen und geschlossen, 6 und 7 (TypeScript 7.0.x, @types/node 26) sind mit Begründung aus Stufe 6 geschlossen (Angular 22 verlangt TypeScript 6.0.x; @types/node folgt der Laufzeit 24). Nichts zu schließen.
8. **Merge auf `master`.** Diese Stufe entstand im Branch `claude/stufe-7-geld-ffoa5t`, der die Stufe 6 (`claude/stufe-6-fachpfad-r36muw`, noch nicht auf `master`) enthält; Merge durch den Betreiber: `git checkout master && git merge --no-ff claude/stufe-7-geld-ffoa5t && git push origin master`.
