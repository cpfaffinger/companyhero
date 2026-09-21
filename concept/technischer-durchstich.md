# CompanyHero – Technischer Durchstich

**Stand:** 21.09.2026  
**Status:** Beschlossen gemäß A-106 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung.  
**Art:** Plan für den ersten technischen Nachweis der Architektur; keine Roadmap, keine Termine.  
**Bezug:** alle Exporte in `concept/`.

„Angenommen“ bedeutet verbindlicher Plan, nicht technisch bewiesen. Der Durchstich ist der schmalste Pfad durch das Produkt, der jede kritische Grenze berührt, und liefert die Beweise, bevor Produktcode in der Breite entsteht.

## 1. Umfang

### 1.1 Enthalten

Ein Tenant wird vom Operator angelegt, ein Tenant-Admin löst den Rollencode ein, richtet Gruppen, Sollstärke, Marke und Anmeldewege ein, übernimmt zusätzlich die Rolle Programm-Manager, startet die Kickoff-Challenge und registriert einen Kiosk. Personen treten ohne E-Mail bei, erfassen Beiträge am Handy (online und offline) und am Kiosk, sehen Fortschritt und Feed, erhalten Push. Metering zählt, die Kostenvorschau rechnet, ein Rechnungsentwurf entsteht. Zwei Tenants beweisen die Isolation. Alles läuft in Compose mit Caddy, PostgreSQL, Garage, OpenBao und Beobachtung.

Domänen im Durchstich: Identität und Zugang, Organisation, Marke und Theme, Entitlements (Kern plus M1), Challenges (Sammelziel, Häkchen), Fortschritt, Feed (System-Ereignisse, ein Tenant-Beitrag), Benachrichtigungen (In-App, Push, E-Mail über SMTP), Metering und Abrechnung (Ledger, Kostenvorschau, Rechnungsentwurf), Datenschutz (Sichtbarkeit, Mindestzahl, Prüfprotokoll, Austritt).

### 1.2 Nicht enthalten

Inhaltsmodule M3 bis M7, Arena, Wearable-Vault, Workshops und Events, Firmenkanal, Mehrsprachigkeit, Verzeichnis und Netzwerk, Partner-Konsole, KI-Authoring, Rechnungsversand, Mahnwesen. Ihre Nachweise folgen bei ihrer Umsetzung.

## 2. Produktwahlen im Durchstich

| Wahl | Anforderung aus dem Register | Entscheidung |
|---|---|---|
| Jobbibliothek | transaktionale Einreihung, Leases, Retries, Dead-Letter, Tenant-Fairness auf PostgreSQL (A-006) | zuerst eine gepflegte PostgreSQL-basierte .NET-Bibliothek prüfen; erfüllt keine die Anforderungen ohne Umgehung, kleine eigene Umsetzung auf `SKIP LOCKED`; Ergebnis mit Begründung im Register |
| OpenAPI-Clientgenerator | Dezimal- und ID-Strings, strict-Typen, keine manuelle Änderung (A-009) | Generator, der die Vertragsregeln aus K13 ohne Nachbearbeitung erzeugt; Nachweis mit echten Antworten |
| Theme-Ableitung | Material-3-Verfahren im HCT-Farbraum, deterministisch, Referenzwerte (A-013, A-077) | .NET-Umsetzung des veröffentlichten Verfahrens, Bibliothek oder eigene Portierung; Nachweis gegen Referenzpaletten |
| OIDC und WebAuthn | Cookie-Sitzung, PKCE, Claim-Minimierung, Passkeys mit Wiederherstellungscode (A-007, A-015) | ASP.NET-Core-Middleware für OIDC; WebAuthn-Bibliothek für .NET; Nachweis mit Microsoft, Google, generischem OIDC und zwei Passkey-Plattformen |

Jede Wahl wird nach dem Nachweis als Registereintrag mit Version festgehalten.

## 3. Reihenfolge

| Stufe | Inhalt | Nachweise aus |
|---|---|---|
| 1 Fundament | Repository, CI, Compose, Images, Migrations-Container, OpenBao, Caddy, Beobachtung, Backup mit Wiederherstellung auf Staging | Betrieb 9.1, 9.4, 9.5, 9.7 |
| 2 Isolation | zwei Tenants, RLS mit Laufzeitrechten, Tenant-Kontext je Request und Job, Modulgrenzen mit Architekturtests | Backend 11.1 bis 11.3, 11.9; Domänenkarte 7 |
| 3 Queue und Idempotenz | logische Queue, Outbox-Kopplung, Worker-Replikate, Kiosk-Vorgangskennung und Offline-Idempotenzschlüssel | Backend 11.4, 11.5; Entscheidungen A-005, A-006, A-009 |
| 4 Zugang | Beitritt mit Code, Passkey, Wiederherstellungscode, Magic-Link, drei OIDC-Anbieter, Kiosk-Gerät mit Kennung und PIN, Sitzungslaufzeiten, Austritt | Zugang 10.1 bis 10.9 |
| 5 Vertrag und Oberfläche | OpenAPI-Client, Referenzscreen mit zwei Firmenmarken und Plattformmarke, hell und dunkel, Großflächenmodus, Ladebudget, Manifest je Tenant | Integrationsregeln 6; Frontend 7; Marke 9.1 bis 9.5, 9.7 |
| 6 Fachpfad | Organisation, Kickoff-Challenge, Beiträge am Handy und Kiosk, Fortschritt, Feed, Sichtbarkeit und Mindestzahl, Push und E-Mail | Organisation 7; Challenges 9; Fortschritt 8; Feed 12.1 bis 12.5; Benachrichtigungen 10; Datenschutz 9.1 bis 9.5 |
| 7 Geld | Ledger, Slots mit Salzvernichtung, Kostenvorschau, Testphase, Rechnungsentwurf | Metering 9.1 bis 9.8; Entitlements 8.1 bis 8.5 |
| 8 Onboarding | vollständiger Beitritt unter 90 Sekunden, Programmstart-Checkliste, Rollout-Fortschritt | Onboarding 6 |

Jede Stufe endet mit grüner CI und einem Abnahmeprotokoll je Nachweis. Eine Stufe beginnt erst, wenn die vorherige abgenommen ist; Ausnahme sind Stufen 4 und 5, die parallel laufen dürfen.

## 4. Arbeitsweise

- Test Driven Development nach Backend 9: Akzeptanzfall, roter Test aus fachlichem Grund, kleinste Implementierung, Refaktorieren, Architekturprüfung.
- Commits direkt auf `master`, ein Commit oder eine zusammenhängende Commit-Folge je Nachweis; CI läuft nach jedem Push (A-030).
- Agentische Entwicklung mit derselben CI und denselben Lint-Grenzen; die Regeln aus den Integrationsregeln sind Lint- und Abhängigkeitsprüfungen ab Stufe 1.
- Testdaten synthetisch; keine echten Personen; feste Testuhren; Testcontainer mit echtem PostgreSQL und Laufzeitrechten.
- Jede Abweichung vom Konzept, die der Durchstich erzwingt, ist eine Registeränderung im selben Commit, nicht danach.

## 5. Abschlusskriterien

1. Alle in Abschnitt 3 genannten Nachweise sind als automatisierte Tests in CI grün oder als dokumentierte manuelle Abnahme (Referenzscreen, Wiederherstellungsübung, Ladezeit auf Mittelklassegerät) mit Protokoll abgelegt.
2. Die vier Produktwahlen stehen mit Version im Register.
3. Das Ladebudget von 180 KB ist gemessen; eine Abweichung ist als Registerentscheidung dokumentiert (K17).
4. Der Neuaufbau aus Images, Konfiguration und Backups gelingt auf Staging innerhalb der RTO (A-027).
5. Ein Lasttest mit Planungslast auf Staging erfüllt die Werte aus Betrieb 9.2.
6. Jede Entscheidung, deren Nachweise erbracht sind, erhält im Register den Zusatz „technisch nachgewiesen“ mit Datum.

Danach beginnt die Umsetzung der übrigen Domänen in der Reihenfolge, die der Betreiber festlegt; das Konzept schreibt keine Reihenfolge vor.

## 6. Nachweise dieses Themas

1. Abnahmeprotokoll je Stufe liegt im Repository; CI-Lauf referenziert.
2. Register enthält die vier Produktwahlen mit Version und die Zusätze „technisch nachgewiesen“.
3. Jeder deployte Commit hat einen grünen CI-Lauf; Stichprobe der Historie.
