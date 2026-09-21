# CompanyHero – Fortschritt

**Stand:** 20.09.2026  
**Status:** Beschlossen gemäß A-044 bis A-048 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung.  
**Domäne:** Fortschritt gemäß [Domänenkarte](domaenen-und-schnittmengen.md).  
**Bezug:** [Challenges](challenges.md), [Datenschutz und Nachweis](datenschutz-und-nachweis.md).

## 1. Grundsätze

- Vier Primitive, von allen Domänen bespielt: Punkte, Serie, Abzeichen, Stufen. Sie sind Anerkennung, keine Zugangskontrolle und keine Währung.
- **Aktivitätsgleichheit:** Jede Handlung zählt gleich. Ein Check-in, eine Atemübung, ein abgeschlossenes Video und ein langer Lauf erzeugen denselben Punktwert. Messwerte zählen nur innerhalb ihrer Challenge.
- **Nullwerte sind verboten:** Kein Screen zeigt einer neuen Person eine Null, ein leeres Diagramm oder ein graues Abzeichen. Leerzustände zeigen die nächste Handlung.
- Punkte verfallen nicht, sind nicht ausgebbar und nie negativ. Es gibt keinen Abzug für nichts.
- Verdiente Anerkennung wird nie zurückgenommen. Stufen und Abzeichen bleiben, auch wenn Punkte durch Korrektur sinken.
- Alle Bezeichnungen (Punkte, Stufen, Serie) sind je Tenant über die Domäne Marke umbenennbar.

## 2. Aktivitätsereignisse und Punkte (A-044)

### 2.1 Aktivitätsereignis

Jede Handlung einer Person erzeugt genau ein Aktivitätsereignis mit Aktivitätsart, Zeitpunkt in der Tenant-Zeitzone, Quelle (selbst, Plattform, automatisch) und Verweis auf den Auslöser. Auslöser sind unter anderem: täglicher Check-in, Challenge-Beitrag, abgeschlossener Inhalt, gelesener Beitrag, bestandenes Quiz, Feed-Beitrag, gegebene Anerkennung, Ergonomie-Check, Atemübung, Veranstaltungsbesuch mit Check-in. Ein automatischer Tageswert aus dem Wearable-Vault erzeugt höchstens eine Handlung je Metrik und Tag, wenn er das Ziel erreicht.

Eine Korrektur oder Löschung des Auslösers erzeugt ein Gegenereignis, das die Wirkung zurücknimmt. Gegenereignisse sind Korrekturen, keine Strafen.

### 2.2 Punkte

| Regel | Festlegung |
|---|---|
| Wert | 10 Punkte je gewerteter Handlung, unabhängig von Art und Messwert |
| Tagesdeckel gesamt | 10 gewertete Handlungen je Tag; weitere Handlungen zählen für Challenges, erzeugen aber keine Punkte |
| Tagesdeckel je Art | Check-in 1, Challenge-Beiträge 3, Inhalte 3, gelesene Beiträge 2, Anerkennungen 3, Feed-Beiträge 1, Veranstaltungsbesuch 1, automatische Tageswerte 1 je Metrik |
| Saldo | kumuliert, verfällt nie, nie negativ; Gegenereignisse reduzieren den Saldo bis höchstens auf null |
| Verwendung | Stufen und persönliche Anzeigen; keine Einlösung, kein Handel, keine Übertragung |

Der Deckel wird der Person sichtbar gemacht („Tagesziel voll, weitere Beiträge zählen für deine Challenge“), nie als Fehler.

### 2.3 Automatische Quellen

Automatische Tageswerte zählen für Punkte und Serie wie eine Handlung, aber nicht für „aktives Mitglied“ in der Abrechnung, weil keine eigene Interaktion vorliegt. Fortschritt emittiert `member.active_month` und `member.active_day` genau einmal je Person und Periode bei der ersten gewerteten Handlung mit Quelle „selbst“ oder „Plattform“; automatische Tageswerte lösen keine dieser Emissionen aus (A-071).

## 3. Serie und Abwesenheit (A-045)

### 3.1 Serie

- Eine Serie ist die Zahl zusammenhängender Kalendertage in der Tenant-Zeitzone mit mindestens einer gewerteten Handlung.
- **Serienschutz:** Ein ausgelassener Tag je Kalendermonat wird automatisch überbrückt. Der Schutz wird angewendet, sobald der nächste Tag eine Handlung enthält; die Person sieht, dass er verbraucht ist.
- Ein Serienbruch senkt nie eine Stufe, ein Abzeichen oder eine erreichte Rangposition.
- Gespeichert werden aktuelle Serie, längste Serie und der Verbrauch des Serienschutzes je Monat.
- Wird die einzige Handlung eines Tages korrigiert, wird die Serie neu berechnet; der Serienschutz greift, wenn er im Monat noch verfügbar ist.

### 3.2 Abwesenheit

- Die Person setzt sich selbst abwesend „von–bis“. Es wird kein Grund gespeichert und keiner abgefragt.
- Wirkung: Serie pausiert ohne Bruch; Benachrichtigungen unterdrücken alle Kategorien außer Konto und Sicherheit sowie Plattform (A-060); Challenge-Teilnahmen bleiben bestehen.
- Beginn heute oder in der Zukunft, rückwirkend bis drei Tage; höchstens acht Wochen je Eintrag, beliebig verlängerbar.
- Abwesenheit ist für niemanden außer der Person sichtbar. Sie erscheint nicht in Aggregaten und nicht gegenüber dem Buddy; der Buddy sieht lediglich eine pausierte Serie.

## 4. Abzeichen (A-046)

### 4.1 Katalog

Der Operator pflegt einen Plattformkatalog von rund 30 Abzeichen in fünf Kategorien. Tenants und Partner legen keine eigenen Abzeichen an.

| Kategorie | Beispiele |
|---|---|
| Einstieg | erster Tag, erste Übung, Profil eingerichtet, erste Gruppe gewählt |
| Dranbleiben | 7, 30, 100 Tage Serie; Comeback nach Pause; ein Monat ohne Serienschutz |
| Gemeinsam | Gruppenziel erreicht, 10 Anerkennungen gegeben, Buddy vier Wochen, Firmenziel erreicht |
| Vielfalt | fünf Aktivitätsarten, Bewegung und Regeneration am selben Tag, Ergonomie-Check gemacht |
| Saison | Quartalsabzeichen, jährlich neu gestaltet, nie wiederholt |

Jedes Abzeichen hat eine Regel über Aktivitätsereignisse, Serie oder Challenge-Ereignisse, einen Text in Nutzersprache und eine Grafik aus dem Design-System. Regeln beziehen sich nie auf Messwerte einer Metrik, nur auf Handlungen, Tage und Kollektivereignisse.

### 4.2 Verleihung und Anzeige

- Verleihung erfolgt ereignisgetrieben durch Jobs nach jeder Handlung und täglich nach Tagesende. Jedes Abzeichen wird je Person höchstens einmal verliehen; Saisonabzeichen einmal je Saison.
- Verliehene Abzeichen werden nie entzogen, auch nicht nach Korrektur des auslösenden Ereignisses.
- Sichtbar sind verdiente Abzeichen mit Datum sowie genau ein nächstes erreichbares je Kategorie mit Fortschrittsanzeige. Alle weiteren existieren für die Person nicht sichtbar.
- Das erste Abzeichen entsteht im Beitritt selbst, damit niemand mit leerem Profil beginnt.
- Feed-Ereignisse zu Abzeichen folgen der Sichtbarkeitsstufe der Person. Zusätzlich gibt es tenantweite Sammelereignisse („Heute haben 12 Personen ein Abzeichen verdient“) nur ab fünf Personen.

## 5. Stufen (A-047)

| Stufe | Schwelle (kumulierte Punkte) | Voreingestellter Name |
|---|---|---|
| 1 | 0 | Neu dabei |
| 2 | 300 | Dabei |
| 3 | 1.000 | Dranbleiber |
| 4 | 3.000 | Vorbild |
| 5 | 10.000 | Urgestein |

- Namen benennen Beständigkeit, nicht Leistung, und sind je Tenant umbenennbar.
- Stufen schalten nichts frei, was Nutzen hätte. Inhalte oder Funktionen hinter Stufen zu sperren ist nicht möglich.
- Eine erreichte Stufe sinkt nie, auch wenn der Punktesaldo durch Korrekturen unter die Schwelle fällt.
- Der Stufenaufstieg erzeugt einen Erfolgsmoment in der App und ein Feed-Ereignis gemäß Sichtbarkeitsstufe.

## 6. Persönliche Anzeigen, Check-in und Signale (A-048)

### 6.1 Täglicher Check-in

Ein Kernelement, das auch ohne jedes Modul Fortschritt erzeugt: drei Kacheln „Bewegt“, „Pause gemacht“, „Erholt“, Mehrfachauswahl, ein Tap genügt. Kein Rating, keine Skala, kein Freitext. Der Check-in ist einmal je Tag möglich, am Kiosk ebenso. Er erzeugt eine Handlung und ein Feed-Ereignis gemäß Sichtbarkeitsstufe. Der Stimmungs-Check-in ist davon getrennt, ausschließlich privat und Teil eines eigenen Moduls.

### 6.2 Persönliches Tagesziel und Anzeigen

- Persönliches Tagesziel: 1 bis 3 Handlungen je Tag, Voreinstellung 1, von der Person einstellbar. Es dient dem Tagesring und der Serie, nicht der Abrechnung.
- **Tagesring:** Fortschritt zum Tagesziel, Zahl in der Mitte.
- **Wochenstreifen:** sieben Ringe, erledigte gefüllt.
- **Monatsraster:** Kalender mit aktiven Tagen.
- **Punkteverlauf:** erscheint erst ab 14 Tagen mit Daten; davor steht der Zeitpunkt, ab dem er erscheint.
- **Serien-Chip:** Flamme mit Tageszahl; bei pausierter Serie ein Pausensymbol.
- Alle Anzeigen sind nur für die Person selbst sichtbar; gegenüber anderen wirkt ausschließlich die Sichtbarkeitsstufe; der Buddy sieht Serie und Tagesring.

### 6.3 Rückblicke

- **Wochenrückblick** sonntags als private Karte: aktive Tage, Handlungen, verdiente Abzeichen, Anteil an Kollektivzielen. Keine Vergleiche mit anderen.
- **Jahresrückblick** als private, auf Wunsch teilbare Karte: rein positiv, ohne Vergleich, ohne Rangposition, ohne Messwerte.

### 6.4 Inaktivitätssignale

Fortschritt erkennt je Person den letzten Tag mit Handlung und erzeugt interne Ereignisse „7 Tage ohne Handlung“, „30 Tage“, „60 Tage“. Die Domäne Benachrichtigungen entscheidet über Zustellung und Wortlaut; die Botschaft ist immer kollektiv formuliert. Während einer Abwesenheit werden keine Signale erzeugt. Die Signale sind für keine Rolle sichtbar und fließen in keine Auswertung.

### 6.5 Aufbewahrung

Aktivitätsereignisse bleiben 24 Monate im Detail. Danach werden sie zu Monatsaggregaten je Person verdichtet: Handlungen je Art, aktive Tage, Punkte. Serie, Saldo, Abzeichen und Stufe bleiben bis zum Austritt. Der Selbstexport enthält Ereignisse, Aggregate, Abzeichen, Serienverlauf und Abwesenheiten.

## 7. Schnittmengen dieses Themas

| Schnittmenge | Festlegung |
|---|---|
| Fortschritt ↔ Challenges | ein Aktivitätsereignis je Beitrag; Gegenereignis bei Korrektur; Kollektivereignisse (Gruppenziel, Firmenziel) lösen Abzeichen aus; Messwerte bleiben in Challenges |
| Fortschritt ↔ Feed und Inhalte | abgeschlossene Inhalte, gelesene Beiträge, Anerkennungen und Feed-Beiträge sind Handlungen; Abzeichen, Stufen und Check-in erzeugen Feed-Ereignisse gemäß Sichtbarkeit |
| Fortschritt ↔ Benachrichtigungen | Inaktivitätssignale, Erfolgsmomente und Buddy-Anstöße als Ereignisse; Abwesenheit unterdrückt alle Kategorien außer Konto und Sicherheit sowie Plattform (A-060) |
| Fortschritt ↔ Datenschutz | Anzeigen nur für die Person; Sammelereignisse ab fünf; Abwesenheit ohne Grund und ohne Sichtbarkeit; Aufbewahrung nach A-024 |
| Fortschritt ↔ Metering | Handlungen sind die Basis für „aktives Mitglied“; automatische Tageswerte zählen dafür nicht |
| Fortschritt ↔ Marke | Bezeichnungen für Punkte, Stufen und Serie kommen aus der Tenant-Konfiguration |
| Fortschritt ↔ Wearable-Vault | abgeleiteter Tageswert wird zu höchstens einer Handlung je Metrik und Tag |

## 8. Nachweise

1. Zehn verschiedene Handlungen an einem Tag ergeben 100 Punkte; die elfte ergibt null Punkte, zählt aber in der Challenge; Deckel je Art greift.
2. Korrektur der einzigen Handlung eines Tages: Saldo sinkt, Serie wird neu berechnet, Serienschutz greift einmal im Monat, Stufe und Abzeichen bleiben.
3. Abwesenheit pausiert Serie und unterdrückt alle Signale; kein Grund gespeichert; für Dritte und Buddy nicht sichtbar außer als pausierte Serie.
4. Neue Person sieht nach dem Beitritt ein verdientes Abzeichen und höchstens fünf nächste; keine graue Kachel, kein leeres Diagramm.
5. Automatischer Tageswert erzeugt eine Handlung mit 10 Punkten und keinen Eintrag „aktives Mitglied“.
6. Stufe 3 erreicht, Saldo durch Korrektur unter 1.000: Stufe bleibt 3.
7. Sammelereignis zu Abzeichen erscheint erst ab fünf Personen.
8. Nach 24 Monaten existieren nur Monatsaggregate; Saldo, Abzeichen und Serienhistorie sind unverändert.
