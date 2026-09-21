# CompanyHero – Wearable-Vault

**Stand:** 21.09.2026  
**Status:** Beschlossen gemäß A-090 bis A-092 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung.  
**Dienst:** Wearable-Vault, isolierter Dienst gemäß [Domänenkarte](domaenen-und-schnittmengen.md); Modul M8.  
**Bezug:** [Challenges](challenges.md), [Fortschritt](fortschritt.md), [Datenschutz und Nachweis](datenschutz-und-nachweis.md), [Entitlements](entitlements.md), [Betrieb](betrieb.md).

Wearables sind ein Komfortmodul, keine Grundlage. Die Plattform funktioniert vollständig ohne Gerät; wer eines hat, spart die Erfassung. Die Herstellerprogramme verlangen, dass ihre Daten nicht an Dritte gelangen und nicht von KI-Diensten verarbeitet werden. Der Vault erfüllt das als Infrastruktur, nicht als Versprechen: ein eigener Dienst, der Rohdaten hält und nur abgeleitete Tageswerte abgibt.

## 1. Grundsätze

- **Rohdaten verlassen den Vault nicht.** Keine Zeitreihen, keine Herzfrequenz, keine Schlafphasen, keine Positionen, keine Körperwerte gelangen in den Monolithen, in Exporte, in Logs oder an Dritte.
- **Der Vault kennt keine Personen.** Er speichert Herstellertokens und Rohdaten unter einem opaken Subjektschlüssel, den nur der Monolith einer Person zuordnen kann.
- **Nur Tagesübersichten:** Es werden ausschließlich Tagesübersichten (Schritte, aktive Minuten, Distanz) und die Schlafdauer bezogen. Herzfrequenz, Positionsdaten, Aktivitätsdetails und Körperdaten werden nicht angefragt.
- **Keine KI, keine Dritten:** Der Vault hat keine ausgehenden Verbindungen außer zu freigegebenen Hersteller-APIs. Keine Telemetrie mit Nutzdaten, kein Data Warehouse, kein KI-Dienst.
- **Der Admin sieht nie, wer verbunden hat.** M8 wird als Flatrate je Tenant abgerechnet; Verbindungen erscheinen in keiner Verwaltung, keinem Aggregat und keiner Rechnung.
- **Eine Verbindung je Person.** Eine neue Verbindung trennt die alte.

## 2. Dienst und Isolation (A-090)

### 2.1 Aufbau

| Eigenschaft | Festlegung |
|---|---|
| Prozess | eigener API- und Worker-Prozess aus eigener Codebasis im selben Stack, eigenes Image, eigenes Compose-Projekt mit eigenem Netz (A-028) |
| Datenspeicher | eigene PostgreSQL-Datenbank mit eigenen Zugangsdaten; Rohdaten zusätzlich feldverschlüsselt mit Vault-eigenem Schlüssel in einem eigenen OpenBao-Namensraum (A-026) |
| Eingehend | zwei Routen über den Reverse Proxy: OAuth-Callback der Hersteller und Webhooks der Hersteller, beide signaturgeprüft und ratenbegrenzt; sonst nur intern vom Monolithen erreichbar |
| Ausgehend | ausschließlich zu Hersteller-Endpunkten über eine Allowlist auf Netzebene; jede andere Verbindung wird verworfen und alarmiert |
| Geheimnisse | Client-Secrets je Hersteller in OpenBao mit Vault-eigener Richtlinie; Herstellertokens verschlüsselt in der Vault-Datenbank; nie im Monolithen |
| Beobachtung | Logs ohne Nutzdaten und ohne Subjektschlüssel im Klartext; Metriken nur als Zähler (Verbindungen, Synchronisierungen, Fehler) |
| Backup | wie der Monolith (A-031), eigenes verschlüsseltes Repository; Rohdaten unterliegen der 35-Tage-Grenze und werden bei Wiederherstellung nachgelöscht |
| Support | kein Operator-Zugriff auf Vault-Inhalte, auch nicht mit Vorgangsnummer (A-025) |

### 2.2 Subjektmodell

1. Beim Verbinden erzeugt der Monolith einen zufälligen **Subjektschlüssel** und speichert die Zuordnung Person ↔ Subjektschlüssel ↔ Tenant ausschließlich im Monolithen, feldverschlüsselt mit Tenant-Schlüssel.
2. Der Vault erhält den Subjektschlüssel, den Hersteller und die Metriken, die der Tenant beziehen darf; nie Person, Anzeigename, Tenant-Name oder Gruppen.
3. Abgeleitete Werte gehen mit dem Subjektschlüssel an den Monolithen; dieser ordnet sie der Person zu und verwirft die Zuordnung bei Trennung.
4. Ein kompromittierter Vault enthält damit Rohdaten ohne Personenbezug; ein kompromittierter Monolith enthält Personen ohne Rohdaten.

### 2.3 Schnittstellen

| Richtung | Inhalt | Transport |
|---|---|---|
| Monolith → Vault | Verbindungsauftrag (Subjektschlüssel, Hersteller, erlaubte Metriken), Synchronisierungsanstoß, Trennung, Löschung, Rohdaten-Exportauftrag | interne API, Jobs aus der Queue (A-006), idempotent |
| Vault → Monolith | abgeleitete Tageswerte je Subjektschlüssel, Verbindungsstatus, Zeitpunkt der letzten Übertragung, Exportdatei als signierter Verweis | interne API mit Dienstzugangsdaten; der Monolith bucht Werte als Fachereignisse |
| Hersteller → Vault | OAuth-Callback, Webhook-Benachrichtigungen | öffentliche Routen mit Signatur- und Zustandsprüfung |
| Vault → Hersteller | Token-Austausch, Abruf von Tagesübersichten und Schlafdauer, Backfill, Token-Erneuerung, Widerruf | Allowlist |

## 3. Hersteller, Verbindung und Datenumfang (A-091)

### 3.1 Adaptermodell

Jeder Hersteller ist ein Adapter mit OAuth-2.0-Ablauf, Endpunkten für Tagesübersichten und Schlafdauer, Webhook-Format und Widerrufsweg. Startadapter: **Garmin, Fitbit, Polar, Withings.** Weitere Hersteller sind zusätzliche Adapter ohne Konzeptänderung. Der Operator schaltet einen Hersteller erst frei, wenn der Programmzugang erteilt ist und die Datenschutzerklärung die verlangten Aussagen enthält (A-020); M8 ist nur buchbar, wenn mindestens ein Hersteller freigeschaltet ist (A-064).

Apple Health und Health Connect haben keine Web-Schnittstelle und sind nicht anbindbar; die Oberfläche sagt das offen (Abschnitt 7).

### 3.2 Verbindung

1. „Ich → Geräte → Hersteller wählen“. Die App erklärt, welche Daten bezogen werden, was daraus wird und was nie den Vault verlässt.
2. Der Monolith erzeugt den Verbindungsauftrag; der Vault startet den OAuth-Ablauf im Systembrowser mit den minimalen Scopes aus 3.3.
3. Nach dem Callback speichert der Vault die Tokens verschlüsselt und meldet „verbunden“; der Monolith zeigt Quelle und Zeitpunkt.
4. Backfill der letzten sieben Tage, gedeckelt und nur innerhalb laufender Challenge-Zeiträume wirksam.
5. Laufend: Webhooks des Herstellers lösen den Abruf aus; zusätzlich ein täglicher Abruf um 03:00 Tenant-Zeit als Rückfall; „Jetzt synchronisieren“ in der App stößt einen Abruf an, höchstens einmal je 15 Minuten.
6. Trennen: Token beim Hersteller widerrufen, Verbindung beendet, Rohdaten binnen 30 Tagen gelöscht, abgeleitete Werte bleiben. Eine neue Verbindung trennt die alte.

### 3.3 Bezogene Daten

| Bezogen | Nicht bezogen |
|---|---|
| Tagesübersicht: Schritte, aktive Minuten, Distanz | Herzfrequenz, Herzfrequenzvariabilität, Sauerstoffsättigung |
| Schlafdauer je Nacht | Schlafphasen, Schlafqualität |
| Zeitpunkt der letzten Synchronisierung des Geräts | Positionsdaten, Strecken, Aktivitätsdetails |
| | Gewicht, Körperfett, Körpermaße, Kalorien |
| | Stress-, Energie- oder Erholungswerte |

Die Scopes im Programmantrag entsprechen genau der linken Spalte. Der Tenant konfiguriert nichts außer der Buchung (A-064); welche Metriken bezogen werden, bestimmt allein der Katalog.

### 3.4 Ableitung und Export aus dem Vault

Je Subjektschlüssel und Kalendertag in der Tenant-Zeitzone:

| Abgeleiteter Wert | Regel |
|---|---|
| Schritte | Tagessumme, gerundet auf 100, gedeckelt auf 25.000 |
| Aktive Minuten | Tagessumme, gerundet auf 5, gedeckelt auf 300 |
| Distanz | Tagessumme, gerundet auf 0,5 km, gedeckelt auf 30 km |
| Schlafziel erreicht | ja, wenn Schlafdauer mindestens sieben Stunden; sonst nein; kein Stundenwert |
| Datenstand | Zeitpunkt der letzten Übertragung |

Ein Tag gilt als abgeschlossen um 03:00 des Folgetages; verspätete Herstellerdaten aktualisieren den Wert bis 48 Stunden danach, passend zur Nachfrist der Challenges. Nichts anderes verlässt den Vault. Rohdaten werden nach 90 Tagen gelöscht; abgeleitete Werte liegen im Monolithen.

### 3.5 Verwendung im Monolithen

- Challenges: Erfassungsart „automatisch“ für die Metriken Schritte, aktive Minuten, Distanz und Schlafziel; bei manueller und automatischer Erfassung am selben Tag gilt der höhere Wert bis zum Deckel (A-039).
- Fortschritt: höchstens eine Handlung je Metrik und Tag, wenn der Tageswert das Ziel der Person oder Challenge erreicht; zählt nicht als aktives Mitglied (A-044, A-071).
- Sichtbarkeit: abgeleitete Werte unterliegen der Sichtbarkeitsstufe der Person wie jeder Beitrag; in Aggregaten erst ab fünf.

## 4. Transparenz, Rechte und Löschung (A-092)

### 4.1 Oberfläche für die Person

Unter „Ich → Geräte“: Hersteller, Verbindungszustand, Zeitpunkt der letzten Übertragung, welche Metriken bezogen werden, „Jetzt synchronisieren“, „Trennen“, „Rohdaten exportieren“. Bei Fehlern (abgelaufenes Token, widerrufener Zugriff beim Hersteller) ein Hinweis mit Neuverbindung. Am Kiosk gibt es keine Geräteverwaltung.

### 4.2 Rechte

- **Auskunft und Export:** Der Selbstexport (A-025) enthält die abgeleiteten Werte. Auf Wunsch erzeugt der Vault einen Export der Rohdaten der letzten 90 Tage als Datei; der Monolith liefert einen signierten, 24 Stunden gültigen Verweis. Der Vault speichert die Datei höchstens 24 Stunden.
- **Löschung:** Trennen löscht Rohdaten binnen 30 Tagen; Austritt löscht sofort Tokens, Rohdaten, Subjektschlüssel und Zuordnung; Tenant-Kündigung und Kündigung von M8 trennen alle Verbindungen des Tenants zum Kündigungstermin; Tokens werden widerrufen, Rohdaten binnen 30 Tagen gelöscht, abgeleitete Werte bleiben bis zur Tenant-Löschung (A-024, A-066).
- **Widerruf beim Hersteller:** Widerruft die Person den Zugriff im Herstellerkonto, erkennt der Vault den Fehler beim nächsten Abruf, beendet die Verbindung und startet die 30-Tage-Löschung.

### 4.3 Was niemand sieht

Tenant-Admin, Programm-Manager, Botschafter, Einsichtsrolle, Partner und Operator sehen weder Verbindungen noch Rohdaten noch, welche Personen Wearables nutzen. Die Einsichtsrolle sieht, dass M8 aktiv ist und welche Hersteller freigeschaltet sind. Metering führt nur `wearable.tenant_month`.

### 4.4 Betrieb und Antrag

- Vor jedem Programmantrag: Datenschutzerklärung mit der verlangten Aussage zur KI-Verarbeitung (A-020), Scopes gemäß 3.3, Nachweis der Isolation gemäß Abschnitt 2. Der Zugang ist erhältlich, aber neu zu beantragen und an laufende Auflagen gebunden; deshalb wird der Antrag früh gestellt und ein Hersteller erst nach Erteilung freigeschaltet.
- Entzieht ein Hersteller den Zugang, deaktiviert der Operator den Hersteller; bestehende Verbindungen werden beendet, Rohdaten gelöscht, Personen benachrichtigt; M8 bleibt buchbar, solange ein anderer Hersteller freigeschaltet ist (A-066).
- Herstellerbedingungen werden jährlich geprüft; Änderungen an Scopes oder Auflagen sind Registerentscheidungen.

## 5. Schnittmengen dieses Themas

| Schnittmenge | Festlegung |
|---|---|
| Vault ↔ Challenges | abgeleitete Tageswerte für Erfassungsart automatisch; höherer Wert bei Doppel; Nachfrist 48 Stunden; Backfill sieben Tage |
| Vault ↔ Fortschritt | eine Handlung je Metrik und Tag bei Zielerreichung; nicht aktives Mitglied |
| Vault ↔ Zugang | Subjektschlüssel-Zuordnung im Monolithen feldverschlüsselt; Austritt löscht alles; keine Geräteverwaltung am Kiosk |
| Vault ↔ Datenschutz | 90 Tage Rohdaten, 30 Tage nach Trennung, sofort bei Austritt; Rohdaten-Export 24 Stunden; keine Verarbeitung durch Dritte oder KI; Datenschutzerklärung mit KI-Aussage |
| Vault ↔ Entitlements | Hersteller-Freischaltung durch Operator; M8 nur mit freigeschaltetem Hersteller; Kündigung trennt Verbindungen |
| Vault ↔ Metering | nur `wearable.tenant_month`; nie je Verbindung |
| Vault ↔ Betrieb | eigenes Compose-Projekt und Netz, Allowlist ausgehend, zwei öffentliche Routen mit Signaturprüfung, eigener OpenBao-Namensraum, Backup mit Nachlöschung |
| Vault ↔ Benachrichtigungen | Verbindungsfehler und Herstellerdeaktivierung als Kategorie Konto und Sicherheit, ohne Nutzdaten |

## 6. Nachweise

1. Vault-Datenbank enthält nach einem Testlauf keine Personen-IDs, Anzeigenamen oder Tenant-Namen; Rohdaten sind ohne Vault-Schlüssel nicht lesbar.
2. Ausgehende Verbindung des Vault-Containers zu einem nicht freigegebenen Ziel wird verworfen und alarmiert.
3. OAuth-Ablauf mit minimalen Scopes; Anfrage nach Herzfrequenz oder Position existiert im Code nicht.
4. Export aus dem Vault enthält nur die fünf abgeleiteten Felder; Schlaf nur als ja oder nein.
5. Doppelter Webhook erzeugt keine doppelte Handlung; manuell und automatisch am selben Tag ergibt den höheren Wert bis zum Deckel.
6. Trennen: Token widerrufen, Rohdaten nach 30 Tagen weg, abgeleitete Werte bleiben; Austritt: alles sofort weg; Herstellerwiderruf erkannt.
7. Kein Verwaltungs-, Partner- oder Operator-Bildschirm zeigt Verbindungen; Rechnung zeigt nur die Flatrate.
8. Backfill sieben Tage wirkt nur in laufenden Challenge-Zeiträumen und gedeckelt.
9. Rohdaten-Export der Person nach 24 Stunden nicht mehr abrufbar und im Vault gelöscht.
10. Support-Zugriff mit Vorgangsnummer erreicht den Vault nicht.

## 7. Apple Health, Health Connect und Startadapter

- **Apple Health und Health Connect** sind nicht anbindbar, weil beide keine Web-Schnittstelle bieten. Eine native Brücken-App ist nicht eingeplant. Die Oberfläche sagt das offen und verweist auf Selbstberichtung als vollwertigen Weg. Sollte eine Brücken-App später gewünscht sein, ist das eine eigene Registerentscheidung.
- **Startadapter** sind Garmin, Fitbit, Polar und Withings. Weitere Hersteller sind zusätzliche Adapter ohne Konzeptänderung.
