# CompanyHero – Challenges

**Stand:** 20.09.2026  
**Status:** Beschlossen gemäß A-038 bis A-043 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung.  
**Domäne:** Challenges gemäß [Domänenkarte](domaenen-und-schnittmengen.md).  
**Bezug:** [Organisation und Mandanten](organisation-und-mandanten.md), [Datenschutz und Nachweis](datenschutz-und-nachweis.md), [Zugang und Identität](zugang-und-identitaet.md).

## 1. Grundsätze

- Eine Challenge ist eine Komposition aus sieben Achsen und drei Konfigurationsblöcken, keine Vorlage mit Parametern. Aus derselben Engine entstehen alle Formate.
- Kooperation ist der Standard: Das gemeinsame Sammelziel ist die voreingestellte Wettbewerbsform. Ranglisten sind eine bewusste Wahl mit Hinweis auf ihre Wirkung.
- Selbstberichtung ist ein erstklassiger Datenpfad. Anti-Manipulation läuft über Tagesdeckel und Plausibilität, nicht über Beweispflicht.
- Messwerte einer Challenge bleiben in dieser Challenge. Für den persönlichen Fortschritt erzeugt jeder Beitrag genau ein pauschales Aktivitätsereignis (Domäne Fortschritt).
- Auswertungsgruppen sind ausschließlich die Gruppen des Tenants und der ganze Tenant. Es gibt keine Ad-hoc-Teams.
- Sichtbarkeit, Mindestzahl und Ranglistenschwelle folgen A-022 und A-023 und werden über die zentrale Leseregel angewendet.
- Belohnungen sind kollektiv und nie geldwert je Person.

## 2. Challenge-Modell (A-038)

### 2.1 Die sieben Achsen

| Achse | Werte | Festlegungen |
|---|---|---|
| **1 Zeitform** | fester Zeitraum · rollierend (Woche oder Monat) · Serie mit Kadenz (jeden Monat neu, synchron) · dauerhaft offen | Kadenz-Challenges laufen für alle im selben Zyklus, nicht ab individuellem Beitritt |
| **2 Metrik** | aus dem Katalog in 2.2 oder frei definiert (Bezeichnung, Einheit, Erfassungsart) | Gewicht, BMI, Körpermaße, Kalorienzufuhr und Körperfett existieren nicht (A-021) |
| **3 Erfassung** | Häkchen · Zahl · Dauer/Timer · Plattformereignis · Foto als Beleg · automatisch (nur mit Wearable-Anbindung) | Foto nie an Admin oder Arena; am Kiosk kein Foto |
| **4 Normalisierung** | Rohwert · pro Kopf (Sollstärke der Gruppe) · relative Verbesserung zur eigenen Baseline · Zielerreichung binär | pro Kopf ist Pflicht bei Gruppenvergleichen mit ungleicher Größe; ohne Sollstärke der Gruppe nicht wählbar |
| **5 Aggregation** | Person · Gruppe einer Dimension · ganzer Tenant · Tenant in der Arena | genau eine Dimension je Challenge |
| **6 Wettbewerbsform** | Sammelziel (Standard) · Meilenstein-Route · Rangliste · Duell · Serie/Gewohnheit · Aufgabenraster (9 oder 16 Felder) · Staffel | Rangliste und Duell zeigen im Wizard den Hinweis zur Wirkung auf Beteiligung |
| **7 Sichtbarkeit** | Nur ich · Gruppe · ganze Firma · Arena | die restriktivere persönliche Einstellung gewinnt immer |

### 2.2 Metrikkatalog

| Gruppe | Metriken | Einheit |
|---|---|---|
| Bewegung | Schritte, aktive Minuten, Distanz, Höhenmeter, Stockwerke, Einheiten | Anzahl, min, km, m, Anzahl, Anzahl |
| Ausdauer | moderate Ausdauerminuten | min |
| Regeneration | Schlafziel erreicht, Atemübungsminuten, Pausen | ja/nein, min, Anzahl |
| Plattform-Handlungen | Check-in, Inhalt abgeschlossen, Beitrag gelesen, Quiz bestanden, Ergonomie-Check | Ereignis |
| Gewohnheiten | frei benanntes Tageshäkchen | ja/nein |
| Frei definiert | Bezeichnung, Einheit, Erfassungsart durch den Tenant | frei |

Schlaf ist ausschließlich binär: Aus Wearable-Quellen kommt nur die Zielerreichung, ausgewertet im Vault; selbstberichteter Schlaf ist ebenfalls nur als Zielerreichung erfassbar. Stundenwerte existieren in der Challenge-Domäne nicht.

### 2.3 Konfigurationsblöcke

**Fairness:** Tagesdeckel je Metrik und Person (Voreinstellung aus dem Katalog, änderbar innerhalb von Grenzen), Plausibilitätsgrenzen für automatische Quellen (höchstens 25.000 Schritte je Tag anrechenbar), Segmentierung nach Gruppe, Serienschutz gemäß Fortschritt, Mindestbeteiligung für Rangsichtbarkeit (A-023).

**Beitritt:** offen · auf Einladung (per Beitrittscode der Challenge) · automatisch alle mit Widerspruchsmöglichkeit. Eine automatische Teilnahme ohne Widerspruchsmöglichkeit gibt es nicht. Austritt aus einer Challenge ist jederzeit möglich; geleistete Beiträge bleiben anonym im Kollektiv.

**Belohnung:** Abzeichen (aus Fortschritt) · Tenant-Belohnung als Freitext mit Zielbedingung (etwa „bei 100 % Gesundheitstag für alle“) · Workshop-Gutschein aus [Workshops und Events](workshops-und-events.md) mit Angebot, Bedingung, Geltungsbereich und Kostenmodell (A-085). Belohnungen gelten immer der ganzen Gruppe oder dem ganzen Tenant, auch Nichtteilnehmenden. Der Programm-Manager markiert eine Tenant-Belohnung als eingelöst; das erzeugt ein Feed-Ereignis. Geld, Urlaubstage oder individuelle Gutscheine sind nicht konfigurierbar.

### 2.4 Texte und Sprache

Titel, Beschreibung und Belohnungstext werden als Textkarte je Sprache gespeichert; Pflicht ist die Standardsprache des Tenants. Weitere Sprachen ergänzt das Mehrsprachigkeitsmodul. Titel verwenden Nutzersprache; Fachbegriffe stehen nur in der Beschreibung.

### 2.5 Sensible Gewohnheiten

Verzichts- und Konsum-Challenges (Alkohol, Tabak, Zucker, Bildschirmzeit) sind eine eigene Kategorie: Sichtbarkeit erzwungen „Nur ich“, keine Teilnehmerzahl, keine Feed-Ereignisse, kein Kollektivstand, kein Beitrag zu Gruppen- oder Tenant-Aggregaten. Sie stehen nicht in der Vorlagenbibliothek, sondern nur als leere Vorlage mit Warnhinweis.

## 3. Erfassung und Korrektur (A-039)

| Regel | Festlegung |
|---|---|
| Erfassungszeitpunkt | Der Beitrag trägt den Zeitpunkt der Erfassung auf dem Gerät und den Eingang beim Server; gewertet wird der Erfassungszeitpunkt in der Zeitzone des Tenants |
| Rückdatierung | Häkchen, Zahl und Dauer bis zu drei Kalendertage rückwirkend; nie in die Zukunft; Plattformereignisse und automatische Werte nur zum tatsächlichen Zeitpunkt |
| Tagesdeckel | gilt je Erfassungstag und Metrik; Beiträge über dem Deckel werden auf den Deckel gekappt und der Person angezeigt |
| Idempotenz | Kiosk-Beiträge mit serverseitiger Vorgangskennung, Offline-Beiträge mit Client-Idempotenzschlüssel; ein Namensraum je Tenant und Person (A-009) |
| Nachfrist | Beiträge mit Erfassungszeitpunkt im Zeitraum werden bis 48 Stunden nach Challenge-Ende angenommen; danach ist der Endstand endgültig |
| Korrektur | Die Person kann eigene Beiträge bis zum Ende der Nachfrist ändern oder löschen; die Änderung erzeugt eine Gegenbuchung; Kollektivstände und Ranglisten werden neu berechnet; verliehene Abzeichen bleiben |
| Kiosk | Häkchen, Zahl und Dauer; kein Foto, keine Korrektur vergangener Tage |
| Foto | Neukodierung ohne Metadaten (A-026), höchstens gruppensichtbar, Löschung 90 Tage nach Ende |
| Automatische Quellen | abgeleitete Tageswerte aus dem Vault; eine manuelle Erfassung derselben Metrik am selben Tag wird nicht addiert, der höhere Wert gilt bis zum Deckel |

Jeder gültige Beitrag erzeugt in einer Transaktion: den Beitrag, das Fachereignis „Beitrag erfasst“, ein pauschales Aktivitätsereignis für Fortschritt, ein Metering-Ereignis und die Folgejobs für Kollektivstand, Feed und Benachrichtigungen (A-006).

## 4. Lebenszyklus, Kadenz und Zuständigkeit (A-040)

### 4.1 Zustände

| Zustand | Bedeutung | Übergang |
|---|---|---|
| Entwurf | konfiguriert, nicht sichtbar | Vorschau ist Pflicht vor „Planen“ |
| Geplant | sichtbar als „startet am“, Beitritt möglich, keine Beiträge | Start zum Zeitpunkt |
| Laufend | Beiträge möglich | Ende zum Zeitpunkt oder vorzeitig durch den Ersteller mit Begründung im Protokoll |
| Nachfrist | 48 Stunden, verspätete Offline-Beiträge, Korrekturen | automatisch |
| Beendet | Endstand endgültig, Belohnung auslösbar, sichtbar unter „Abgeschlossen“ | nach 12 Monaten Archivierung |
| Archiviert | nur noch im Verlauf der Person und in Aggregaten | Endzustand |

Änderungen an Zeitraum, Metrik, Normalisierung, Aggregation oder Wettbewerbsform sind nach dem Start nicht möglich. Änderbar bleiben Texte, Bild und Belohnungstext.

### 4.2 Zuständigkeit

| Rolle | Darf |
|---|---|
| Programm-Manager | tenantweite und gruppenbezogene Challenges anlegen, Vorlagen für Botschafter freigeben, Kadenz festlegen, laufende Challenges beenden, Belohnungen als eingelöst markieren |
| Gesundheitsbotschafter | Challenges für die eigenen Gruppen aus freigegebenen Vorlagen anlegen und beenden |
| Tenant-Admin | Arena-Beitritt; keine Challenge-Erstellung |
| Mitglied | beitreten, austreten, Beiträge erfassen, einem Botschafter eine Vorlage vorschlagen |

### 4.3 Kadenz

- Empfehlung, die der Wizard zeigt: eine tenantweite Challenge je Monat, Themenwechsel je Quartal, Gruppen-Challenges der Botschafter zusätzlich.
- Keine Vorlage wird innerhalb von 12 Monaten im selben Tenant wiederholt; der Wizard warnt und schlägt Alternativen vor. Die Warnung ist übersteuerbar, die Übersteuerung wird protokolliert.
- Eine Kickoff-Challenge ist beim Einrichten des Tenants vorbelegt: Sammelziel mit Meilenstein-Route, Häkchen oder aktive Minuten, Sichtbarkeit Firma, synchroner Start für alle Gruppen.

## 5. Vorlagenbibliothek (A-041)

### 5.1 Ebenen

| Ebene | Pflege | Sichtbarkeit |
|---|---|---|
| Plattformvorlagen | Operator | alle Tenants |
| Partnervorlagen | Partner-Admin | Tenants des Partners |
| Tenantvorlagen | Programm-Manager, aus eigener Challenge gespeichert | eigener Tenant |

Eine Vorlage ist eine vollständige Achsenkombination mit Titel, Beschreibung, Bild, Vorschlagswerten und Metadaten (Dauer, benötigte Erfassungsarten, benötigte Module). Vorlagen, deren Metrik eine nicht freigeschaltete Quelle braucht, werden mit Hinweis angezeigt und mit einer Ersatzmetrik angeboten, etwa aktive Minuten statt Schritte.

### 5.2 Plattformvorlagen zum Start

| # | Vorlage | Form | Metrik | Normalisierung | Sichtbarkeit |
|---|---|---|---|---|---|
| 1 | Gemeinsam nach … (Route zu einem Ziel) | Meilenstein-Route | Schritte oder aktive Minuten | Rohwert | Firma |
| 2 | Eine Million Schritte | Sammelziel | Schritte | Rohwert | Firma |
| 3 | 5.000 aktive Minuten im Monat | Sammelziel | aktive Minuten | Rohwert | Firma |
| 4 | Standort-Cup | Duell | frei | pro Kopf | Firma |
| 5 | Abteilung gegen Abteilung | Duell | frei | pro Kopf | Firma |
| 6 | Der große Sprung | Rangliste | Schritte oder Minuten | Verbesserung | Firma |
| 7 | Bewegte Pause, 7 Tage | Serie | Häkchen | binär | Gruppe |
| 8 | Treppe statt Lift | Serie | Häkchen | binär | Gruppe |
| 9 | 30 Nächte 7 Stunden | Serie | Schlafziel | binär | Nur ich |
| 10 | Zwei Minuten Atempause täglich | Serie | Dauer | binär | Nur ich |
| 11 | Rad oder Fuß zur Arbeit | Sammelziel | Häkchen | Rohwert | Firma |
| 12 | Wasserwoche | Serie | Häkchen | binär | Gruppe |
| 13 | Bewegungs-Bingo | Aufgabenraster | gemischt | binär | Gruppe |
| 14 | Vielfalts-Woche | Aufgabenraster | gemischt | binär | Gruppe |
| 15 | Ergonomie-Woche | Sammelziel | Plattformereignis | Rohwert | Firma |
| 16 | Wissens-Quiz-Woche | Rangliste | Quiz | Rohwert | Firma |
| 17 | Lese-Serie | Serie | Beitrag gelesen | binär | Nur ich |
| 18 | Erste Schritte (Einstieg) | Sammelziel | gemischt | binär | Gruppe |
| 19 | Willkommens-Bingo (Rollout) | Aufgabenraster | gemischt | binär | Gruppe |
| 20 | Schicht-Staffel | Staffel | frei | pro Kopf | Firma |
| 21 | Jahresauftakt | Serie mit Kadenz | gemischt | binär | Firma |
| 22 | Sommer-Aktivmonat | Sammelziel | aktive Minuten | Rohwert | Firma |
| 23 | Firmen-Cup | Duell | Schritte oder Minuten | pro Kopf | Arena |
| 24 | Regionale Arena | Rangliste | aktive Minuten | pro Kopf | Arena |

Die Texte und Bilder der Vorlagen sind Redaktionsarbeit des Betreibers und kein Bestandteil dieses Konzepts.

## 6. Wertung, Kollektivstände und Ranglisten (A-042)

### 6.1 Berechnung

- **Kollektivstände** werden ereignisgetrieben nach jedem Beitrag aktualisiert und in der App sofort angezeigt; die Mitglieder-App zeigt zusätzlich das Alter des Standes.
- **Ranglisten** werden alle 15 Minuten und am Ende der Nachfrist als Snapshot berechnet. Angezeigt wird immer ein Snapshot, nie eine Live-Berechnung; das verhindert Rückschlüsse aus Einzelbewegungen.
- **Pro Kopf** bezieht sich auf die Sollstärke der Gruppe zum Stichtag des Challenge-Starts; Änderungen der Sollstärke während der Challenge wirken nicht auf die laufende Wertung.
- **Relative Verbesserung:** Die ersten sieben Tage sind Baseline; gewertet wird ab Tag acht als Verhältnis zum eigenen Tagesdurchschnitt der Baseline. Ohne Erfassung in der Baseline gibt es keine Verbesserungswertung; Beiträge zählen ins Kollektiv.
- **Zielerreichung binär:** Ein Tag gilt als erfüllt, wenn der Tageswert das Ziel erreicht; Serienlogik gemäß Fortschritt.
- **Aufgabenraster:** Ein Feld gilt als erfüllt mit dem ersten passenden Beitrag; eine Reihe oder das volle Raster löst das Feed-Ereignis aus.
- **Staffel:** Je Woche ist eine Gruppe an der Reihe; Beiträge anderer Gruppen zählen in dieser Woche ins Kollektiv, nicht in die Staffelwertung.

### 6.2 Anzeige

- Unter fünf Beitragenden oder unter 40 Prozent der aktivierten Personen der Auswertungsgruppe wird statt der Rangliste der Kollektivfortschritt gerendert (A-023).
- Kollektivbalken zeigen unter fünf sichtbaren Beitragenden nur den gerundeten Prozentwert.
- Personen mit „Nur für mich“ erscheinen in keiner Rangliste und keinem Gruppenstand; ihre Beiträge zählen ins Kollektiv.
- Ranglistenzeilen zeigen Position, Anzeigename und Wert; die eigene Zeile ist hervorgehoben. Nullwerte werden nicht gelistet.
- Meilensteine (25, 50, 75, 100 Prozent und definierte Stationen) erzeugen genau ein Feed-Ereignis und eine kollektiv formulierte Benachrichtigung.

### 6.3 Arena-Projektion

Für Challenges mit Sichtbarkeit Arena berechnet die Domäne je teilnehmendem Tenant den Pro-Kopf-Wert auf die Tenant-Sollstärke, die Größenklasse und den Aktualisierungszeitpunkt und übergibt sie als Ereignis an den Arena-Dienst. Unter fünf beitragenden Personen wird kein Wert übergeben, nur „nimmt teil“. Pseudonyme Einzelwerte werden nur bei doppelter Zustimmung übergeben (A-022). Die Arena selbst ist ein eigenes Thema.

## 7. Buddy-System (A-043)

- Zwei Mitglieder desselben Tenants verbinden sich über eine Einladung per Anzeigename oder QR-Code; die andere Seite bestätigt ausdrücklich.
- Beide sehen gegenseitig Serie, Tagesziel und Fortschritt in gemeinsamen Challenges, unabhängig von der Sichtbarkeitsstufe. Das ist die einzige Ausnahme von „die restriktivere Einstellung gewinnt“ (A-022).
- Jede Seite kann die Verbindung jederzeit einseitig beenden; die Wirkung endet sofort.
- Eine Person hat höchstens einen Buddy zugleich.
- Wöchentlich erhalten beide einen gemeinsamen Anstoß über Benachrichtigungen; Buddy-Abzeichen vergibt Fortschritt.
- Die Verbindung ist für niemanden außer den beiden sichtbar, auch nicht für Botschafter.
- Austritt einer Seite beendet die Verbindung.

## 8. Schnittmengen dieses Themas

| Schnittmenge | Festlegung |
|---|---|
| Challenges ↔ Fortschritt | jeder Beitrag erzeugt genau ein pauschales Aktivitätsereignis; Messwerte bleiben in der Challenge; Serienschutz und Abzeichen gehören Fortschritt |
| Challenges ↔ Organisation | Aggregation nur entlang der Gruppendimensionen; Beiträge behalten die Gruppe zum Beitragszeitpunkt; pro Kopf auf Sollstärke zum Start |
| Challenges ↔ Datenschutz | Sichtbarkeit, Mindestzahl und Ranglistenschwelle über die zentrale Leseregel; sensible Kategorie ohne Aggregate; Foto-Belege gruppensichtbar |
| Challenges ↔ Zugang | Kiosk-Vorgangskennung und Offline-Idempotenzschlüssel; Nachfrist für Offline-Synchronisierung |
| Challenges ↔ Feed und Benachrichtigungen | Ereignisse für Start, Meilenstein, Ende, Belohnung eingelöst, Raster-Reihe; nie individuelle Rückstände |
| Challenges ↔ Entitlements | Erfassungsart „automatisch“ und Sichtbarkeit „Arena“ nur mit freigeschalteten Modulen; Vorlagen mit Ersatzmetrik |
| Challenges ↔ Metering | je Beitrag ein Metering-Ereignis; aktive Challenge je Monat und Teilnehmertage als Metriken |
| Challenges ↔ Arena | Projektion als Ereignis; keine Beitragssummen, keine Teilnehmerzahlen |
| Challenges ↔ Wearable-Vault | nur abgeleitete Tageswerte; höherer Wert von manuell und automatisch bis zum Deckel |

## 9. Nachweise

1. Jede der sieben Wettbewerbsformen lässt sich aus den Achsen konfigurieren; Rangliste und Duell zeigen den Wirkungshinweis.
2. Beitrag über dem Tagesdeckel wird gekappt; Rückdatierung um vier Tage abgelehnt; Zukunft abgelehnt.
3. Derselbe Beitrag zweimal übertragen (Kiosk und Offline): genau ein Beitrag, ein Aktivitätsereignis, ein Metering-Ereignis.
4. Offline-Beitrag 40 Stunden nach Ende mit Zeitpunkt im Zeitraum wird angenommen; nach 50 Stunden abgelehnt; Endstand danach unverändert.
5. Korrektur eines Beitrags erzeugt Gegenbuchung; Kollektivstand und Rangliste stimmen; Abzeichen bleibt.
6. Rangliste erscheint erst bei fünf Beitragenden und 40 Prozent der Aktivierten; Person mit „Nur für mich“ fehlt in Rangliste, zählt im Kollektiv.
7. Pro-Kopf-Duell ohne Sollstärke einer Gruppe wird im Wizard blockiert; Sollstärke-Änderung während der Challenge ändert die Wertung nicht.
8. Relative Verbesserung: ohne Baseline keine Wertung, Beitrag im Kollektiv; mit Baseline korrektes Verhältnis ab Tag acht.
9. Sensible Kategorie: keine Teilnehmerzahl, kein Feed-Ereignis, kein Aggregat, Sichtbarkeit nicht änderbar.
10. Buddy: gegenseitige Sicht trotz „Nur für mich“, sofortiges Ende bei Widerruf, für Dritte unsichtbar.
11. Vorlage innerhalb von 12 Monaten wiederholt: Warnung, Übersteuerung protokolliert.
12. Arena-Projektion enthält nur Pro-Kopf-Wert, Größenklasse und Zeitpunkt; unter fünf nur „nimmt teil“.
