# CompanyHero – Onboarding und Rollout

**Stand:** 21.09.2026  
**Status:** Beschlossen gemäß A-093 bis A-095 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung.  
**Art:** Querschnittsthema über Zugang, Organisation, Challenges, Fortschritt, Feed, Benachrichtigungen und Marke.  
**Bezug:** [Zugang und Identität](zugang-und-identitaet.md), [Organisation und Mandanten](organisation-und-mandanten.md), [Challenges](challenges.md), [Benachrichtigungen](benachrichtigungen.md).

Adoption ist kein Feature, sondern ein Ablauf. Dieses Thema legt fest, wie eine Person in unter 90 Sekunden zu ihrem ersten Fortschritt kommt und wie ein Tenant vom ersten Rollencode zum laufenden Programm geführt wird.

## 1. Grundsätze

- **Nullwerte sind verboten.** Kein Screen zeigt einer neuen Person eine Null, ein leeres Diagramm oder eine Rangliste mit Nullen. Leerzustände zeigen die nächste Handlung.
- **Ein Weg hinein ohne Firmen-E-Mail.** QR oder Code, Anzeigename, Gruppe, Sichtbarkeit, Zugang sichern, erste Handlung. Kein Klarname, kein Passwort, keine Körperdaten.
- **Freiwilligkeit ist sichtbar.** Die Sichtbarkeitswahl ist ein eigener Schritt ohne Voreinstellung; der Status bleibt als Chip im Profil.
- **Der Feed ist am Tag 1 nicht leer.** Kickoff-Challenge, Botschafter-Beiträge und System-Ereignisse sorgen dafür.
- **Der Kern erzeugt eigenen Fortschritt.** Auch ohne Modul liefert der Check-in Punkte, Serie und das erste Abzeichen.

## 2. Beitritt einer Person (A-093)

### 2.1 Ablauf auf dem eigenen Gerät

| Schritt | Inhalt | Regel |
|---|---|---|
| 1 | QR scannen oder Code eingeben | Beitrittscode nach A-014 |
| 2 | Tenant-Vorschau: Logo, Produktname, ein Satz, was das ist | Anrede und Tonalität des Tenants |
| 3 | Anzeigename, Vorschlag vorbelegt; Hinweis „Du kannst auch einen Spitznamen verwenden“ | eindeutig je Tenant |
| 4 | Sprache, falls M11 mehrere Sprachen bietet | sonst übersprungen |
| 5 | Gruppe je Dimension antippen, falls der Tenant Gruppen pflegt; Vorbelegung aus dem Code | ein Tap je Dimension |
| 6 | Sichtbarkeit: drei gleichrangige Kacheln ohne Voreinstellung | Pflichtwahl (A-022) |
| 7 | Zugang sichern: Passkey, E-Mail für Magic-Link oder externer Anbieter; ohne E-Mail Wiederherstellungscode anzeigen | nach Tenant-Anmeldewegen (A-015) |
| 8 | Großflächenmodus anbieten, ein Tap | (A-081) |
| 9 | **Erste Handlung:** Kickoff-Challenge-Beitrag oder Check-in; mit M3 zusätzlich „3 Minuten Bewegungspause jetzt?“ | Messpunkt: Start der Handlung |
| 10 | Erstes Abzeichen und Erfolgsmoment | (A-046) |
| 11 | „Zum Startbildschirm hinzufügen“ mit plattformspezifischer Anleitung | überspringbar |
| 12 | Push-Erklärung mit Button | erst nach Installation; überspringbar (A-059) |

Zielwert: vom QR-Code bis zum **Start** der ersten Handlung höchstens 90 Sekunden, ohne Tastatur außer dem Anzeigenamen. Schritte 11 und 12 sind später unter „Ich“ erreichbar.

### 2.2 Ablauf am Kiosk

Schritte 1 bis 6 wie oben im Großflächenmodus; Schritt 7 zeigt Kiosk-Kennung und lässt die PIN wählen; Wiederherstellungscode zum Notieren; Schritt 9 Check-in oder Kiosk-Beitrag; Schritt 10 Abzeichen; danach „Auf mein Handy übertragen“ per QR-Link als Angebot (A-018). Schritte 11 und 12 entfallen.

### 2.3 Wiederkehrende Personen

Beim zweiten Öffnen: Start mit Kopfkarte und Check-in; keine erneuten Erklärungen. Fehlende Schritte (Installation, Push, Gruppe) erscheinen als je eine Karte im Feed, höchstens einmal je Woche, verwerfbar.

## 3. Einrichtung eines Tenants und Rollout-Choreografie (A-094)

### 3.1 Geführter Programmstart

Der erste Tenant-Admin löst seinen Rollencode ein (A-033) und landet im geführten Programmstart mit Checkliste. Die freie Verwaltung ist parallel erreichbar; der Rollout wird erst freigegeben, wenn die Pflichtschritte erledigt sind.

| Schritt | Pflicht | Inhalt |
|---|---|---|
| Stammdaten prüfen | ja | Firmierung, Zeitzone, Standardsprache, Rechnungskontakt (A-033) |
| Gruppen und Sollstärken | ja | Dimensionen benennen, Gruppen anlegen oder per CSV, Sollstärke gesamt und je Gruppe (A-034) |
| Marke | ja | Produktname, Logo, Saatfarbe, Anrede, Tonalität; Vorschau in beiden Modi (A-077) |
| Anmeldewege | ja | Wege prüfen, Anbieter konfigurieren, Kiosk-Geräte registrieren (A-015, A-018) |
| Module | ja | Kern plus gebuchte Module prüfen; Kostenvorschau (A-066) |
| Programm-Manager | ja | mindestens einen Rollencode ausstellen, falls der Admin die Rolle nicht selbst übernimmt |
| Kickoff-Challenge | ja | vorbelegt: Sammelziel mit Meilenstein-Route, Häkchen oder aktive Minuten, Sichtbarkeit Firma, synchroner Start, Belohnung als Freitext oder Gutschein (A-040, A-085) |
| Botschafter | empfohlen | ein Botschafter je 50 gemeldete Personen; Rollencodes ausstellen; die Verwaltung zeigt das Verhältnis |
| Beitrittscodes | ja | je Standort oder Schicht mit Gruppenvorbelegung; Gültigkeit und Limits |
| Aushang und Ankündigung | ja | Aushang-PDF und Ankündigungstext in Tonalität erzeugen |
| Vorschau | ja | Mitglieder-App als Person sehen, bevor der Rollout startet |
| Starttermin | ja | Tag 0 festlegen; alle Gruppen starten synchron |

### 3.2 Choreografie

| Phase | Zeitpunkt | Plattform |
|---|---|---|
| Vorbereitung | ab Einrichtung | Checkliste, Erinnerungen an offene Pflichtschritte |
| Botschafter | eine Woche vor Start | Botschafter lösen Codes ein, durchlaufen ihr 10-Minuten-Onboarding und setzen die ersten drei Beiträge, damit der Feed am Tag 0 nicht leer ist |
| Start | Tag 0 | Kickoff-Challenge startet synchron; Aushänge hängen; Ankündigung als Push für bereits Beigetretene |
| Zwei Wochen | Tag 1 bis 14 | tägliche System-Ereignisse, Meilenstein-Benachrichtigungen, wöchentlicher Aushang-Zettel für Personen ohne Gerät |
| Auswertung | Tag 15 | Verwaltung zeigt Aktivierungsquote als Schätzgröße gegen die Sollstärke, Beitritte über Kiosk als Zahl ab fünf, Installationsanteil ab fünf, und eine Empfehlung für die nächste Challenge; kein Gesundheitsbericht |
| Rhythmus | ab Woche 3 | monatliche Challenge-Kadenz, Quartalsthema, keine Vorlage zweimal in 12 Monaten (A-040); Sollstärke vierteljährlich prüfen |

### 3.3 Botschafter-Onboarding

Zehn Minuten in der App: Rolle und Grenzen (nur eigene Gruppen, nur freigegebene Vorlagen, keine Einzelwerte), erste Challenge aus Vorlage anlegen, ersten Beitrag schreiben, Aushang für die Gruppe erzeugen, Kiosk-Check-in vorführen. Abschluss erzeugt das Abzeichen „Erste Gruppe“.

## 4. Rollout-Werkzeuge und Kennzahlen (A-095)

### 4.1 Werkzeuge

| Werkzeug | Inhalt |
|---|---|
| Aushang-PDF | A4 mit Tenant-Marke, QR-Code des Beitrittscodes, Kurzanleitung in drei Schritten, Ankündigungstext in Tonalität; je Standort oder Schicht mit eigenem Code |
| Wöchentlicher Aushang-Zettel | Kollektivstand, nächster Meilenstein, Wochenthema, QR (A-062) |
| Ankündigungstexte | für Aushang, E-Mail des Tenants und Ankündigungs-Beitrag; drei Tonalitätsvarianten aus dem Textkatalog, bearbeitbar |
| Rollout-Checkliste | Pflicht- und Empfehlungsschritte mit Status, Erinnerungen als Verwaltungsbenachrichtigung |
| Kiosk-Einrichtung | Registrierungscode, Aufstellhinweise, Testanmeldung |
| Vorschau als Person | die Mitglieder-App im Tenant-Theme ohne echte Daten |
| Beiblatt für den Betriebsrat | aus den Nachweisdokumenten des Betreibers (A-025) mit Tenant-Daten befüllt |

### 4.2 Kennzahlen des Rollouts

Nutzungskennzahlen je Tenant, keine Gesundheitskennzahlen. Sie sind Produktziele des Betreibers, in der Verwaltung als Rollout-Fortschritt sichtbar, Aggregate ab fünf Personen, Bezugsgröße die gemeldete Sollstärke als ausgewiesene Schätzgröße.

| Kennzahl | Zielwert | Messung |
|---|---|---|
| Aktivierung (Beitritt plus erste Handlung) in 14 Tagen | mindestens 60 % der Sollstärke | Personen mit erster Handlung |
| Anteil ohne hinterlegte E-Mail und ohne Anbieter unter den Aktivierten | mindestens 30 % | Anmeldewege, nur als Anteil |
| Beitritte über Kiosk | gemessen, kein Zielwert | Anzahl ab fünf |
| Vier-Wochen-Retention | mindestens 40 % | Personen mit Handlung in Woche 4 |
| Zwölf-Wochen-Retention | mindestens 25 % | Personen mit Handlung in Woche 12 |
| Teilnahme an der laufenden Challenge | mindestens 50 % der Aktivierten | Beitragende |
| Installiert und Push aktiv | mindestens 50 % der Aktivierten | Abonnements, nur als Anteil |
| Vom QR bis zum Start der ersten Handlung | höchstens 90 Sekunden | Median der Beitritte, ohne Personenbezug |

Keine dieser Zahlen steuert Sichtbarkeit; die Mindestzahl und die Ranglistenschwelle beziehen sich auf aktivierte Personen (A-023). Eine schlecht gepflegte Sollstärke verfälscht den Bericht, öffnet aber keine Auswertung.

### 4.3 Verbotene Muster

Diese Muster sind in Entwurf und Umsetzung ausgeschlossen: Werbe-Overlays über Inhalten; Paywall-Marker auf Inhalten; Fortschrittsanzeigen ohne Daten; Ranglisten mit Nullen; Punkte, die nur wieder Inhalte kaufen; Fachsprache in Titeln; Pflicht-Gesundheitsprofil vor dem ersten Nutzen; Fremd-Login mitten im Produkt; ausgegraute Funktionen „nur in der App“; Duplikate in Katalogen; Verzichts-Challenges als Vorgabe; Streak-Bruch als Strafe.

## 5. Schnittmengen dieses Themas

| Schnittmenge | Festlegung |
|---|---|
| Onboarding ↔ Zugang | Beitrittscodes, Anmeldewege, Kiosk-Kennung und -PIN, Übertragung per QR |
| Onboarding ↔ Datenschutz | Sichtbarkeitswahl ohne Voreinstellung; Kennzahlen ab fünf; keine Personenlisten |
| Onboarding ↔ Fortschritt | erste Handlung, erstes Abzeichen, Erfolgsmoment |
| Onboarding ↔ Challenges | Kickoff-Challenge vorbelegt; synchroner Start |
| Onboarding ↔ Feed und Benachrichtigungen | Botschafter-Beiträge vor Tag 0; Erinnerungskarten; Ankündigungs-Push; Aushang |
| Onboarding ↔ Marke | Tenant-Vorschau, Tonalität der Texte, Manifest je Tenant für die Installation |
| Onboarding ↔ Organisation | Gruppen und Sollstärken als Pflichtschritt; Botschafterverhältnis |
| Onboarding ↔ Verwaltung | geführter Programmstart, Checkliste, Rollout-Fortschritt |

## 6. Nachweise

1. Beitritt ohne E-Mail auf einem Mittelklassegerät: Start der ersten Handlung unter 90 Sekunden; keine Tastatur außer Anzeigename.
2. Sichtbarkeitsschritt ohne Voreinstellung; Weiter ohne Wahl nicht möglich.
3. Nach dem Beitritt: ein verdientes Abzeichen, Kopfkarte, Check-in, kein Nullwert, kein leeres Diagramm.
4. Push-Prompt erst nach Button; auf iOS erst nach Installation.
5. Kiosk-Beitritt endet mit Kennung und PIN; Übertragung per QR eröffnet Sitzung auf dem Handy.
6. Rollout ohne erledigte Pflichtschritte nicht freigebbar; Kickoff-Challenge startet synchron am Tag 0.
7. Rollout-Fortschritt zeigt Kennzahlen nur ab fünf und als Anteil der Sollstärke mit Kennzeichnung als Schätzgröße.
8. Feed am Tag 0 enthält Botschafter-Beiträge und System-Ereignisse.
