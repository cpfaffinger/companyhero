# CompanyHero – Domänen und Schnittmengen

**Stand:** 20.09.2026  
**Status:** Verbindlicher Modulschnitt des modularen Monolithen gemäß A-012 im [Entscheidungsregister](architektur-entscheidungen.md). Dieses Dokument legt Grenzen, Eigentum und Schnittstellen fest. Die inhaltliche Ausarbeitung jeder Domäne erfolgt themenweise in eigenen Exporten.  
**Bezug:** [Backend](architektur-backend.md), [Integrationsregeln](architektur-integrationsregeln.md).

## 1. Grundregeln

- Eine Domäne ist ein Modul des Monolithen mit eigenem Projekt, eigenem Datenbankschema und eigenem DbContext. Sie besitzt ihre Daten und alle Schreiboperationen darauf.
- Andere Domänen greifen ausschließlich über öffentliche Anwendungsfunktionen oder Fachereignisse zu. Kein Modul liest fremde Tabellen.
- Querschnitte haben genau einen Eigentümer, der eine Schnittstelle bereitstellt. Alle Domänen verwenden diese Schnittstelle; keine Domäne implementiert einen Querschnitt selbst nach.
- Isolierte Dienste sind keine Module des Monolithen. Sie erhalten ausschließlich ausdrücklich freigegebene Daten über eine Schnittstelle.
- Architekturtests erzwingen die erlaubte Abhängigkeitsrichtung aus Abschnitt 6.

## 2. Domänen des Monolithen

| Domäne | Besitzt | Stellt bereit | Bezieht |
|---|---|---|---|
| **Identität und Zugang** | Personen, Anmeldewege (Anbieterverknüpfungen mit Issuer und Subject, Magic-Link-E-Mail, Passkeys, Wiederherstellungscodes), plattformweiter Identitätsindex, Sitzungen, Kiosk-Geräte und Gerätesitzungen, Kiosk-Kennungen und PINs, Beitritts- und Rollencodes, Offline-Freigaben, pseudonymisiertes Sicherheitsprotokoll (Anmeldungen, Fehlversuche) | geprüfte Identität einer Anfrage; Ausstellung und Widerruf von Sitzungen; Einlösung von Codes; Beitritt als Transaktion mit Organisation | Mitgliedschaftsprüfung von Organisation |
| **Organisation und Mandanten** | Organisationen (Operator, Partner, Tenant) mit Stammdaten und Lebenszyklus, Dimensionen und flache Gruppen, Mitgliedschaften mit Zuständen, Rollenzuweisungen nach Rollenkatalog, Sollstärken mit Stichtagshistorie, Partner-Voreinstellungen für Marke und Anmeldewege, Programmstart-Checkliste und Rollout-Fortschritt je Tenant | Tenant-Zugehörigkeit einer Person, Rollen und Gruppen für Autorisierung, Sollstärke als Bezugsgröße, Gruppe zum Beitragszeitpunkt, Rollout-Kennzahlen ab fünf | keine; Personen werden nur über ihre Kennung referenziert |
| **Marke und Theme** | Tenant-Theme-Dokumente mit Versionen (Produktname, Logos, Saatfarbe, Akzent, Anrede, Tonalität, Startbild, Willkommenstext, Bezeichnungen), abgeleitete und versionierte Tokensätze mit Ersetzungsliste, Plattformmarke und Standard-Theme, Textkatalog mit drei Tonalitätsvarianten je Sprache, Sperrliste der Begriffe, Manifest- und Icon-Erzeugung je Tenant und Kiosk | Tokensatz je Tenant und Modus; Vorschauableitung ohne Persistierung; Texte in Tonalität, Anrede und Sprache mit Bezeichnungen; Manifest je Tenant; Sperrliste für Textkatalog und Moderation | Organisation für Stammdaten und Partner-Voreinstellungen |
| **Entitlements** | Modulkatalog mit Kern und zwölf Modulen, Entitlement-Datensätze je Tenant und Modul mit Zuständen, Testphasen, Gültigkeit und append-only Historie, Rahmen und Voreinstellungen entlang Operator → Partner → Tenant, Funktionsfreigaben des Operators, Grenzwerte | Antwort auf „darf dieser Tenant dieses Modul jetzt nutzen“ für Navigation, API, Daten und Jobs; Ereignisse aktiviert, Testphase, gekündigt, inaktiv an Metering; Katalog mit Datenschutzhinweis | Organisation für Hierarchie und Tenant-Zustand; die Kostenvorschau vor einer Buchung setzt die Verwaltung aus Entitlements und Metering zusammen, Entitlements ruft Metering nicht auf |
| **Challenges** | Vorlagen auf drei Ebenen, Challenges mit sieben Achsen und versionierter Konfiguration, Teilnahmen, Beiträge mit Erfassungszeitpunkt und Gegenbuchungen, Baselines, Tagesdeckel, Ranglisten-Snapshots, Kollektivstände, Tenant-Belohnungen, Buddy-Paare, Vorgangskennungen und Idempotenzschlüssel für Beiträge | Kollektivstände mit Altersangabe, Ranglisten nach Sichtbarkeitsprüfung, Beitragserfassung, Ereignisse für Start, Meilenstein, Ende und Belohnung, Projektionen für die Arena | Fortschritt für Aktivitätsereignisse; Organisation für Gruppen und Sollstärken; Datenschutz für Sichtbarkeit; Entitlements; abgeleitete Werte aus dem Vault |
| **Fortschritt** | Aktivitätsereignisse und Gegenereignisse, Punktesaldo mit Tagesdeckeln, Serien und Serienschutz, Abwesenheiten ohne Grund, Abzeichenkatalog und Verleihungen, Stufen, täglicher Check-in, persönliches Tagesziel, Monatsaggregate | persönlicher Fortschritt einer Person; Ereignisse „Abzeichen verliehen“, „Stufe erreicht“, „Check-in“; Inaktivitätssignale an Benachrichtigungen | Ereignisse aus Challenges, Inhalten und Vault |
| **Feed und Inhalte** | Feed-Einträge mit Geltungsbereich und Verdichtung, Tenant- und Mitglieder-Beiträge, Kommentare, Anerkennungen, Meldungen und Moderationsentscheidungen, tenant-eigene Ergänzungen der Sperrliste für die Moderation, Inhalte in neun Typen auf drei Ebenen mit Versionen und Lizenzen, Sammlungen und Serien, Redaktionsplan, Medienableitungen, KI-Anbieterkonfiguration je Tenant, Favoriten, Fortsetzen, Verlauf, Quiz-Ergebnisse | Feed einer Person nach Sichtbarkeitsprüfung; Bibliothek und Suche; Inhaltsabschlüsse, Quiz und Anerkennungen als Handlungen; Inhaltsaggregate ab fünf | Ereignisse aller Domänen; Datenschutz; Entitlements; Organisation für Gruppen; Marke für Tonalität |
| **Benachrichtigungen** | Benachrichtigungseinträge mit Lesestatus, Push-Abonnements je Person und Gerät, VAPID-Schlüsselverwaltung, persönliche Kanal- und Kategorieeinstellungen, Erinnerungsfenster, Ruhezeiten, Tenant-Schalter und Kontingente, E-Mail-Transportkonfiguration auf Plattform- und Tenant-Ebene (SMTP, Amazon SES, SendGrid, Mailgun), Zustellstatus, Aushang- und Dokumenterzeugung, ICS-Export | Regelwerk von Fachereignis zu Zustellung; Zustellung über In-App, Push, E-Mail, Aushang und Kalender; Reichweitenvorschau für Autoren; Testversand und DNS-Prüfung für Absenderdomains | Ereignisse aller Domänen; Abwesenheit aus Fortschritt; Sichtbarkeit aus Datenschutz; Tokensatz und Tonalität aus Marke |
| **Metering und Abrechnung** | Metering-Ledger (append-only, je Periode partitioniert, ohne Personenbezug, periodisch gesalzene Zähler-Slots mit Salz in OpenBao), Metrikkatalog, Tagesaggregate, Preisregeln, Preispläne mit Versionen, Partner-Preisrahmen, Steuerregeln, Rechnungen, Gutschriften, Zahlungsstatus, Mahnstufen, Partnerabrechnungsmodell | Kostenvorschau und Simulation, Verbrauchsdetail als Tagesaggregat, Rechnungslauf mit PDF, CSV und EN-16931-XML, Provisionsgutschriften, Sperrvorschlag an den Operator | Metering-Ereignisse aller Domänen; Entitlements für Zustände und Testphasen; Organisation für Stammdaten, Land, UID und Partnermodell |
| **Datenschutz und Nachweis** | Sichtbarkeitseinstellungen je Person, Arena- und Buddy-Zustimmungen (unveränderlich), Prüfprotokoll (append-only), Fristenkatalog und Löschläufe, Auskunfts- und Exportaufträge, Austrittsfolgen, Tenant-Datenschlüssel, Betreiberdaten und versionierte Rechtstexte | Sichtbarkeitsregel und k-Anonymitätsprüfung als Leseregel; Protokollschnittstelle; Feldverschlüsselung; Lösch- und Exportabläufe; gültige Rechtstexte für App und Verwaltung | Daten aller Domänen über deren öffentliche Export- und Löschfunktionen |
| **Workshops und Events** (M9) | Anbieter, Angebote, Veranstaltungen mit Stationen, Anmeldungen mit Ticket-Token und Klarname-Freigabe, Wartelisten, Check-ins, Gutscheine, anonyme Rückmeldungen | Kalender je Geltungsbereich, Buchung mit Kostenvorschau, Ticket-Scan, Gutschein-Freischaltung als Belohnungsobjekt, Anbieterübersicht für den Operator | Challenges für Belohnungsbedingung; Organisation für Geltungsbereich und Standorte; Entitlements für M9; Metering für Preise; Datenschutz für Sichtbarkeit und Löschung |

## 3. Querschnitte und ihre Eigentümer

| Querschnitt | Eigentümer | Regel für alle Domänen |
|---|---|---|
| Tenant-Kontext und Transaktion | Plattforminfrastruktur | Jeder Request und jeder Job erhält genau einen geprüften Kontext; Datenzugriff nur innerhalb der Kontexttransaktion |
| Autorisierung | Plattforminfrastruktur mit Daten aus Identität, Organisation und Entitlements | Jede Operation prüft Tenant, Rolle, Modulzugang und Eigentum serverseitig |
| Sichtbarkeit und k-Anonymität | Datenschutz und Nachweis | Jede Leseoperation mit Personenbezug oder Aggregat läuft durch die Sichtbarkeitsregel; die restriktivere Einstellung gewinnt; Aggregate unter der Mindestzahl werden nicht ausgegeben |
| Prüfprotokoll | Datenschutz und Nachweis | Konfigurations-, Rollen- und Sichtbarkeitsänderungen werden über die Protokollschnittstelle geschrieben |
| Metering-Emission | Metering und Abrechnung | Jede abrechnungsrelevante Entität emittiert von Beginn an ein Ereignis mit Idempotenzschlüssel, auch ohne aktive Preisregel |
| Jobs und Fachereignisse | Plattforminfrastruktur | Einreihung nur in der Transaktion der fachlichen Änderung; Abonnenten sind idempotent |
| Idempotenz von Beiträgen | Challenges | Vorgangskennungen und Client-Idempotenzschlüssel liegen in einem Namensraum je Tenant und Person |

## 4. Isolierte Dienste

| Dienst | Eigener Datenspeicher | Erhält | Gibt ab |
|---|---|---|---|
| **Arena** | eigene Datenbank, eigene Zugangsdaten, nur intern erreichbar | Arena-Definitionen des Operators; je teilnehmendem Tenant Name, Logo, Größenklasse, Region, Branche; stündliche Projektionen: Pro-Kopf-Wert auf Sollstärke, Status ab fünf Beitragenden; pseudonyme Einzelwerte mit Arena-Token nur bei doppelter Zustimmung | Stände und Ereignisse an den Monolithen, der sie je Tenant cached und als Feed-Karten und Benachrichtigungen ausspielt; Details in [Arena](arena.md) |
| **Wearable-Vault** | eigene Datenbank mit Vault-eigenem Schlüssel, eigene Zugangsdaten, ausgehend nur Hersteller-Allowlist, eingehend nur OAuth-Callback und Webhooks | Verbindungsaufträge mit opakem Subjektschlüssel; Tagesübersichten und Schlafdauer der Hersteller | ausschließlich quantisierte Tageswerte (Schritte, aktive Minuten, Distanz, Schlafziel ja/nein, Datenstand) je Subjektschlüssel an den Monolithen; niemals Zeitreihen; Details in [Wearable-Vault](wearable-vault.md) |

Beide Dienste verwenden denselben Stack, dieselben Betriebsregeln und dieselbe Queue-Schnittstelle. Nur der Monolith spricht mit ihnen; die Mitglieder-App kennt sie nicht. Keiner der Dienste kennt Personen.

## 5. Schnittmengen

| Schnittmenge | Auflösung |
|---|---|
| Challenges ↔ Fortschritt | Ein Beitrag erzeugt in Fortschritt genau ein pauschales Aktivitätsereignis je Handlung. Messwerte wie Minuten oder Schritte bleiben in Challenges und fließen nie in Punkte, Stufen oder Anzeigen außerhalb ihrer Challenge. |
| Entitlements ↔ Metering | Zwei getrennte Systeme: Entitlements beantwortet den Zugang, Metering den Verbrauch. Ein Modul kann freigeschaltet und unbewertet sein; ein gekündigtes Modul kann Nachläufe haben. Eine gemeinsame Verwaltungsoberfläche, zwei Mechanismen. |
| Challenges ↔ Arena | Challenges berechnet die Projektion und übergibt sie als Ereignis. Die Arena hält keine Mitgliederdaten und keine Beitragssummen. |
| Wearable-Vault ↔ Challenges | Nur abgeleitete Tageswerte überschreiten die Grenze; die Zielauswertung geschieht im Vault. |
| Identität ↔ Organisation | Anmeldung und Mitgliedschaft sind getrennt. Eine geprüfte Identität ohne Mitgliedschaft hat keinen Tenant-Zugang; Rollen kommen aus Organisation, nie aus Anbieterclaims. |
| Marke ↔ Benachrichtigungen | Druckdokumente und E-Mails verwenden den gespeicherten Tokensatz und die Tonalität aus Marke und Theme. Keine eigene Farb- oder Textableitung in Benachrichtigungen. |
| Datenschutz ↔ alle lesenden Domänen | Sichtbarkeit ist eine zentrale Leseregel, keine Filterlogik je Feature. Feed, Challenges, Ranglisten und Verwaltungsansichten verwenden dieselbe Prüfung. |
| Datenschutz ↔ Metering | Der Ledger enthält keine Personen-IDs; Zähler-Slots werden je Abrechnungsperiode neu gesalzen. Das Verbrauchsdetail zeigt Aggregate, nie Einzelzeilen. |
| Kiosk und Offline ↔ Challenges | Beide Idempotenzregime enden im selben Eindeutigkeitsraum der Beitragserfassung. Die Erfassungslogik unterscheidet nicht nach Herkunft, nur die Kennungsvergabe. |
| Organisation ↔ Entitlements | Freischaltungen vererben sich entlang der Organisationshierarchie; ein Tenant bucht nur innerhalb dessen, was ihm zugänglich ist. Partnerrechte sind ausdrückliche Berechtigungen. |

## 6. Erlaubte Abhängigkeitsrichtung

```
Plattforminfrastruktur (Kontext, Autorisierung, Jobs, Ereignisse)
   ▲
   │ alle Domänen
   │
Identität ── Organisation ── Entitlements ── Marke und Theme
   ▲              ▲               ▲                ▲
   │              │               │                │
Datenschutz und Nachweis (Sichtbarkeit, Protokoll) ◄── alle lesenden Domänen
   ▲
   │
Fortschritt ◄── Challenges ◄── Feed und Inhalte
   ▲               ▲                ▲
   └── Benachrichtigungen (abonniert Ereignisse) ──┘
   └── Metering und Abrechnung (abonniert Metering-Ereignisse aller Domänen)
   └── Workshops und Events ──► Challenges (Gutschein als Belohnungsobjekt) ──► Fortschritt (Handlung „Veranstaltung besucht“)
       Challenges ─Ereignis─► Workshops (Gutschein-Freischaltung)

Isolierte Dienste: Arena ◄── Challenges (Projektion) · Wearable-Vault ──► Challenges (abgeleitete Werte)
```

Pfeile zeigen erlaubte Referenzen beziehungsweise Ereignisabonnements. Rückwärtsabhängigkeiten laufen ausschließlich über Fachereignisse, nie über Projektreferenzen.

## 7. Nachweise

- Architekturtests scheitern bei jeder Projektreferenz außerhalb von Abschnitt 6.
- Datenbanktests scheitern, wenn ein Modul ein fremdes Schema liest oder schreibt.
- Ein Beitrag erzeugt genau ein Aktivitätsereignis, genau ein Metering-Ereignis und einen Feed-Eintrag nach Sichtbarkeitsprüfung; Wiederholung erzeugt nichts davon erneut.
- Ein Tenant-Admin erhält aus keiner Domäne individuelle Aktivitätswerte einer anderen Person.
- Die Arena-Datenbank enthält nach einem Testlauf keine Personen-IDs und keine Beitragssummen.
