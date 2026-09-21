# CompanyHero – finales Konzept

Dieser Ordner enthält ausschließlich den gültigen Stand der Produkt- und Architekturplanung für CompanyHero. Jede Aussage hier ist eine getroffene Entscheidung oder eine daraus abgeleitete verbindliche Regel. Geänderte Entscheidungen werden ersetzt, nicht historisiert. Offene Detailfragen sind als solche benannt und dem Themengebiet zugeordnet, in dem sie entschieden werden.

Der Ordner `concept-entwurf-v0.1` ist ein früherer Arbeitsbestand mit Ideen. Er ist nicht verbindlich und dient nicht als Quelle für Entscheidungen.

Die Plattform entsteht ohne konkreten Kunden. Es gibt keine externen Stakeholder; Anforderungen stammen ausschließlich aus diesem Konzept. Betreiberdaten und Rechtstexte sind Konfiguration und werden nachträglich eingetragen.

## Vorgehen

- Themengebiete werden nacheinander ausgearbeitet; Christopher bestimmt Reihenfolge und Zeitpunkt.
- Zu jedem Themengebiet entsteht ein eigener Export in diesem Ordner.
- Entscheidungen werden im [Entscheidungsregister](architektur-entscheidungen.md) mit ID, Status, Umfang, Begründung, Folgen und Nachweisen geführt (A-010).
- „Angenommen“ bedeutet verbindlicher Plan. Technische Nachweise folgen im Durchstich und in der Umsetzung.

## Exporte

| Export | Inhalt | Status |
|---|---|---|
| [Entscheidungsregister](architektur-entscheidungen.md) | A-001 bis A-106 | angenommen, 20. und 21.09.2026 |
| [Backend-Architektur](architektur-backend.md) | C#/ASP.NET Core auf .NET 10 LTS, PostgreSQL mit RLS, modularer Monolith, logische Job-Queue in PostgreSQL, Sitzung und Anmeldung, Theme-Ableitung, Betrieb, Durchstich | beschlossen |
| [Frontend-Architektur](architektur-frontend.md) | Angular/TypeScript, Angular Material mit eigenem CSS und gemeinsamen Tokens, Offline-Bestand, Ladebudget, Referenzscreen | beschlossen |
| [Zuständigkeiten und Integrationsregeln](architektur-integrationsregeln.md) | Zuständigkeitsmatrix und Regeln K01 bis K19 für Gestaltung, Zustand, API, Offline, Kiosk, Build und Qualität | beschlossen |
| [Domänen und Schnittmengen](domaenen-und-schnittmengen.md) | Modulschnitt des Monolithen, Querschnitte mit Eigentümern, isolierte Dienste, Schnittmengen, Abhängigkeitsrichtung | beschlossen |
| [Zugang und Identität](zugang-und-identitaet.md) | Beitritt, Rollencodes, Anmeldewege, Kontomodell, Sitzungen, Kiosk, Offline-Freigabe, Austritt | beschlossen |
| [Datenschutz und Nachweis](datenschutz-und-nachweis.md) | Rollenverteilung, nicht konfigurierbare Regeln, Sichtbarkeitsmodell, k-Anonymität, Fristen und Löschung, Protokolle, Einsichtsrolle, Export, technische Maßnahmen | beschlossen |
| [Betrieb](betrieb.md) | Betriebsziele, Hosting und Orchestrierung, Plattformkomponenten, Lieferkette, Backups, Domains, E-Mail, Push, Betriebsprozesse | beschlossen; Hosting und Domain stellt der Betreiber bereit |
| [Organisation und Mandanten](organisation-und-mandanten.md) | Organisationsmodell, Tenant-Lebenszyklus, Gruppen und Sollstärken, Mitgliedschaft, Rollenkatalog mit Rechtematrix, Partner-Ebene | beschlossen |
| [Challenges](challenges.md) | Sieben Achsen und drei Blöcke, Metrikkatalog, Erfassung und Korrektur, Lebenszyklus und Kadenz, Vorlagenbibliothek, Wertung und Ranglisten, Arena-Projektion, Buddy-System | beschlossen |
| [Fortschritt](fortschritt.md) | Aktivitätsereignisse und Punkte, Serie und Abwesenheit, Abzeichen, Stufen, Check-in, persönliche Anzeigen, Rückblicke, Inaktivitätssignale | beschlossen |
| [Feed und Inhalte](feed-und-inhalte.md) | Feed-Modell, Beiträge, Kommentare, Anerkennung, Moderation, Inhaltsmodell, Redaktion und Redaktionsplan, Medienverarbeitung, KI-Authoring mit eigenen Zugangsdaten, Entdecken, Inhaltsauswertung | beschlossen |
| [Benachrichtigungen](benachrichtigungen.md) | Kanäle und Tenant-Schalter, Web Push mit Service Worker und VAPID, Kategorien und Regelwerk, Zusammenspiel mit Beiträgen und Feed, persönliche Einstellungen und Erinnerungen, E-Mail, Aushang, Kalender, Zustellpipeline und Auswertung | beschlossen |
| [Entitlements](entitlements.md) | Modulkatalog mit Kern und zwölf Modulen, Entitlement-Modell mit Zuständen und Rahmen, Buchung, Testphase und Kündigung, Durchsetzung in Navigation, API, Daten und Jobs, Funktionsfreigaben und Grenzwerte | beschlossen |
| [Metering und Abrechnung](metering-und-abrechnung.md) | Ledger mit Pseudonymisierung, Metrikkatalog und Emission, aktives Mitglied, Preisregeln, Pläne und Partnerrahmen, Kostenvorschau und Verbrauchsdetail, Rechnungslauf, Steuern, Zahlung, Mahnwesen, Partnerabrechnung | beschlossen |
| [Marke und Theme](marke-und-theme.md) | Gestaltungshaltung, Tokenmodell und Design-System, Tenant-Theme-Kontrakt und Ableitung, Plattformmarke und Co-Branding, PWA-Manifest je Tenant, Tonalität und Textkatalog, Barrierefreiheit und Großflächenmodus | beschlossen |
| [Workshops und Events](workshops-und-events.md) | Angebotsmodell mit Anbietern, Angeboten, Veranstaltungen und Stationen, Buchung und Storno, Anmeldung mit Ticket-QR und Check-in, verdiente Belohnung als Gutschein, Rückmeldung, Auswertung, Benachrichtigungen und Metering | beschlossen |
| [Arena](arena.md) | Isolierter Dienst, Schnittstellen, Arena-Modell mit Ligen und Lebenszyklus, Beitritt, Projektion, Anzeige in Plattform-Schale, persönliche Zustimmung, Anerkennung | beschlossen |
| [Wearable-Vault](wearable-vault.md) | Isolierter Dienst mit Subjektmodell, Adaptermodell, Verbindung, bezogene Daten und Ableitung, Transparenz, Rechte, Löschung, Programmantrag | beschlossen |
| [Onboarding und Rollout](onboarding-und-rollout.md) | Beitritt einer Person in zwölf Schritten, Kiosk-Variante, geführter Programmstart, Rollout-Choreografie, Botschafter-Onboarding, Werkzeuge, Kennzahlen, verbotene Muster | beschlossen |
| [Verwaltung und Konsolen](verwaltung-und-konsolen.md) | Gemeinsame Regeln, Verwaltung des Tenants mit Bereichen je Rolle, Partner-Konsole, Operator-Konsole | beschlossen |
| [Inhaltsmodule](inhaltsmodule.md) | M3 Bewegung, M4 Ergonomie, M5 Wissen, M6 Regeneration mit Stimmungs-Check-in, M7 Ernährung | beschlossen |
| [Mehrsprachigkeit](mehrsprachigkeit.md) | Sprachen, Übersetzungsquellen und Fallbacks, Redaktion, Kündigung | beschlossen |
| [Verzeichnis und Netzwerk](verzeichnis-und-netzwerk.md) | SCIM ohne Personenimport, Deaktivierungssignal, IP-Regeln für die Verwaltung, Protokollexport | beschlossen |
| [Technischer Durchstich](technischer-durchstich.md) | Umfang, Produktwahlen, acht Stufen, Arbeitsweise, Abschlusskriterien | beschlossen |

## Stand

Alle Themengebiete des Konzepts sind ausgearbeitet. Der nächste Schritt ist der technische Durchstich gemäß A-106; danach die Umsetzung der übrigen Domänen in der vom Betreiber festgelegten Reihenfolge. Neue Themen entstehen als eigene Exporte mit Registereinträgen.
