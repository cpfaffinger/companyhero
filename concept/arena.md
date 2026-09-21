# CompanyHero – Arena

**Stand:** 21.09.2026  
**Status:** Beschlossen gemäß A-087 bis A-089 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung.  
**Dienst:** Arena, isolierter Dienst gemäß [Domänenkarte](domaenen-und-schnittmengen.md); Modul M2.  
**Bezug:** [Challenges](challenges.md), [Organisation und Mandanten](organisation-und-mandanten.md), [Datenschutz und Nachweis](datenschutz-und-nachweis.md), [Marke und Theme](marke-und-theme.md).

Die Arena ist der firmenübergreifende Wettbewerb. Für kleine Betriebe, in denen eine eigene Community nie kritische Masse erreicht, ist sie der eigentliche Nutzen. Sie liegt strukturell außerhalb der Mandantengrenze; deshalb ist sie kein Modul des Monolithen, sondern ein eigener Dienst, der ausschließlich freigegebene Projektionen erhält.

## 1. Grundsätze

- **Nur Projektionen:** Was einen Tenant verlässt, ist der Pro-Kopf-Wert auf die gemeldete Sollstärke, die Größenklasse, Firmenname, Logo und Zeitpunkt. Nie Beitragssummen, Teilnehmerzahlen, Beteiligungsquoten, Gruppenwerte, Klarnamen, E-Mail-Adressen oder Wearable-Daten.
- **Pro Kopf auf Sollstärke:** Normalisierung auf Aktive würde Betriebe mit wenigen Aktiven belohnen und Admins lehren, Gelegenheitsteilnehmer nicht zu aktivieren.
- **Mindestbeteiligung:** Ein Tenant mit weniger als fünf beitragenden Personen erscheint als „nimmt teil“ ohne Wert und ohne Rang.
- **Doppelte Zustimmung für Einzelwerte:** Tenant-Admin erlaubt Einzelränge in dieser Arena und die Person hat die Arena-Sichtbarkeit gesetzt. Fehlt eines, zählt der Beitrag nur in den Pro-Kopf-Wert.
- **Plattform-Schale:** Die Arena wird sichtbar betreten; die Tenant-Marke wird zum Teilnehmerkennzeichen (A-078).
- **Keine leeren Ligen:** Eine Arena startet nur mit mindestens vier Tenants; Größenklassen greifen erst ab vier Tenants je Klasse.
- **Keine Preise:** Anerkennung über Feed und Abzeichen für alle im Tenant, nie Sachpreise oder individuelle Belohnungen.

## 2. Dienst und Isolation (A-087)

### 2.1 Aufbau

| Eigenschaft | Festlegung |
|---|---|
| Prozess | eigener API- und Worker-Prozess aus eigener Codebasis im selben Stack (A-001), eigenes Image, eigenes Compose-Projekt (A-028) |
| Datenbank | eigene PostgreSQL-Datenbank mit eigenen Zugangsdaten; kein Zugriff auf die Datenbank des Monolithen und umgekehrt |
| Netz | nur intern erreichbar; keine öffentliche Route; Zugriff ausschließlich vom Monolithen |
| Zugangsdaten | Dienstzugangsdaten je Richtung in OpenBao mit eigener Richtlinie |
| Beobachtung | Logs ohne Personenbezug; Metriken über den gemeinsamen Collector (A-029) |
| Backup | wie der Monolith (A-031), eigenes Repository |

### 2.2 Schnittstellen

| Richtung | Inhalt | Transport |
|---|---|---|
| Monolith → Arena | Arena-Definitionen des Operators, Tenant-Beitritte mit Name, Logo-Referenz, Größenklasse, Region, Branche; Projektionen je Tenant und Arena; pseudonyme Einzelwerte; Austritte und Löschaufträge | Jobs aus der Queue (A-006) rufen die interne Arena-API idempotent auf; Wiederholung ohne Doppelwirkung |
| Arena → Monolith | Stände je Arena (Ränge, Pro-Kopf-Werte, Meilensteine), Ereignisse (Start, Stand aktualisiert, Ende) | der Monolith holt Stände über die interne API und cached sie je Tenant; Ereignisse werden als Fachereignisse im Monolithen veröffentlicht |

Der Arena-Dienst kennt Tenants nur als Kennung mit Name, Logo, Größenklasse, Region und Branche. Er kennt keine Personen; pseudonyme Einzelwerte tragen ein zufälliges Arena-Token je Person und Arena, das der Monolith erzeugt und das mit dem Ende der Arena-Aufbewahrung gelöscht wird.

### 2.3 Verantwortung

Der Betreiber ist Verantwortlicher für die Verarbeitung im Arena-Dienst (A-020). Das Arena-Informationsblatt beschreibt, welche Daten einen Tenant verlassen. Der Support-Zugriff des Operators folgt A-025; Rohzugriff auf die Arena-Datenbank ist wie beim Monolithen auf den Betrieb beschränkt.

## 3. Arena-Modell (A-088)

### 3.1 Definition

| Feld | Festlegung |
|---|---|
| Titel, Beschreibung | in Nutzersprache, je Sprache |
| Zeitraum | fester Zeitraum von vier bis zwölf Wochen |
| Metrik | Schritte, aktive Minuten, Distanz, Häkchen oder Plattformereignisse; frei definierte Metriken nur, wenn alle Tenants sie gleich erfassen können |
| Normalisierung | immer pro Kopf auf die Sollstärke des Tenants zum Arena-Start |
| Wettbewerbsform | Rangliste der Firmen, gemeinsames Sammelziel aller Firmen, Duell zweier Ligen |
| Ligen | Größenklassen bis 50, 51 bis 250, 251 bis 1.000, über 1.000 nach Sollstärke; optional zusätzlich Region oder Branche |
| Einzelrangliste | vom Operator je Arena erlaubt oder nicht; zusätzlich je Tenant und Person nach doppelter Zustimmung |
| Beitrittsfenster | Anmeldung offen bis Arena-Start |
| Mindestteilnahme | vier Tenants je Arena; Ligen erst ab vier Tenants je Klasse, sonst eine offene Arena über alle Klassen |

### 3.2 Lebenszyklus

| Zustand | Bedeutung |
|---|---|
| Entwurf | vom Operator angelegt, nicht sichtbar |
| Anmeldung offen | für Tenants mit M2 in der Verwaltung sichtbar; Beitritt möglich |
| Laufend | Projektionen laufen ein; stündliche Snapshots |
| Nachfrist | 48 Stunden nach Ende für verspätete Projektionen (A-039) |
| Beendet | Endstand; Abzeichen und Feed-Ereignis; sichtbar unter „Abgeschlossen“ |
| Archiviert | nach Zeitraum plus 12 Monaten: pseudonyme Einzelwerte und Arena-Tokens gelöscht, Tenant-Verläufe als anonyme Aggregate (A-024) |

Erreicht eine Arena zum Start keine vier Tenants, verschiebt der Operator den Start um bis zu vier Wochen oder sagt ab; beigetretene Tenants werden benachrichtigt, ihre gespiegelten Challenges enden ohne Wertung.

### 3.3 Operator

Der Operator legt Arenen an, ordnet Ligen zu, moderiert (Ausschluss eines Tenants bei Missbrauch mit Protokoll), verlängert oder sagt ab und sieht Stände aller Arenen. Er sieht keine Personen außer den pseudonymen Einzelwerten, die ohnehin arenaweit sichtbar sind.

## 4. Teilnahme, Projektion und Anzeige (A-089)

### 4.1 Beitritt eines Tenants

1. Der Tenant-Admin öffnet „Challenges → Arena → Offene Arenen“ (M2 vorausgesetzt).
2. Das Detail zeigt Zeitraum, Metrik, Liga, Teilnehmerzahl der Firmen und den Abschnitt **„Was verlässt unsere Firma?“** mit der vollständigen Liste aus Abschnitt 1.
3. Der Tenant-Admin wählt: Geltungsbereich (ganzer Tenant), Einzelränge erlauben ja oder nein.
4. Beitritt ist eine sensible Aktion (A-015), protokolliert, für die Einsichtsrolle sichtbar. Metering erhält `arena.entry` und je Monat `arena.size_class`.
5. Der Beitritt erzeugt im Tenant eine **gespiegelte Challenge** mit den Arena-Regeln, Sichtbarkeit „Arena“, automatischem Beitritt aller mit Widerspruchsmöglichkeit und synchronem Zeitraum. Personen erfassen dort wie in jeder Challenge (A-039).
6. Austritt vor Start jederzeit; nach Start endet die Teilnahme mit der laufenden Arena; die Projektion bleibt bis Ende, es sei denn, der Tenant verlangt Rückzug, dann erscheint er als „ausgeschieden“ ohne Werte.

Die Sollstärke zum Arena-Start bestimmt Größenklasse und Pro-Kopf-Bezug; spätere Änderungen wirken nicht auf die laufende Arena.

### 4.2 Projektion

Die Domäne Challenges berechnet je Tenant und Arena stündlich:

| Wert | Regel |
|---|---|
| Pro-Kopf-Wert | Summe der gültigen Beiträge geteilt durch Sollstärke zum Start, gerundet auf eine Nachkommastelle |
| Größenklasse | aus Sollstärke zum Start |
| Zeitpunkt | letzte Aktualisierung |
| Status | „wertend“ ab fünf beitragenden Personen, sonst „nimmt teil“ |
| Einzelwerte | nur wenn der Operator für die Arena eine Einzelrangliste vorsieht und die doppelte Zustimmung vorliegt: Arena-Token, Arena-Pseudonym der Person (voreingestellt der Anzeigename; Personen mit Klarname als Anzeigename wählen ein eigenes Pseudonym), persönlicher Wert der Metrik in der Arena; ohne Einzelrangliste wird nichts übertragen |

Übergeben werden nur diese Felder. Beitragssummen, Teilnehmerzahlen, Beteiligungsquoten und Gruppenwerte existieren im Arena-Dienst nicht. Ein Wechsel der Arena-Sichtbarkeit einer Person entfernt oder ergänzt ihren Einzelwert mit der nächsten Projektion; Rückzug wirkt rückwirkend auf die Anzeige.

### 4.3 Anzeige in der Mitglieder-App

- Bereich „Challenges → Arena“ in der Plattform-Schale mit eigener Kopfzeile „Firmenübergreifend“; das eigene Firmenlogo als Teilnehmerkennzeichen (A-078).
- Übersicht: laufende und abgeschlossene Arenen, eigene Firma hervorgehoben, Rang und Pro-Kopf-Wert je Firma, „nimmt teil“ ohne Wert für Firmen unter fünf, Stand mit Altersangabe.
- Gemeinsames Sammelziel aller Firmen als Kollektivbalken mit Meilenstein-Route.
- Einzelrangliste nur, wenn der Operator sie erlaubt und mindestens fünf Personen mit Zustimmung vorhanden sind; Zeilen mit Anzeigename, Firmenlogo und Wert; die eigene Zeile hervorgehoben; keine Nullwerte.
- Nie sichtbar: Beteiligungsquote irgendeiner Firma, Teilnehmerzahlen, Gruppen anderer Firmen.
- Arena-Ereignisse (Start, Stand, Meilenstein, Ende) erscheinen im Feed des Tenants als Karten in Plattform-Schale und über Benachrichtigungen der Kategorie Challenge.
- Der Wechsel in die Arena ändert nie Sitzung oder Mitgliedschaft; offene kontextgebundene Overlays werden geschlossen (K05).

### 4.4 Persönliche Zustimmung

Die Arena-Sichtbarkeit ist eine separate Zustimmung im Profil (A-022) mit Klartext: Welche Firmen sie sehen, was sie sehen (Arena-Pseudonym und Wert der Arena-Metrik), dass Pseudonym und Wert an den Arena-Dienst des Betreibers übertragen und dort erst ab fünf zustimmenden Personen angezeigt werden, dass der Betreiber Verantwortlicher ist, jederzeit widerrufbar mit sofortiger Wirkung. Die Person wählt dabei ihr Arena-Pseudonym; Voreinstellung ist der Anzeigename, bei Klarnamen als Anzeigename ist ein eigenes Pseudonym Pflicht. Sie ist standardmäßig aus und wird beim Beitritt des Tenants nicht abgefragt, sondern erst beim ersten Öffnen der Arena angeboten.

### 4.5 Ende und Anerkennung

Zum Endstand nach der Nachfrist: Feed-Karte je Tenant mit Rang und Pro-Kopf-Wert der eigenen Firma, Abzeichen „Firmenziel“ für alle Personen des Tenants bei Erreichen des Sammelziels oder eines Ranges in der oberen Hälfte, Belohnung nur als Tenant-Belohnung oder Workshop-Gutschein gemäß A-038 und A-085, kollektiv. Keine Preise vom Betreiber.

## 5. Schnittmengen dieses Themas

| Schnittmenge | Festlegung |
|---|---|
| Arena ↔ Challenges | gespiegelte Challenge je Beitritt; Projektion stündlich; Endstand nach Nachfrist; keine Beitragssummen im Dienst |
| Arena ↔ Organisation | Sollstärke zum Start als Bezug; Region und Branche als optionale Stammdaten für Ligen |
| Arena ↔ Entitlements | M2 setzt M1 voraus; Kündigung endet mit der laufenden Arena |
| Arena ↔ Metering | `arena.entry` und `arena.size_class` vom Monolithen beim Beitritt und je Monat |
| Arena ↔ Datenschutz | Betreiber als Verantwortlicher, Informationsblatt, doppelte Zustimmung, Mindestzahl, Aufbewahrung Zeitraum plus 12 Monate, Löschung der Tokens |
| Arena ↔ Marke | Plattform-Schale; Tenant-Logo als Teilnehmerkennzeichen; Texte im Textkatalog |
| Arena ↔ Feed und Benachrichtigungen | Arena-Ereignisse als Fachereignisse im Monolithen; Karten in Plattform-Schale; Kategorie Challenge |
| Arena ↔ Fortschritt | Abzeichen „Firmenziel“ kollektiv |
| Arena ↔ Betrieb | eigenes Compose-Projekt, eigene Datenbank, interne Route, eigene Zugangsdaten, eigenes Backup |

## 6. Nachweise

1. Der Arena-Dienst hat keinen Zugriff auf die Monolith-Datenbank und umgekehrt; öffentliche Route existiert nicht.
2. Nach einem Testlauf enthält die Arena-Datenbank keine Personen-IDs, Klarnamen, Beitragssummen, Teilnehmerzahlen oder Quoten; nur Pro-Kopf-Werte, Größenklassen, Firmennamen, Logos, Zeitpunkte und Arena-Tokens mit Arena-Pseudonymen.
3. Tenant mit vier Beitragenden erscheint als „nimmt teil“ ohne Wert; mit fünf wertend.
4. Einzelwert erscheint nur bei doppelter Zustimmung; Widerruf entfernt ihn mit der nächsten Projektion.
5. Arena mit drei Tenants startet nicht; Verschiebung oder Absage benachrichtigt die Tenants; Ligen entstehen erst ab vier je Klasse.
6. Sollstärke-Änderung während der Arena ändert weder Größenklasse noch Pro-Kopf-Bezug.
7. Stände aktualisieren stündlich, nie live; Endstand nach 48 Stunden Nachfrist.
8. Wechsel in die Arena zeigt Plattform-Schale, schließt kontextgebundene Overlays, lässt Sitzung unverändert.
9. Nach Zeitraum plus 12 Monaten sind Tokens und Einzelwerte gelöscht; Tenant-Verläufe anonym.
10. Wiederholte Projektionsjobs erzeugen keine doppelten Einträge.
