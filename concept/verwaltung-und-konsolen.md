# CompanyHero – Verwaltung und Konsolen

**Stand:** 21.09.2026  
**Status:** Beschlossen gemäß A-096 bis A-098 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung.  
**Art:** Querschnittsthema über alle Domänen; legt Oberflächen, Bereiche und Regeln der drei Konsolen fest.  
**Bezug:** [Organisation und Mandanten](organisation-und-mandanten.md), [Entitlements](entitlements.md), [Metering und Abrechnung](metering-und-abrechnung.md), [Datenschutz und Nachweis](datenschutz-und-nachweis.md).

Der Firmenvertreter ist eine andere Person mit einem anderen Job als das Mitglied. Deshalb gibt es drei Konsolen aus derselben Codebasis: die **Verwaltung** des Tenants, die **Partner-Konsole** und die **Operator-Konsole**. Alle drei sind getrennte Feature-Einstiege unter derselben Origin und werden nie in das erste Ladepaket der Mitglieder-App aufgenommen.

## 1. Gemeinsame Regeln

- **Rechte aus der Rechtematrix** (A-036). Jeder Bereich ist nur für Rollen sichtbar, die ihn verwenden dürfen; die API prüft unabhängig (K14).
- **Sensible Aktionen** verlangen eine Anmeldung, die nicht älter als 15 Minuten ist (A-015): Buchungen, Rollencodes, Anbieter, Kiosk-Geräte, Marke, Rechnungsdaten, Anmeldewege, Arena-Beitritt, Veranstaltungsbuchung.
- **Jede Änderung** steht im Prüfprotokoll mit Klarname der Rolle (A-025).
- **Keine individuellen Aktivitätswerte, nirgends.** Aggregate ab fünf, keine Inaktivitätslisten, keine Sortierung nach Aktivität (A-021, A-023).
- **Vorschau vor Start:** Challenges, Marke, Ankündigungen und Module werden vorher so gezeigt, wie Mitglieder sie sehen.
- **Kosten vor dem Klick:** jede kostenwirksame Aktion zeigt die Auswirkung aus der Kostenvorschau (A-073).
- **Nicht freigeschaltete Module** erscheinen nur im Modulkatalog der Verwaltung, nie als ausgegraute Bereiche.
- **Desktop-first, responsiv.** Aufgaben von Botschaftern und Organisatoren (Moderation eigener Gruppen, Gruppen-Challenges, Ticket-Scan) liegen zusätzlich in der Mitglieder-App unter „Meine Aufgaben“.
- **Layout** nach Design-System in Tenant-Marke (Verwaltung) beziehungsweise Plattform-Marke (Partner- und Operator-Konsole).

## 2. Verwaltung des Tenants (A-096)

| Bereich | Inhalt | Rollen |
|---|---|---|
| **Übersicht** | Aktivierungsquote als Schätzgröße gegen Sollstärke, aktive Challenge mit Kollektivstand, Rollout-Fortschritt, nächste empfohlene Handlung, offene Aufgaben (Meldungen, Pflichtschritte, Gutscheinablauf). Keine Gesundheitskennzahlen. | Tenant-Admin, Programm-Manager |
| **Programmstart** | geführte Checkliste (A-094), bis der Rollout freigegeben ist; danach als Rollout-Werkzeuge | Tenant-Admin, Programm-Manager |
| **Mitglieder und Gruppen** | Dimensionen, Gruppen, Sollstärken mit Stichtag, CSV-Import; Mitgliederliste mit Anzeigename, Gruppen, Rolle, Beitritt; Rollencodes ausstellen und entziehen; Mitglied entfernen (A-034, A-035) | Tenant-Admin; Programm-Manager ohne Mitgliederliste und Rollen außer Botschafter |
| **Challenges** | Vorlagenbibliothek, Wizard mit Achsen, Vorschau, Kadenzkalender, laufende und beendete Challenges, Belohnungen einlösen, Vorlagenfreigabe für Botschafter, Arena-Übersicht (A-038 bis A-042) | Programm-Manager; Arena-Beitritt Tenant-Admin |
| **Inhalte** | Bibliothek freischalten und ausblenden, eigene Inhalte, Redaktionsplan, Serien, Medienverarbeitung, KI-Authoring, Inhaltsauswertung ab fünf (A-052 bis A-057) | Programm-Manager, Redakteur |
| **Feed** | Ankündigungen und Beiträge verfassen, anheften, planen; Moderationswarteschlange; Sperrliste; Kommentar- und Beitragsschalter (A-050, A-051) | Programm-Manager; Redakteur im Firmenkanal; Botschafter eigene Gruppen |
| **Veranstaltungen** | Katalog, Anfragen, Buchungen, Organisator, Anmeldezahlen ab fünf, Gutscheine (A-082 bis A-085) | Programm-Manager; Buchung Tenant-Admin |
| **Benachrichtigungen** | Tenant-Schalter je Kanal und Kategorie, Ruhezeit, Kontingente, Auswertung ab fünf, Aushang-Zettel (A-058 bis A-063) | Programm-Manager, Tenant-Admin |
| **Module und Kosten** | Modulkatalog mit Vorschau und Datenschutzhinweis, Buchen, Testphase, Kündigen, Kostenvorschau, Verbrauchsdetail aggregiert, Rechnungen, Metrikdefinitionen (A-064 bis A-066, A-073) | Tenant-Admin |
| **Marke und Sprache** | Theme-Dokument, Live-Vorschau, Kontrastbericht, Versionen, Tonalität, Anrede, Bezeichnungen, Sprachen (A-077, A-080) | Tenant-Admin |
| **Zugang** | Anmeldewege und Anbieter, Anbieterzwang, Kiosk-Geräte, Sitzungen des Tenants beenden (A-015, A-018) | Tenant-Admin |
| **Datenschutz** | nur lesend: geltende Regeln und Schwellen, was der Admin sehen kann und was nicht, Auftragsverarbeitungsvertrag, Unterauftragsverarbeiterliste, Löschfristen, Tenant-Export (A-025) | Tenant-Admin, Einsichtsrolle |
| **Prüfprotokoll** | alle Konfigurations-, Rollen- und Zugangsänderungen, Support-Zugriffe, filterbar, exportierbar | Tenant-Admin, Einsichtsrolle |
| **Einsicht** | Ansicht der Einsichtsrolle: aktive Module und Kanäle, Regeln, tenantweite Quote ab fünf, verwendete Metriken ohne Beträge, Gutscheine, Prüfprotokoll; nichts änderbar (A-025) | Einsichtsrolle |

Die Verwaltung zeigt jeder Rolle beim Öffnen ihre offenen Aufgaben zuerst.

## 3. Partner-Konsole (A-097)

| Bereich | Inhalt |
|---|---|
| Tenants | anlegen mit Stammdaten, ersten Tenant-Admin-Rollencode ausstellen, Zustand (aktiv, gesperrt, gekündigt), sperren und kündigen (A-033) |
| Rahmen | erlaubte Module, Testphasen, Preisrahmen innerhalb des Operator-Rahmens, Voreinstellungen für Marke und Anmeldewege (A-037, A-065, A-072) |
| Vorlagen und Inhalte | Partnervorlagen für Challenges, Partnerinhalte, Partneranbieter für Veranstaltungen (A-041, A-052, A-082) |
| Abrechnung | Mengen und Beträge je Tenant, Provisionsgutschriften oder Sammelrechnung (A-075) |
| Prüfprotokoll | eigene Aktionen und Tenant-Ereignisse auf Konfigurationsebene |

Nie sichtbar: Mitgliederlisten, Inhalte der Tenants, Challenges, Quoten, Gruppen, Sollstärken. Die Partner-Konsole läuft in Plattform-Schale.

## 4. Operator-Konsole (A-098)

| Bereich | Inhalt |
|---|---|
| Organisationen | Partner und Tenants anlegen, Zustände, Sperren, Kündigungen, Region und Branche |
| Module und Preise | Modulkatalog, Listenpreisplan, Tenant-Pläne, Partner-Rahmen, Steuerregeln, Grenzwerte, Funktionsfreigaben (A-064 bis A-068, A-072) |
| Abrechnung | Versiegelung, Rechnungsentwürfe, Freigabe, Versand, Zahlungseingänge, Mahnstufen, Sperrvorschläge, Gutschriften (A-074, A-075) |
| Arena | Arenen anlegen, Ligen, Moderation, Stände (A-088) |
| Vorlagen und Inhalte | Plattformvorlagen, Plattforminhalte, Serien, Redaktion mit Vier-Augen-Freigabe (A-041, A-053) |
| Textkatalog und Marke | drei Tonalitätsvarianten je Schlüssel und Sprache, Sperrliste, Plattformmarke, Standard-Theme, Abzeichenkatalog (A-046, A-078, A-080) |
| Rechtstexte und Betreiberdaten | versionierte Rechtstexte, Betreiberdaten, Nachweisdokumente (A-020) |
| Anbieter | Anbieter und Angebote für Veranstaltungen, Anbieterübersicht, Rückmeldungen (A-082, A-086) |
| Hersteller | Wearable-Hersteller freischalten, Programmstatus, Scopes (A-091) |
| Anmeldeanbieter | plattformweite Registrierungen für Microsoft und Google (A-015) |
| E-Mail | Plattformtransport, Zustellfehler (A-032) |
| Support | Zugriff auf Tenant-Konfiguration nur mit Vorgangsnummer, 24 Stunden, protokolliert; nie Einzelwerte, nie Vault (A-025) |
| Betrieb | Zustellfehlerquoten, Job-Queue-Kennzahlen, Medienverarbeitung, Dead-Letter-Replay (A-006, A-029) |

Operator-Rollen sind Personen der Operator-Organisation mit Passkey oder externem Anbieter (A-036). Operator-Support sieht nur, was A-025 erlaubt. Die Konsole läuft in Plattform-Schale.

## 5. Schnittmengen dieses Themas

| Schnittmenge | Festlegung |
|---|---|
| Konsolen ↔ Frontend | getrennte Feature-Einstiege, Lazy Loading, nicht im ersten Ladepaket (K17); Tenant-Marke in der Verwaltung, Plattform-Marke in Partner- und Operator-Konsole |
| Konsolen ↔ Zugang | privilegierte Rollen nur mit Passkey oder Anbieter; frische Anmeldung für sensible Aktionen |
| Konsolen ↔ Datenschutz | Aggregate ab fünf, keine Einzelwerte, Prüfprotokoll, Datenschutzbereich nur lesend, Einsichtsrolle |
| Konsolen ↔ Metering | Kostenvorschau vor jeder kostenwirksamen Aktion; Rechnungslauf beim Operator |
| Konsolen ↔ Mitglieder-App | „Meine Aufgaben“ für Botschafter und Organisatoren |

## 6. Nachweise

1. Produktionsbuild der Mitglieder-App enthält keine Verwaltungskomponenten im ersten Ladepaket.
2. Jede „–“-Zelle der Rechtematrix ist in der Oberfläche unsichtbar und über die API abgelehnt.
3. Sensible Aktion ohne frische Anmeldung wird abgelehnt; erfolgreiche Aktion steht im Prüfprotokoll.
4. Kein Bereich zeigt individuelle Aktivitätswerte; Aggregate unter fünf werden ausgeblendet.
5. Partner-Konsole ohne Zugriff auf Mitglieder, Inhalte, Challenges, Quoten; Operator-Support nur mit Vorgangsnummer.
6. Botschafter erledigt Moderation, Gruppen-Challenge und Ticket-Scan in der Mitglieder-App.
