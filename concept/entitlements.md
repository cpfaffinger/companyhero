# CompanyHero – Entitlements

**Stand:** 20.09.2026  
**Status:** Beschlossen gemäß A-064 bis A-068 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung.  
**Domäne:** Entitlements gemäß [Domänenkarte](domaenen-und-schnittmengen.md).  
**Bezug:** [Organisation und Mandanten](organisation-und-mandanten.md), [Feed und Inhalte](feed-und-inhalte.md), [Challenges](challenges.md), [Datenschutz und Nachweis](datenschutz-und-nachweis.md).

Entitlements beantworten genau eine Frage: **Darf dieser Tenant dieses Modul jetzt nutzen?** Die Antwort steuert Navigation, API und Datenzugriff. Was ein Modul kostet, beantwortet die Domäne Metering und Abrechnung. Beide Systeme teilen eine Verwaltungsoberfläche und nichts sonst.

## 1. Grundsätze

- Ein **Modul** ist eine Einheit aus Funktion, Sichtbarkeit und Preis. Es wird je Tenant freigeschaltet, ist jederzeit zubuchbar und jederzeit zum Monatsende kündbar.
- Der **Kern** ist immer aktiv und nicht abwählbar.
- **Nicht freigeschaltete Module sind unsichtbar**, nicht ausgegraut. Kein Schloss, kein deaktivierter Menüpunkt, kein Werbebanner in der Mitglieder-App. Buchbar sind Module ausschließlich in der Verwaltung, dort mit Vorschau, Datenschutzhinweis und Preis.
- **Entitlement und Metering sind getrennt.** Ein Modul kann aktiv und unbewertet sein (Testphase, Kulanz), und ein gekündigtes Modul kann Nachläufe in der Abrechnung haben. Entitlements prüfen nie Zahlungen; Zahlungsverzug wird auf Tenant-Ebene über die Sperre behandelt (A-033).
- Module werden **je Tenant** freigeschaltet, nicht je Gruppe.
- Module **vererben sich** über den Partner-Rahmen: Ein Tenant bucht nur innerhalb dessen, was ihm zugänglich gemacht wurde.

## 2. Modulkatalog (A-064)

### 2.1 Kern (immer aktiv)

| Bestandteil | Umfang |
|---|---|
| Identität und Zugang | Beitritt per Code, alle Anmeldewege einschließlich externer Anbieter, Passkey, Magic-Link, Wiederherstellungscode, Kiosk mit Gerät- und Personensitzung, Anbieterzwang, Sitzungen (A-014 bis A-019) |
| Organisation | Gruppen in bis zu drei Dimensionen, Sollstärken, Rollen, Rollencodes, Mitgliederliste ohne Aktivitätsdaten (A-033 bis A-036) |
| Profil und Fortschritt | Check-in, Punkte, Serie, Abwesenheit, Abzeichen, Stufen, Tagesziel, Rückblicke (A-044 bis A-048) |
| Feed | System-Ereignisse, Tenant-Beiträge durch Programm-Manager, Redakteur und Botschafter mit Ankündigungen, Mitglieder-Beiträge, Kommentare, Anerkennung, Moderation (A-049 bis A-051) |
| Benachrichtigungen | In-App, Web Push, E-Mail, Aushang, Kalender mit allen Kategorien und Schaltern (A-058 bis A-063) |
| Marke und Tonalität | Name, Logo, Saatfarbe, Anrede, Tonalität, Bezeichnungen, Theme-Ableitung (A-013) |
| Datenschutz und Nachweis | Sichtbarkeitsstufen, Zustimmungen, Prüfprotokoll, Einsichtsrolle, Export, Austritt (A-020 bis A-026) |
| Rollout-Werkzeuge | Aushang-PDF mit QR, wöchentlicher Aushang-Zettel, Ankündigungstexte in Tonalität, Botschafter-Onboarding, Kickoff-Vorbelegung |
| PWA | Installierbarkeit, Offline-Freigabe, Offline-Erfassung |
| Redaktion für Tenant-Beiträge | Editor, Zeitsteuerung, optional KI-Authoring mit eigenen Zugangsdaten (A-057) |
| Verwaltung | Modulkatalog, Kostenvorschau, Rechnungen, Datenschutzansicht nur lesend, Prüfprotokoll |

Der nackte Kern ist ein Firmen-Feed mit Fortschrittsanzeige. Er funktioniert, wird aber im Vertrieb nur zusammen mit Challenges geführt; das ist Preis- und Vertriebsgestaltung, kein Entitlement-Zwang.

### 2.2 Module

| Modul | Mitglied sieht | Verwaltung konfiguriert | Abhängigkeit | Datenschutzhinweis im Katalog |
|---|---|---|---|---|
| **M1 Challenges** | Bereich „Challenges“, Kollektivstände, Ranglisten nach Schwelle, Buddy-System (A-038 bis A-043) | Challenges aus Vorlagen oder frei, Vorlagenfreigabe, Kadenz, Belohnungen | keine | Sichtbarkeit je Person begrenzt jede Challenge; Ranglisten erst ab fünf und 40 % |
| **M2 Arena** | Arena-Stand als Pro-Kopf-Wert je Firma in Plattform-Schale, optional pseudonyme Einzelrangliste | Beitritt je Arena, Einzelränge erlauben | M1 | Es verlässt den Tenant nur der Pro-Kopf-Wert; unter fünf Beitragenden kein Wert |
| **M3 Bewegung** | Kategorie Bewegung in „Entdecken“, Schnelleinstieg über Situation und Körperregion, Wochenplan, Favoriten | Sammlungen freischalten, eigene Videos und Übungen, Startempfehlung | keine | Verlauf nur für die Person; Nutzung nur als Aggregat ab fünf |
| **M4 Ergonomie** | Kategorie Ergonomie, geführter Arbeitsplatz-Check mit Maßen, abgeleitete Übungen, Wiederholung nach drei Monaten | Varianten je Arbeitsplatztyp | keine, M3 empfohlen | Ergebnisse feldverschlüsselt und nur für die Person; Tenant erhält Aggregate ab fünf für den Arbeitsschutz |
| **M5 Wissen** | Kategorie Wissen, Artikel, tägliche Kurzfakten im Feed, Quiz mit Handlung, Lese-Serie | Redaktionsplan, eigene Beiträge, Autorenprofile | keine | Quiz-Ergebnisse nur für die Person |
| **M6 Regeneration** | Kategorie Regeneration, Atemübung mit Animation, Audio 2 bis 15 Minuten, privater Stimmungs-Check-in | Freischaltung, Empfehlungen je Schichtmodell | keine | Stimmungs-Check-in feldverschlüsselt, ausschließlich privat, nie aggregiert |
| **M7 Ernährung** | Kategorie Ernährung, Rezepte, Zubereitungsvideos, Trinkerinnerung, Gewohnheitsvorlagen | Freischaltung, eigene Rezepte | keine | keine Kalorien, Gewichtsziele oder Ernährungs-Scores |
| **M8 Wearables** | „Gerät verbinden“, Quelle und Synchronisationszeitpunkt, automatische Tageswerte als Handlung und Challenge-Beitrag | nur die Buchung; die Verwaltung sieht nie, wer verbunden hat | keine, M1 empfohlen; buchbar nur, wenn der Operator mindestens einen Hersteller freigegeben hat | Rohdaten im isolierten Vault, 90 Tage; nur abgeleitete Tageswerte verlassen ihn; keine KI |
| **M9 Workshops und Events** | Bereich „Firma“ mit Terminen, Anmeldung, „freigespielt“-Karte für verdiente Belohnungen | Katalog, Buchung, Teilnehmerlisten, Verknüpfung Challenge → Belohnung | M1 | Belohnungen immer kollektiv; Anmeldung unter Anzeigename mit Ticket-QR; Teilnehmerliste nur für den Organisator, nicht exportierbar, nach 30 Tagen gelöscht; Preise je Termin oder Platzkontingent, nie je Teilnehmer |
| **M10 Firmenkanal** | Bereich „Firma“ mit Beiträgen von HR, Betriebsrat und Standorten, Dokumente, Termine mit ICS | Firmenkanal-Rechte je Gruppe für die im Kern vorhandene Redakteursrolle, Zielgruppensteuerung, Dokumentkategorien | keine | Dokumente ohne Personenbezug; Zielgruppen über Gruppen |
| **M11 Mehrsprachigkeit** | Sprachwahl im Beitritt, Oberfläche und Inhalte in gewählter Sprache | aktive Sprachen | keine | keine |
| **M12 Verzeichnis und Netzwerk** | nichts Sichtbares | Übernahme von Gruppen und Sollstärken aus dem Verzeichnis, Deaktivierungssignal je Identität nur mit Subject und Status, IP-Regeln für die Verwaltung, Protokollexport | keine | kein Personenimport; das Verzeichnis liefert keine Namen in die Plattform |

Navigation: Start und Ich immer; Challenges mit M1; Entdecken mit mindestens einem von M3 bis M7, bei genau einem benannt nach der Kategorie; Firma mit M9 oder M10. Mehr als fünf Bereiche gibt es nicht.

### 2.3 Bündel

Bündel sind Vertriebsvorschläge der Verwaltung, keine eigenen Entitlements: Einstieg (M1), Aktiv (M1, M2, M3), Belegschaft (zusätzlich M4, M11, M12), Voll (alle), Redaktion (M5, M10). Die Buchung eines Bündels erzeugt einzelne Entitlements.

## 3. Entitlement-Modell (A-065)

### 3.1 Datensatz

| Feld | Bedeutung |
|---|---|
| Tenant, Modul | Schlüssel; je Paar höchstens ein aktiver Datensatz |
| Zustand | aktiv, Testphase, auslaufend, inaktiv |
| aktiv_ab | Beginn der Nutzung, sofort bei Buchung |
| aktiv_bis | leer oder Monatsende nach Kündigung |
| test_bis | Ende der Testphase; danach automatisch aktiv mit Bewertung, sofern nicht vorher gekündigt |
| Quelle | Tenant-Admin, Partner-Admin, Operator-Admin, Partner-Voreinstellung |
| Grenzwerte | Speicher, Uploads je Monat, aktive Sprachen und andere Betriebsgrenzen mit Standardwerten (Abschnitt 6) |
| Historie | append-only: jede Zustandsänderung mit Zeitpunkt, Rolle und Grund |

### 3.2 Zustände

| Zustand | Nutzung | Bewertung durch Metering | Übergang |
|---|---|---|---|
| Testphase | voll | Ereignisse erfasst, nicht bewertet | endet zu `test_bis` → aktiv; Kündigung in der Testphase → inaktiv zum Testende ohne Kosten |
| Aktiv | voll | bewertet | Kündigung → auslaufend |
| Auslaufend | voll bis `aktiv_bis` | bewertet bis `aktiv_bis` | zu `aktiv_bis` → inaktiv; Rücknahme der Kündigung vor `aktiv_bis` → aktiv |
| Inaktiv | keine; Daten 90 Tage lesbar für Export, danach verdichtet (A-024) | Speicher bis zur Löschung ausgewiesen | erneute Buchung → aktiv, ohne neue Testphase |

Eine Testphase gibt es je Tenant und Modul höchstens einmal.

### 3.3 Rahmen und Vererbung

- Der **Operator** legt je Partner den Rahmen fest: erlaubte Module, Testphasen-Erlaubnis und -dauer je Modul, Voreinstellungen bei Tenant-Anlage.
- Der **Partner** legt innerhalb seines Rahmens denselben Rahmen für seine Tenants fest (A-037).
- Tenants ohne Partner erhalten den Rahmen des Operators direkt.
- Voreinstellungen erzeugen bei Tenant-Anlage aktive Entitlements mit Quelle „Voreinstellung“; der Tenant-Admin kann sie kündigen wie jedes andere.
- Wird ein Modul aus einem Rahmen entfernt, bleiben bestehende Entitlements bis zum übernächsten Monatsende aktiv und laufen dann aus; Tenant-Admins werden benachrichtigt. Neue Buchungen sind sofort nicht mehr möglich.

## 4. Buchung, Testphase und Kündigung (A-066)

### 4.1 Wer darf was

| Aktion | Tenant-Admin | Partner-Admin | Operator-Admin |
|---|---|---|---|
| Modul buchen | innerhalb des Rahmens | für eigene Tenants | für alle |
| Testphase starten | wenn der Rahmen es erlaubt, einmal je Modul | für eigene Tenants | für alle |
| Kündigen | ja | für eigene Tenants | für alle |
| Kündigung zurücknehmen | ja, vor `aktiv_bis` | ja | ja |
| Modul erzwungen deaktivieren | nein | nein | ja, mit Grund im Protokoll |
| Grenzwerte ändern | nein | nein | ja |

### 4.2 Buchungsablauf in der Verwaltung

1. Modulkatalog mit Vorschau echter Screens, Umfang, Abhängigkeiten, Datenschutzhinweis aus 2.2 und Preis samt Metrik aus dem Preisplan.
2. Auswirkung auf laufenden Monat und Folgemonat aus der Kostenvorschau des Metering, **vor** dem Klick.
3. Bei unerfüllter Abhängigkeit schlägt die Verwaltung das Bündel vor; sie verweigert nicht.
4. Buchung ist eine sensible Aktion mit frischer Anmeldung (A-015) und wird protokolliert.
5. Wirkung sofort: Entitlement aktiv, Navigation der Mitglieder-App erweitert sich beim nächsten Laden. Optional erzeugt der Programm-Manager ein Feed-Ereignis „Neu bei uns“ und eine Ankündigung; automatisch geschieht nichts.
6. Metering erhält das Ereignis „Modul aktiviert“ beziehungsweise „Testphase gestartet“.

### 4.3 Kündigung

- Wirksam zum Monatsende, in dem gekündigt wird; bis dahin volle Nutzung und Bewertung (Proratierung regelt Metering).
- Laufende Challenges enden spätestens zum Kündigungstermin von M1 (A-040); Arena-Teilnahme endet mit der laufenden Arena; Wearable-Verbindungen werden getrennt, Rohdaten binnen 30 Tagen gelöscht, abgeleitete Werte bleiben.
- Nach `aktiv_bis`: Navigation verliert den Bereich, Feed zeigt keine neuen Karten des Moduls, bestehende Karten bleiben 90 Tage sichtbar, Inhalte und Daten des Moduls sind 90 Tage für Tenant-Export lesbar, danach Verdichtung zu anonymen Aggregaten (A-024). Verdiente Abzeichen bleiben dauerhaft.
- Kündigung von Modulen, von denen andere abhängen (M1 mit aktivem M2 oder M9), kündigt die abhängigen Module zum selben Termin; die Verwaltung zeigt das vorher.

### 4.4 Erzwungene Deaktivierung durch den Operator

Bei Wegfall einer Voraussetzung (etwa Entzug eines Herstellerprogramms für M8) oder Missbrauch kann der Operator ein Modul mit Grund deaktivieren. Wirkung wie eine Kündigung zum angegebenen Termin, frühestens sieben Tage nach Ankündigung an die Tenant-Admins, außer bei rechtlicher Notwendigkeit sofort. Protokolliert und für die Einsichtsrolle sichtbar.

## 5. Durchsetzung (A-067)

| Ebene | Regel |
|---|---|
| Navigation | wird ausschließlich aus aktiven Entitlements erzeugt; kein Menüpunkt für inaktive Module |
| API | jede Operation eines Moduls prüft das Entitlement im Autorisierungsquerschnitt; ohne Entitlement Antwort „nicht gefunden“, nicht „verboten“, damit keine Katalogauskunft über die API entsteht |
| Daten | Lesepfade eines inaktiven Moduls liefern für Mitglieder nichts; Tenant-Export liest 90 Tage; Speicherung folgt A-024 |
| Feed | Karten inaktiver Module werden nicht erzeugt; bestehende bleiben 90 Tage, danach ausgeblendet |
| Benachrichtigungen | Kategorien inaktiver Module erzeugen nichts |
| Jobs | Jobs inaktiver Module werden übersprungen und als übersprungen protokolliert, nicht als Fehler |
| Challenges | Achsenwerte, die ein Modul brauchen (Erfassung automatisch, Sichtbarkeit Arena, Plattformereignis-Metriken einer Kategorie), sind nur mit aktivem Modul wählbar; Vorlagen bieten Ersatzmetriken |
| Inhalte | Kategorien und Sammlungen nur mit aktivem Modul; Tenantinhalte einer Kategorie setzen das Modul voraus |
| Auswertung | Entitlement-Prüfung wird je Request einmal ausgewertet, bis 60 Sekunden zwischengespeichert und bei jeder Änderung sofort invalidiert; Buchung und Kündigung wirken beim nächsten Request |
| Nachweis | Historie ist append-only und Grundlage für Rechnung, Einsichtsrolle und Prüfprotokoll |

Die Frontend-Darstellung ist keine Autorisierung (K14); die API-Prüfung ist maßgeblich.

## 6. Funktionsfreigaben und Grenzwerte (A-068)

### 6.1 Funktionsfreigaben des Operators

Getrennt von Modulen führt der Operator **Funktionsfreigaben**: befristete Schalter je Tenant für Funktionen vor ihrer allgemeinen Verfügbarkeit oder für den Durchstich. Sie sind für Tenants nicht buchbar und im Katalog nicht sichtbar, haben ein Ablaufdatum von höchstens 90 Tagen, sind verlängerbar und protokolliert. Eine Funktionsfreigabe ersetzt nie ein Modul und erzeugt keine Metering-Bewertung. Sie ist für die Einsichtsrolle sichtbar.

### 6.2 Grenzwerte

| Grenzwert | Gilt für | Standard | Verhalten bei Überschreitung |
|---|---|---|---|
| Medienspeicher gesamt | Tenant | 50 GB | Upload blockiert mit Hinweis; Bepreisung des Speichers bleibt beim Metering |
| Video-Uploads je Monat | M3, M5, M7, M10 | 50 | Upload blockiert mit Hinweis |
| Dokumente gesamt | M10 | 500 | Upload blockiert mit Hinweis |
| Aktive Sprachen | M11 | 5 | weitere Sprache nicht aktivierbar |
| Kiosk-Geräte | Kern | 20 | weitere Registrierung nicht möglich |
| KI-Aufrufe je Monat | Kern, falls konfiguriert | vom Tenant gesetzt, sonst 1.000 | Funktion pausiert bis Monatswechsel |

Grenzwerte sind Betriebsschutz. Der Operator ändert sie je Tenant; Änderungen werden protokolliert. Sie sind keine Preisstufen.

## 7. Schnittmengen dieses Themas

| Schnittmenge | Festlegung |
|---|---|
| Entitlements ↔ Metering | Entitlements senden „aktiviert“, „Testphase gestartet“, „gekündigt“, „inaktiv“ als Ereignisse; Metering bewertet; Entitlements prüfen nie Zahlungen; Kostenvorschau vor Buchung kommt aus Metering |
| Entitlements ↔ Organisation | Rahmen entlang Operator → Partner → Tenant; Voreinstellungen bei Tenant-Anlage; Tenant-Sperre überlagert alle Entitlements |
| Entitlements ↔ Autorisierung | jede Moduloperation prüft das Entitlement im Querschnitt; Antwort „nicht gefunden“ |
| Entitlements ↔ Feed und Inhalte | Kategorien, Sammlungen, Firmenkanal-Rechte und Karten nur mit aktivem Modul; keine Hinweise auf inaktive Module |
| Entitlements ↔ Challenges | Achsenwerte und Vorlagen mit Ersatzmetrik; Kündigung von M1 beendet laufende Challenges zum Termin |
| Entitlements ↔ Benachrichtigungen | Kategorien folgen Modulen; Benachrichtigung an Tenant-Admins bei Rahmenänderung, Testphasenende, erzwungener Deaktivierung |
| Entitlements ↔ Datenschutz | Datenschutzhinweis je Modul im Katalog; Historie für Einsichtsrolle; 90-Tage-Lesbarkeit und Verdichtung nach A-024; Wearable-Rohdaten binnen 30 Tagen |
| Entitlements ↔ Wearable-Vault | M8 nur buchbar, wenn der Operator mindestens einen Hersteller freigegeben hat; Kündigung trennt Verbindungen |
| Entitlements ↔ Arena | M2 setzt M1 voraus; Arena-Teilnahme endet mit der laufenden Arena |

## 8. Nachweise

1. Mitglieder-App eines Tenants nur mit Kern: zwei Bereiche, keine Schlösser, keine Hinweise auf Module; API-Aufruf eines Modulendpunkts liefert „nicht gefunden“.
2. Buchung von M2 ohne M1: Bündelvorschlag statt Ablehnung; Buchung beider erweitert die Navigation beim nächsten Laden; Metering erhält zwei Ereignisse.
3. Testphase: Nutzung voll, Bewertung null; Kündigung in der Testphase kostenfrei; zweite Testphase desselben Moduls abgelehnt.
4. Kündigung von M1 mit aktivem M2: beide auslaufend zum Monatsende; laufende Challenge endet zum Termin; Rücknahme vor Termin stellt beide wieder her.
5. Nach `aktiv_bis`: Bereich weg, keine neuen Karten, Export 90 Tage möglich, danach Verdichtung; Abzeichen bleiben.
6. Entfernen eines Moduls aus dem Partner-Rahmen: keine neuen Buchungen, bestehende laufen zum übernächsten Monatsende aus, Benachrichtigung an Tenant-Admins.
7. Erzwungene Deaktivierung: Grund im Protokoll, sieben Tage Vorlauf, Einsichtsrolle sieht es.
8. Grenzwert Speicher überschritten: Upload blockiert mit Hinweis, keine Metering-Wirkung; Operator erhöht, Upload möglich.
9. Funktionsfreigabe läuft nach 90 Tagen aus; für Tenant nicht buchbar, nicht sichtbar, ohne Bewertung.
10. Entitlement-Cache: Buchung wirkt spätestens beim nächsten Request, nicht erst nach 60 Sekunden.
