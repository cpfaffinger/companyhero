# CompanyHero – Workshops und Events

**Stand:** 21.09.2026  
**Status:** Beschlossen gemäß A-082 bis A-086 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung.  
**Domäne:** Workshops und Events (Modul M9) gemäß [Domänenkarte](domaenen-und-schnittmengen.md).  
**Bezug:** [Challenges](challenges.md), [Entitlements](entitlements.md), [Metering und Abrechnung](metering-und-abrechnung.md), [Benachrichtigungen](benachrichtigungen.md), [Datenschutz und Nachweis](datenschutz-und-nachweis.md).

Workshops und Events bringen die Plattform in den Betrieb: Gesundheitstage, Ergonomie-Workshops, Vorträge, Impulseinheiten, Online-Sessions und interne Veranstaltungen. Das Modul liefert zugleich die **verdiente Belohnung**: Erreicht eine Gruppe oder der Tenant ein gemeinsames Ziel, wird ein Workshop freigeschaltet. Das ist die einzige Belohnung, die keine Software kopieren kann, und sie gilt immer allen, auch denen, die nicht mitgemacht haben.

## 1. Grundsätze

- **Kollektiv:** Eine freigeschaltete Belohnung steht dem ganzen Geltungsbereich offen. Es gibt keinen Vorrang für Teilnehmende einer Challenge und keine individuellen Belohnungen.
- **Pseudonym bis zur Tür:** Anmeldung unter Anzeigename, Zutritt per Ticket-QR. Klarnamen nur, wenn die Person sie je Anmeldung freigibt.
- **Teilnahme ist kein Aktivitätswert:** Wer an einem Rückenworkshop teilnimmt, erscheint in keiner Auswertung; sichtbar sind nur Anzahlen ab fünf.
- **Preise je Termin oder je Platzkontingent, nie je Teilnehmer.** Eine Rechnung verrät keine Teilnehmerzahl.
- **Keine Anbieter-Anmeldung in der Plattform:** Anbieter sind Stammdaten; Organisation, Check-in und Abschluss liegen bei Rollen des Tenants und des Operators.
- **Online-Formate laufen im Werkzeug des Anbieters.** Die Plattform verteilt den Zugangslink an Angemeldete; sie streamt nicht (A-054).

## 2. Angebotsmodell (A-082)

### 2.1 Entitäten

| Entität | Inhalt | Eigentümer |
|---|---|---|
| **Anbieter** | Name, Kontakt, Regionen, Formate, Sprachen, Vertragsart (Eigenleistung des Betreibers, externer Anbieter mit Provision, tenant-intern), Abrechnungshinweise | Operator; Partner für Partneranbieter; Tenant für interne Anbieter wie Betriebsarzt |
| **Angebot** | Titel in Nutzersprache, Beschreibung, Kategorie (Bewegung, Ergonomie, Regeneration, Ernährung, Wissen, Gesundheitstag, Vortrag), Format (Präsenz, Online, Hybrid), Dauer, Mindest- und Höchstteilnehmerzahl, Zielgruppe, benötigte Ausstattung und Raum, Barrierefreiheitsmerkmale, Sprachen, Bild, Preismodell (je Termin, je Platzkontingent, Provision), Stornobedingungen, Vorlaufzeit | Anbieter-Eigentümer |
| **Veranstaltung** | Angebot oder freie interne Veranstaltung, Tenant, Geltungsbereich (Tenant oder Gruppen), Zeitpunkt, Dauer, Ort (Standort aus Gruppen oder freie Adresse) oder Online-Link, Kapazität, Anmeldefrist, Abmeldefrist, Organisator, Status, optional Stationen | Tenant |
| **Station** | Teil eines Gesundheitstags: Titel, Zeitfenster, Kapazität, Ort im Gebäude | Tenant |
| **Anmeldung** | Person, Veranstaltung und Station, Zeitpunkt, Status (angemeldet, Warteliste, abgemeldet, eingecheckt), Ticket-Token, Klarname-Freigabe | Person |
| **Gutschein** | Verknüpfung Challenge → Angebot, Bedingung, Geltungsbereich, Kostenmodell, Zustand, Gültigkeit | Tenant |

### 2.2 Katalogebenen

| Ebene | Pflege | Sichtbar für |
|---|---|---|
| Plattformkatalog | Operator: eigene Trainer und vertraglich angebundene Anbieter | alle Tenants mit M9, gefiltert nach Region und Sprache |
| Partnerkatalog | Partner | Tenants des Partners |
| Interne Anbieter und Veranstaltungen | Tenant | eigener Tenant, ohne Kosten über die Plattform |

Der Plattformkatalog zeigt je Angebot Verfügbarkeit nach Region, Vorlaufzeit und Preis aus dem Preisplan des Tenants. Angebote ohne verfügbaren Anbieter in der Region des Tenants werden nicht angezeigt; es gibt keine „auf Anfrage“-Platzhalter.

### 2.3 Formate

- **Präsenz:** Ort aus den Standorten des Tenants oder freie Adresse; Raum- und Ausstattungsliste aus dem Angebot als Checkliste für den Organisator.
- **Online:** Der Anbieter liefert den Zugangslink; die Plattform zeigt ihn nur Angemeldeten ab 60 Minuten vor Beginn. Kein Streaming, keine Einbettung.
- **Hybrid:** Präsenzkapazität und Online-Kapazität getrennt.
- **Gesundheitstag:** Veranstaltung mit mehreren Stationen; Anmeldung je Station; ein Ticket je Person für den Tag; Kapazität je Station.
- **Interne Veranstaltung:** ohne Anbieter aus dem Katalog, ohne Kosten; angelegt vom Programm-Manager tenantweit oder vom Botschafter für seine Gruppen (etwa Lauftreff, Vortrag der Betriebsärztin). Mitglieder schlagen Veranstaltungen einem Botschafter vor.

### 2.4 Kein Anbieter-Login

Anbieter erhalten Buchungsanfragen, Bestätigungen, Teilnehmerzahlen, Kalendereinträge und Änderungen per E-Mail aus der Plattform; sie antworten per E-Mail oder Telefon an den Operator beziehungsweise Organisator, der den Zustand in der Plattform pflegt. Check-in und Abschlussmeldung erledigt der Organisator. Die Abrechnung zwischen Betreiber und Anbieter ist ein Geschäftsprozess des Betreibers außerhalb der Plattform; die Plattform liefert dem Operator monatlich eine Anbieter-Übersicht mit durchgeführten Terminen und vereinbarten Beträgen.

## 3. Buchung durch den Tenant (A-083)

### 3.1 Ablauf

1. **Auswahl:** Programm-Manager oder Tenant-Admin wählt ein Angebot, Geltungsbereich, Wunschtermine (bis zu drei), Ort, Kapazität. Die Kostenvorschau zeigt Preis, Stornobedingungen und Auswirkung auf den Monat (A-073).
2. **Anfrage:** Die Plattform sendet die Anfrage an den Anbieter und den Operator. Zustand „angefragt“. Für interne Veranstaltungen entfällt dieser Schritt.
3. **Terminbestätigung:** Der Operator oder Partner trägt den bestätigten Termin ein. Zustand „bestätigt, nicht gebucht“; die Bestätigung hält den Termin sieben Tage.
4. **Buchung:** Der Tenant-Admin bucht verbindlich. Sensible Aktion mit frischer Anmeldung (A-015), protokolliert. Zustand „gebucht“. Metering erhält `event.booking` mit der Anzahl gebuchter Plätze.
5. **Veröffentlichung:** Der Organisator veröffentlicht die Veranstaltung für den Geltungsbereich; Anmeldungen beginnen; Feed-Karte und Benachrichtigung der Kategorie Veranstaltungen.
6. **Durchführung:** Check-in per Ticket-Scan.
7. **Abschluss:** Der Organisator meldet die Durchführung innerhalb von sieben Tagen; sonst erinnert die Plattform, nach 14 Tagen gilt der Termin als durchgeführt. Metering erhält `event.delivered`.

Der Organisator ist der Programm-Manager oder eine von ihm je Veranstaltung benannte Person mit Rolle Botschafter oder Redakteur; für Gruppenveranstaltungen der Botschafter selbst.

### 3.2 Storno und Mindestteilnehmerzahl

| Fall | Regel |
|---|---|
| Storno durch den Tenant bis 14 Tage vor Termin | kostenfrei |
| Storno danach | Stornosatz aus dem Angebot, Voreinstellung 50 %, innerhalb von 48 Stunden 100 %; als `event.cancellation` mit Betrag im Metering |
| Mindestteilnehmerzahl zur Anmeldefrist nicht erreicht | Der Organisator entscheidet: durchführen (voller Preis) oder absagen (kostenfrei, auch innerhalb der Stornofrist); die Plattform fragt ihn zur Anmeldefrist |
| Absage durch den Anbieter | kostenfrei; Angemeldete werden benachrichtigt; Ersatztermin als neue Anfrage |
| Terminverschiebung | wie Absage plus neue Veranstaltung; Anmeldungen werden mit Zustimmung der Personen übernommen |

Stornobedingungen stehen vor der Buchung in der Kostenvorschau und auf der Rechnung.

### 3.3 Sichtbarkeit in der Verwaltung

Tenant-Admin und Programm-Manager sehen Buchungen, Termine, Kosten, Kapazität und Anmeldezahl. Anmeldezahlen unter fünf werden als „unter 5“ angezeigt, außer dem Organisator der Veranstaltung, der die Liste der Anzeigenamen sieht (Abschnitt 4.4). Die Einsichtsrolle sieht Anzahl der Veranstaltungen und Teilnehmerzahlen ab fünf. Partner sehen Buchungen und Beträge ihrer Tenants ohne Anmeldedaten.

## 4. Anmeldung der Mitglieder (A-084)

### 4.1 Kalender und Anmeldung

- Bereich „Firma“ zeigt den Veranstaltungskalender des Geltungsbereichs: kommende Veranstaltungen, eigene Anmeldungen, freigeschaltete Belohnungen.
- Anmeldung mit einem Tap; bei Gesundheitstagen je Station. Kapazität wird beim Anmelden geprüft; ist sie erreicht, folgt die Warteliste in Reihenfolge der Anmeldung. Rückt jemand nach, wird er benachrichtigt und hat 24 Stunden zur Bestätigung.
- Abmeldung bis zur Abmeldefrist (Voreinstellung 24 Stunden vor Beginn) mit einem Tap; danach nur mit Hinweis an den Organisator.
- Erinnerung 24 Stunden und 1 Stunde vor Beginn über Benachrichtigungen; ICS-Export; Online-Link ab 60 Minuten vorher.
- Am Kiosk ist Anmeldung möglich; das Ticket wird der Person in der App angezeigt oder als Kiosk-Kennung plus Ticketnummer zum Notieren.

### 4.2 Ticket und Check-in

- Jede Anmeldung erzeugt ein Ticket mit zufälligem Token als QR-Code in der App.
- Der Organisator scannt mit der App; der Scan markiert die Anmeldung als eingecheckt und zeigt nur „gültig“ oder „ungültig“, keinen Namen.
- Ohne Gerät: Die Person nennt ihre Kiosk-Kennung; der Organisator prüft sie in der Check-in-Ansicht.
- Check-in erzeugt eine Handlung „Veranstaltung besucht“ in Fortschritt (Deckel eins je Tag). Ohne Check-in kann die Person bis 48 Stunden nach der Veranstaltung selbst bestätigen.

### 4.3 Klarname und Zutritt

Braucht ein Betrieb Klarnamen für den Zutritt (Werksausweis, Besucherliste), kann die Person bei der Anmeldung ihren Klarnamen für genau diese Veranstaltung freigeben. Die Freigabe ist optional, wird erklärt und gilt nur für die Teilnehmerliste des Organisators. Ohne Freigabe steht der Anzeigename auf der Liste.

### 4.4 Teilnehmerliste

| Wer | Sieht |
|---|---|
| Organisator der Veranstaltung | Anzeigenamen, freigegebene Klarnamen, Check-in-Status, Anzahl |
| Anbieter | nur Anzahl, per E-Mail vor dem Termin |
| Tenant-Admin, Programm-Manager ohne Organisatorrolle | Anzahl ab fünf |
| Einsichtsrolle | Anzahl ab fünf |
| Mitglieder | nichts über andere; bei Sichtbarkeit „Mein Team“ oder „Ganze Firma“ optional „X Personen aus deinem Team sind dabei“ ab fünf |

Die Liste ist nicht exportierbar. Anmeldedaten mit Personenbezug werden 30 Tage nach der Veranstaltung gelöscht; es bleiben Anzahl, Check-in-Quote und die Handlung in Fortschritt ohne Veranstaltungsbezug.

## 5. Verdiente Belohnung (A-085)

### 5.1 Verknüpfung

Der Programm-Manager verknüpft beim Anlegen einer Challenge (A-038, Block Belohnung) einen **Workshop-Gutschein**: ein Angebot aus dem Katalog oder eine interne Veranstaltung, eine Bedingung (Zielerreichung 100 % oder ein benannter Meilenstein), den Geltungsbereich (Tenant oder Gruppe) und das Kostenmodell:

| Kostenmodell | Bedeutung | Metering |
|---|---|---|
| Vom Tenant zugesagt | Der Tenant zahlt die Einlösung nach seinem Preisplan | `event.booking` und `event.delivered` regulär, `workshop.credit_redeemed` mit Betrag null als Nachweis |
| Im Preisplan enthalten | Eine Gutschriftregel des Preisplans deckt die Einlösung | `event.delivered` und gegenläufige `credit`-Regel; `workshop.credit_redeemed` mit dem gedeckten Betrag |

Welche Modelle wählbar sind, ergibt sich aus dem Preisplan und dem Partnerrahmen; die Kostenvorschau zeigt die Wirkung vor der Verknüpfung.

### 5.2 Freischaltung

- Tritt die Bedingung ein, schaltet Challenges den Gutschein frei: Feed-Ereignis „Unser Team hat einen Gesundheitstag freigespielt“, Benachrichtigung an den Geltungsbereich, Aushang-Vorlage.
- Der Gutschein ist **sechs Monate** gültig. 30 Tage vor Ablauf erinnert die Plattform den Programm-Manager; ein verfallener Gutschein wird protokolliert und im Feed nicht erwähnt.
- Einlösen heißt: Der Programm-Manager startet die Buchung nach Abschnitt 3 mit dem Gutschein als Grundlage; Geltungsbereich und Angebot sind vorbelegt, das Kostenmodell steht fest.

### 5.3 Gleichbehandlung

Die Veranstaltung steht allen Personen des Geltungsbereichs offen, unabhängig von ihrer Challenge-Teilnahme. Bei knapper Kapazität gilt die Warteliste in Reihenfolge; die Plattform schlägt bei Überhang einen zweiten Termin vor. Es gibt keine Auslosung nach Beiträgen, keine Priorisierung und keine individuellen Gutscheine.

## 6. Rückmeldung, Auswertung, Benachrichtigungen und Metering (A-086)

### 6.1 Rückmeldung

Nach der Veranstaltung erhalten Angemeldete mit Check-in eine optionale anonyme Rückmeldung mit drei Skalenfragen (Inhalt, Durchführung, Weiterempfehlung) und einem optionalen Freitext. Skalenwerte werden ab fünf Antworten als Aggregat dem Organisator und dem Operator gezeigt; der Freitext geht ausschließlich an den Operator zur Qualitätssicherung der Anbieter und wird nach 12 Monaten gelöscht. Es gibt keine Bewertung einzelner Trainer in der Mitglieder-App.

### 6.2 Auswertung

| Empfänger | Sieht |
|---|---|
| Organisator | Anmeldungen, Warteliste, Check-in-Quote, Rückmeldung ab fünf |
| Programm-Manager, Tenant-Admin | Veranstaltungen, Buchungen, Kosten, Teilnehmerzahlen ab fünf, Gutscheine mit Zustand |
| Einsichtsrolle | Anzahl Veranstaltungen, Teilnehmerzahlen ab fünf, freigeschaltete und eingelöste Gutscheine |
| Partner | Buchungen und Beträge ihrer Tenants |
| Operator | Anbieterübersicht mit Terminen, Beträgen, Rückmeldungen und Freitexten; Katalogpflege |

Keine Liste, wer nicht teilgenommen hat; keine Verknüpfung von Teilnahme mit Aktivitätswerten.

### 6.3 Benachrichtigungen

Kategorie **Veranstaltungen** (A-060): Veröffentlichung im Geltungsbereich, Anmeldebestätigung mit Ticket, Nachrücken von der Warteliste, Erinnerung 24 Stunden und 1 Stunde vorher, Online-Link, Änderung und Absage, Belohnung freigeschaltet, Gutschein läuft ab (an Programm-Manager), Rückmeldung möglich. Push-Standard ein, E-Mail sofort für Bestätigung, Änderung und Absage. Anbieter erhalten E-Mails ohne Personendaten außer Anzahl und Ansprechpartner des Tenants.

### 6.4 Metering

| Metrik | Einheit | Auslöser |
|---|---|---|
| `event.booking` | gebuchte Plätze | verbindliche Buchung |
| `event.delivered` | durchgeführte Termine | Abschlussmeldung oder automatisch nach 14 Tagen |
| `event.cancellation` | Stornobetrag | Storno innerhalb der Frist |
| `workshop.credit_redeemed` | eingelöste Gutscheine mit Betrag | Buchung aus Gutschein |

Nie: Teilnehmer, Check-ins oder Anmeldungen als Metrik. Provisionen bei externen Anbietern laufen über `revenue_share` im Preisplan (A-072).

## 7. Schnittmengen dieses Themas

| Schnittmenge | Festlegung |
|---|---|
| Workshops ↔ Challenges | Gutschein als Belohnungsobjekt im Block Belohnung; Freischaltung durch Challenge-Ereignis; Geltungsbereich aus der Challenge |
| Workshops ↔ Entitlements | M9 setzt M1 voraus; Kündigung von M1 beendet M9; interne Veranstaltungen ohne Kosten gehören zu M9, einfache Termine ohne Anmeldung zum Firmenkanal |
| Workshops ↔ Metering | Plätze und Termine, nie Teilnehmer; Storno und Gutschein als Ereignisse; Kostenvorschau vor Buchung und Verknüpfung |
| Workshops ↔ Organisation | Geltungsbereich über Gruppen; Standorte als Orte; Organisatorrolle je Veranstaltung; Botschafter für Gruppenveranstaltungen |
| Workshops ↔ Zugang | Kiosk-Anmeldung; Kiosk-Kennung als Ticketersatz; Check-in-Ansicht für Organisatoren |
| Workshops ↔ Fortschritt | Handlung „Veranstaltung besucht“ mit Deckel eins je Tag; kein Messwert |
| Workshops ↔ Feed und Inhalte | Karten für Veröffentlichung und Freischaltung; Aushang-Vorlage; keine Teilnehmernamen im Feed |
| Workshops ↔ Benachrichtigungen | Kategorie Veranstaltungen; Anbieter-E-Mails ohne Personendaten |
| Workshops ↔ Datenschutz | Anmeldedaten 30 Tage nach Veranstaltung gelöscht; Klarname nur mit Freigabe je Anmeldung; Listen nicht exportierbar; Aggregate ab fünf; Freitexte nur an Operator |
| Workshops ↔ Marke | Texte in Tonalität; Aushang im Tokensatz; Anbieter-E-Mails in Plattform- oder Tenant-Absender nach A-032 |

## 8. Nachweise

1. Buchung durch Tenant-Admin nur nach frischer Anmeldung; Kostenvorschau mit Stornobedingungen vorher; Metering erhält gebuchte Plätze, nie Teilnehmer.
2. Anmeldung, Warteliste, Nachrücken mit 24-Stunden-Frist, Abmeldung bis zur Frist; Kapazität nie überschritten.
3. Ticket-Scan zeigt nur gültig oder ungültig; Check-in erzeugt eine Handlung; Selbstbestätigung bis 48 Stunden danach.
4. Teilnehmerliste nur für den Organisator, mit Klarname nur bei Freigabe, nicht exportierbar, nach 30 Tagen gelöscht; Anzahl bleibt.
5. Gutschein wird bei 100 % freigeschaltet, Feed-Ereignis und Benachrichtigung im Geltungsbereich; Nichtteilnehmende können sich anmelden; kein Vorrang; Ablauf nach sechs Monaten mit Erinnerung 30 Tage vorher.
6. Beide Kostenmodelle erzeugen korrekte Metering-Ereignisse und Rechnungspositionen.
7. Mindestteilnehmerzahl nicht erreicht: Organisator wird gefragt; Absage kostenfrei auch innerhalb der Stornofrist; Storno durch Tenant innerhalb 14 Tagen erzeugt Stornobetrag.
8. Online-Link erst 60 Minuten vor Beginn und nur für Angemeldete; keine Einbettung in der App.
9. Rückmeldung anonym; Skalen ab fünf sichtbar; Freitext nur beim Operator.
10. Gesundheitstag mit drei Stationen: Anmeldung je Station, ein Ticket je Person, Kapazität je Station.
11. Kündigung von M1 beendet M9 zum selben Termin; interne Veranstaltung ohne Kosten möglich, solange M9 aktiv ist.
