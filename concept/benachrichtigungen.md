# CompanyHero – Benachrichtigungen

**Stand:** 20.09.2026  
**Status:** Beschlossen gemäß A-058 bis A-063 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung.  
**Domäne:** Benachrichtigungen gemäß [Domänenkarte](domaenen-und-schnittmengen.md).  
**Bezug:** [Feed und Inhalte](feed-und-inhalte.md), [Fortschritt](fortschritt.md), [Challenges](challenges.md), [Betrieb](betrieb.md), [Datenschutz und Nachweis](datenschutz-und-nachweis.md).

Benachrichtigungen bringen Personen zurück zu Feed, Challenges und Inhalten. Sie sind an eine Person gerichtet, im Unterschied zum Feed, der zeigt, was im Tenant passiert. Alles hier ist optional: je Tenant, je Kanal, je Kategorie und je Person, jederzeit ein- und ausschaltbar. Ausgenommen sind Konto- und Sicherheitsnachrichten.

## 1. Grundsätze

- **Ein Ereignis, eine Entscheidung:** Jede Domäne veröffentlicht Fachereignisse; nur diese Domäne entscheidet, ob daraus eine Benachrichtigung wird, für wen, über welchen Kanal und wann.
- **Kollektiv formuliert:** Keine Nachricht nennt einen individuellen Rückstand, eine fehlende Teilnahme oder einen Vergleich zwischen Personen. Der Auslöser darf individuell sein, die Botschaft nie.
- **Kein Inhalt in Push-Nutzlasten:** Push transportiert nur eine Referenz; der Client löst Text und Ziel auf.
- **Wenig statt viel:** Tages- und Wochenkontingente, Ruhezeiten, Zusammenfassungen und Verdichtung sind Teil des Modells, nicht Feinschliff.
- **Sichtbarkeit gilt auch hier:** Eine Benachrichtigung nennt nur Personen, die für den Empfänger sichtbar sind, und erreicht nur Personen, die den Bezug sehen dürfen.
- **Tonalität und Sprache** kommen aus Tenant-Konfiguration und persönlicher Sprache; die verbotenen Begriffe der Marke gelten.

## 2. Kanäle (A-058)

| Kanal | Voraussetzung | Wer schaltet | Bemerkung |
|---|---|---|---|
| **In-App** | Sitzung | immer aktiv | Benachrichtigungszentrum hinter der Glocke in der Kopfzeile; Grundlage aller anderen Kanäle |
| **Web Push** | installierte oder geöffnete PWA, erteilte Berechtigung, Abonnement je Gerät | Tenant ein/aus; Person je Kategorie und je Gerät | Abschnitt 3 |
| **E-Mail** | hinterlegte E-Mail der Person; konfigurierter Transport (A-032) | Tenant ein/aus; Person je Kategorie sofort, täglich, wöchentlich oder aus | Abschnitt 6 |
| **Aushang** | Drucker | Programm-Manager und Botschafter erzeugen | wöchentliches PDF ohne Personenbezug für Belegschaften ohne Gerät |
| **Kalender** | Kalender-App der Person | Person | ICS-Dateien für Erinnerungsfenster und Termine |

Nicht Teil der Domäne: SMS, Chat-Integrationen, native Push-Dienste außerhalb des Web-Standards. Der Kiosk zeigt keine persönlichen Benachrichtigungen.

### 2.1 Tenant-Schalter

Der Tenant-Admin oder Programm-Manager schaltet je Kanal (Push, E-Mail, Aushang) und je Kategorie tenantweit ein oder aus. Ausschalten wirkt sofort und **pausiert**: Abonnements und persönliche Einstellungen bleiben erhalten, es wird nichts gesendet. Wiedereinschalten wirkt sofort ohne neue Browser-Berechtigung. Personen sehen den Tenant-Zustand in ihren Einstellungen („Vom Unternehmen derzeit pausiert“). Änderungen stehen im Prüfprotokoll und sind für die Einsichtsrolle sichtbar.

## 3. Web Push (A-059)

### 3.1 Technik

- Standard-Web-Push: Push-API und Notifications-API im Angular-Service-Worker, Abonnement beim Push-Dienst des Browserherstellers, Authentifizierung über **VAPID**-Schlüsselpaar der Plattform. Kein Fremd-SDK, keine Drittanbieter-Push-Plattform.
- Der VAPID-Schlüssel liegt in OpenBao; Rotation ist ein geplanter Vorgang mit Übergangsphase, in der alte und neue Schlüssel akzeptiert werden und Abonnements beim nächsten App-Start erneuert werden.
- Nutzlast: verschlüsselt nach Web-Push-Standard, Inhalt ausschließlich `{ id, kategorie, ziel }`. Der Service Worker lädt Titel, Text und Bild über die API mit der Sitzung. Ohne gültige Sitzung zeigt er eine neutrale Nachricht mit dem Produktnamen des Tenants und öffnet beim Tippen die Anmeldung.
- Je Nachricht: Lebensdauer (TTL) und Dringlichkeit nach Kategorie, ein Sammelschlüssel (Topic) je Bezug, damit mehrere Meilensteine derselben Challenge zu einer Anzeige zusammenfallen.
- Antworten des Push-Dienstes mit 404 oder 410 löschen das Abonnement; nach fünf aufeinanderfolgenden Fehlern wird es pausiert und beim nächsten App-Start geprüft.

### 3.2 Abonnement

1. Die App erklärt den Nutzen in einem eigenen Schritt mit Button („Benachrichtigungen einschalten“). Der Browser-Prompt erscheint erst nach diesem Tap, nie beim ersten Laden.
2. Auf iOS führt der Schritt zuerst durch „Zum Startbildschirm hinzufügen“, weil Push dort nur in der installierten App verfügbar ist; die App erkennt den Zustand und zeigt den passenden Weg.
3. Nach erteilter Berechtigung abonniert der Service Worker automatisch und meldet Endpunkt und Schlüssel an das Backend; das Backend speichert das Abonnement je Person und Gerät mit Gerätebezeichnung, Zeitpunkt und letztem Erfolg.
4. Bei `pushsubscriptionchange` erneuert der Service Worker das Abonnement ohne Nutzeraktion und meldet es erneut.
5. Bei jedem App-Start prüft die App den Zustand (Berechtigung, Abonnement, Tenant-Schalter) und korrigiert stillschweigend, was korrigierbar ist; widerrufene Berechtigungen werden in den Einstellungen mit Anleitung angezeigt.

Unter „Ich → Benachrichtigungen → Geräte“ sieht die Person ihre Abonnements und kann einzelne Geräte abmelden. Abmeldung aus allen Sitzungen und Austritt löschen alle Abonnements. Der Kiosk abonniert nie.

### 3.3 Anzeige

Titel in Nutzersprache, ein Satz Text, optional Bild aus dem Inhalt, Tenant-Logo als Symbol, Aktionen je Kategorie (etwa „Beitrag erfassen“, „Öffnen“). Tippen öffnet das Ziel in der App. Der Zähler am App-Symbol entspricht den ungelesenen Einträgen im Benachrichtigungszentrum.

## 4. Kategorien und Regelwerk (A-060)

### 4.1 Kategorien

| Kategorie | Auslöser | Empfänger | Push-Standard | E-Mail-Standard | Abschaltbar |
|---|---|---|---|---|---|
| **Erinnerungen** | persönliche Zeitfenster, Wochenplan | die Person | ein | aus | ja |
| **Challenge** | Start, Meilenstein 25/50/75/100 %, letzte 24 Stunden, Ende, Belohnung eingelöst, Raster-Reihe | Teilnehmende bzw. Zielgruppe | ein | wöchentlich | ja |
| **Fortschritt** | Abzeichen, Stufe, Serienschutz verbraucht, Wochenrückblick verfügbar | die Person | ein | aus | ja |
| **Wieder dabei** | 7, 30, 60 Tage ohne Handlung | die Person | ein | täglich zusammengefasst | ja |
| **Ankündigungen** | Tenant-Beitrag mit Markierung „Ankündigung“ | Zielgruppe des Beitrags | ein | sofort | ja |
| **Beiträge und Kommentare** | Kommentar auf eigenen Beitrag, Antwort auf eigenen Kommentar, Anerkennung erhalten | die Person | zusammengefasst | täglich | ja |
| **Inhalte** | neuer Inhalt oder Serienfolge laut Redaktionsplan | Zielgruppe | ein, höchstens eine je Tag | wöchentlich | ja |
| **Buddy** | Anfrage, Bestätigung, wöchentlicher Anstoß | die beiden | ein | aus | ja |
| **Veranstaltungen** (M9) | Veröffentlichung, Anmeldebestätigung mit Ticket, Nachrücken, Erinnerung 24 Stunden und 1 Stunde vorher, Online-Link, Änderung, Absage, Belohnung freigeschaltet, Rückmeldung möglich | Geltungsbereich bzw. Angemeldete | ein | sofort für Bestätigung, Änderung, Absage | ja |
| **Konto und Sicherheit** | Magic-Link, Rollencode, neue Anmeldung auf neuem Gerät, Sitzungen beendet, Anmeldeweg wird deaktiviert, Austritt bestätigt | die Person | aus (nur In-App und E-Mail) | sofort | nein |
| **Verwaltung** | Meldungen in der Moderation, Medienverarbeitung fertig, Zustellfehler, Kostenvorschau-Stufensprung, Sollstärke-Erinnerung, Rechnung verfügbar | Rollen nach Rechtematrix | ein | sofort oder täglich | ja, außer Zustellfehler und Rechnung |
| **Plattform** | Wartungsfenster, Störungen, neue Rechtstextversion | Tenant-Admins bzw. alle | In-App | sofort an Tenant-Admins | nein |

### 4.2 Regeln für alle Kategorien

- **Sichtbarkeit:** Empfängerkreis und genannte Personen laufen durch die zentrale Sichtbarkeitsprüfung. Eine Kommentar-Benachrichtigung nennt den Kommentator nur, wenn er für den Empfänger sichtbar ist; sonst „Jemand aus deinem Team“.
- **Kontingente:** höchstens drei Push je Person und Tag über alle Kategorien außer Erinnerungen und Konto; höchstens drei Ankündigungen je Woche tenantweit und eine je Woche und Gruppe durch Botschafter; höchstens ein Inhalte-Push je Tag. Was das Kontingent überschreitet, bleibt In-App.
- **Ruhezeiten:** tenantweit voreingestellt 20:00 bis 07:00 in der Tenant-Zeitzone, änderbar; Personen können ihre Ruhezeit erweitern, nicht verkürzen. In der Ruhezeit werden Push und sofortige E-Mails zurückgehalten und um 07:00 verdichtet gesendet; Erinnerungen der Person und Konto-Nachrichten sind ausgenommen.
- **Verdichtung:** Mehrere Ereignisse derselben Kategorie und desselben Bezugs innerhalb von 30 Minuten ergeben eine Nachricht. Anerkennungen und Kommentare werden täglich zusammengefasst.
- **Abwesenheit** (A-045) unterdrückt alle Kategorien außer Konto und Sicherheit sowie Plattform.
- **Deduplikation:** Idempotenzschlüssel aus Ereignis, Person und Kanal; ein Ereignis erzeugt je Person und Kanal höchstens eine Zustellung, auch bei Wiederholung von Jobs.
- **Sprache und Tonalität:** Texte aus Textkarten je Sprache in der Tonalitätsstufe des Tenants; Bezeichnungen (Punkte, Stufen, Gruppen) aus der Marke.
- **Verbotene Aussagen:** kein Rückstand, kein Vergleich mit Personen, keine Nennung von Nichtteilnahme, keine Begriffe der Sperrliste der Marke, keine Werbung für nicht freigeschaltete Module.

### 4.3 Wortlaut „Wieder dabei“

Tag 7: „Dein Team ist bei 62 % – schaust du vorbei?“ Tag 30: Ankündigung des Comeback-Abzeichens. Tag 60: eine letzte Nachricht, danach Ruhe bis zur nächsten Handlung. Nie „du bist zurückgefallen“, nie eine andere Person. Ohne aktive Challenge nennt die Nachricht den nächsten Inhalt oder das Tagesziel.

## 5. Zusammenspiel mit Beiträgen und Feed (A-060)

| Situation | In-App | Push | E-Mail | Feed |
|---|---|---|---|---|
| Tenant-Beitrag ohne Markierung | Eintrag für Zielgruppe | nein | nein | Karte |
| Tenant-Beitrag als **Ankündigung** | Eintrag | ja, im Kontingent und in Ruhezeiten zurückgehalten | sofort für Personen mit Einstellung „sofort“ | Karte, optional angeheftet |
| Geplanter Tenant-Beitrag | zum Veröffentlichungszeitpunkt | wie oben | wie oben | zum Zeitpunkt |
| Mitglieder-Beitrag | nur Buddy erhält einen Eintrag, falls Sichtbarkeit es erlaubt | nie an Dritte | nein | Karte im Sichtbarkeitskreis |
| Kommentar auf eigenen Beitrag | sofort | in der täglichen Zusammenfassung | täglich | im Beitrag |
| Antwort auf eigenen Kommentar | sofort | in der täglichen Zusammenfassung | täglich | im Beitrag |
| Anerkennung erhalten | tägliche Zusammenfassung ohne Zahl („Es gab Anerkennung für deinen Beitrag“) | nein | täglich, falls gewählt | Anerkennungsleiste |
| Meldung eines Beitrags | Moderationsrolle sofort | ja | sofort | keine Karte |
| Beitrag ausgeblendet oder entfernt | betroffene Person, neutral | nein | nein | verschwindet |
| Neuer Inhalt laut Redaktionsplan | Eintrag | höchstens einer je Tag | wöchentliche Übersicht | Karte |
| Challenge-Meilenstein | Teilnehmende | ja, per Sammelschlüssel verdichtet | wöchentlich | genau eine Karte |

Ein Tenant-Beitrag lässt sich zusätzlich als **A4-Aushang** exportieren (Abschnitt 6.2), damit dieselbe Ankündigung Personen ohne Gerät erreicht. Autoren sehen vor dem Senden, wie viele Personen die Ankündigung als Push, als E-Mail und nur In-App erhalten würden, als Zahlen ab fünf.

## 6. Persönliche Einstellungen, Erinnerungen, E-Mail, Aushang und Kalender (A-061, A-062)

### 6.1 Persönliche Einstellungen

Unter „Ich → Benachrichtigungen“:

- Je Kategorie: Push ein/aus, E-Mail sofort/täglich/wöchentlich/aus. In-App ist immer aktiv.
- Geräte mit Push-Abonnement, einzeln abmeldbar.
- Persönliche Ruhezeit, nur erweiterbar gegenüber dem Tenant.
- **Erinnerungsfenster:** bis zu vier Zeitfenster je Tag, Wochentagsauswahl, optional Text („Bewegungspause“), Kanal Push oder Kalender; Erinnerungen enthalten nie Zahlen zum eigenen Stand, nur die Einladung zur Handlung.
- Sprache für Benachrichtigungen, falls vom Profil abweichend.
- Alle Einstellungen gelten je Person und Tenant, sind im Export enthalten und werden bei Austritt gelöscht.

Der Onboarding-Schritt setzt Erinnerungen nicht ohne Zustimmung; er bietet ein Zeitfenster als Vorschlag an, das die Person mit einem Tap übernimmt oder überspringt.

### 6.2 Aushang

- Wöchentlicher **Aushang-Zettel** je Tenant oder Gruppe als A4-PDF: Kollektivstand, nächster Meilenstein, Wochenthema, QR-Code zum Beitritt oder zur Challenge, Tenant-Marke aus dem Tokensatz (A-013). Kein Personenbezug, keine Beteiligungsquote unter fünf, keine Ranglisten.
- **Ankündigungs-Aushang** aus einem Tenant-Beitrag: Titel, Text, Bild, QR-Code.
- Erzeugung durch den Worker als Job; Programm-Manager und Botschafter laden herunter oder erhalten ihn wöchentlich per E-Mail. Die Vorlage folgt dem Design-System und ist nicht frei gestaltbar.

### 6.3 E-Mail

- Sofortige Nachrichten für Konto und Sicherheit sowie gewählte Kategorien; tägliche und wöchentliche Zusammenfassungen zu festgelegter Zeit außerhalb der Ruhezeit.
- Jede E-Mail enthält einen Ein-Klick-Abmeldelink je Kategorie und einen Link zu den Einstellungen; Konto-Nachrichten haben keinen Abmeldelink.
- Kein Tracking-Pixel, keine Klickverfolgung; Links führen in die App.
- Absender und Transport gemäß A-032; Betreff und Text ohne Personendaten außer der Anrede mit Anzeigenamen.
- Zustellstatus je Nachricht ohne Inhalt, 30 Tage; drei aufeinanderfolgende Unzustellbarkeiten pausieren E-Mail für die Person mit In-App-Hinweis.

### 6.4 Kalender

Erinnerungsfenster und Termine aus Tenant-Beiträgen sind als ICS-Datei exportierbar; Serien als wiederkehrende Termine. Die Datei enthält Titel, Zeit, Ort und einen Link, keine Personendaten Dritter.

## 7. Zustellpipeline, Status und Auswertung (A-063)

### 7.1 Ablauf

1. Eine Domäne veröffentlicht ein Fachereignis (A-006).
2. Der Benachrichtigungs-Worker wendet das Regelwerk an: Kategorie, Tenant- und Personenschalter, Sichtbarkeitsprüfung des Empfängerkreises, Abwesenheit, Kontingente, Ruhezeiten, Verdichtung, Deduplikation.
3. Ergebnis ist je Empfänger ein **In-App-Eintrag** (immer) und je gewähltem Kanal ein Zustelljob mit Idempotenzschlüssel.
4. Kanaljobs senden; Antworten werden als Zustellstatus gespeichert; Fehler folgen den Kanalregeln (Abschnitt 3.1, 6.3).
5. Klick oder Öffnen markiert den Eintrag als gelesen; Lesen synchronisiert über alle Geräte.

Alle Schritte laufen im Tenant-Kontext; Jobs sind idempotent; Wiederholungen erzeugen keine doppelte Zustellung.

### 7.2 Benachrichtigungszentrum

Liste nach Zeit mit Kategoriefilter, ungelesen hervorgehoben, Aktion je Eintrag, „Alle gelesen“. Einträge bleiben 90 Tage. Offline zeigt das Zentrum die zuletzt geladenen Einträge nicht; es ist online (A-008).

### 7.3 Status und Auswertung

| Empfänger | Sieht |
|---|---|
| Person | eigene Einstellungen, Geräte, ob der Tenant Kanäle pausiert hat |
| Programm-Manager, Tenant-Admin | je Kategorie und Woche: gesendet, zugestellt (E-Mail angenommen bzw. Push vom Dienst akzeptiert), geöffnet; nur ab fünf Personen, ohne Personenbezug; Anteil der Personen mit aktivem Push und mit installierter App als Zahl ab fünf |
| Einsichtsrolle | aktive Kanäle und Kategorien, Tenant-Schalter, Kontingente |
| Operator | Zustellfehlerquoten je Kanal plattformweit für den Betrieb |

Keine Liste, wer Push abgelehnt oder nichts geöffnet hat. Zustellstatus 30 Tage ohne Inhalt; Aggregate 12 Monate.

## 8. Datenschutz und Sicherheit

- Push-Dienste der Browserhersteller erhalten Endpunkt und verschlüsselte Nutzlast ohne Inhalt; sie stehen als Empfänger technisch notwendiger Daten im Auftragsverarbeitungsvertrag.
- Keine Personendaten in Nutzlasten, Betreffs, URLs oder Zustellprotokollen; Endpunkte werden wie Zugangsdaten behandelt und nicht protokolliert.
- Konto- und Sicherheitsnachrichten gehen bevorzugt per E-Mail, sonst In-App; nie per Push mit Inhalt.
- Abmelde- und Einstellungslinks sind signiert und zeitlich begrenzt; sie erlauben nur die Änderung von Benachrichtigungseinstellungen.
- Abonnements, Einstellungen und Einträge werden bei Austritt sofort gelöscht (A-019, A-024).

## 9. Schnittmengen dieses Themas

| Schnittmenge | Festlegung |
|---|---|
| Benachrichtigungen ↔ Feed und Inhalte | Ankündigungsmarkierung, Kommentar- und Anerkennungszusammenfassungen, Moderationsmeldungen, Inhalte-Push aus dem Redaktionsplan, Aushang aus Tenant-Beitrag |
| Benachrichtigungen ↔ Challenges | Start, Meilenstein, Endspurt, Ende, Belohnung, Raster-Reihe; Sammelschlüssel je Challenge |
| Benachrichtigungen ↔ Fortschritt | Abzeichen, Stufe, Serienschutz, Wochenrückblick, Inaktivitätssignale; Abwesenheit als Unterdrückung |
| Benachrichtigungen ↔ Zugang | Konto- und Sicherheitsnachrichten; Löschung bei Abmeldung aus allen Sitzungen und Austritt; Kiosk ohne Abonnement |
| Benachrichtigungen ↔ Organisation | Zielgruppen über Gruppen; Botschafter-Kontingent je Gruppe; Verwaltungsnachrichten nach Rechtematrix |
| Benachrichtigungen ↔ Marke | Tonalität, Bezeichnungen, Sperrbegriffe, Tokensatz für Aushang und E-Mail-Layout |
| Benachrichtigungen ↔ Datenschutz | Sichtbarkeitsprüfung, Aggregate ab fünf, keine Personendaten in Nutzlasten, Prüfprotokoll für Tenant-Schalter |
| Benachrichtigungen ↔ Betrieb | E-Mail-Transport (A-032), VAPID-Schlüssel in OpenBao, Worker-Jobs (A-006), Zustellfehler als Metrik |
| Benachrichtigungen ↔ Metering | gesendete Nachrichten als Metering-Ereignis ohne Personenbezug |

## 10. Nachweise

1. Push-Abonnement entsteht erst nach dem App-eigenen Button; auf iOS erst nach Installation; `pushsubscriptionchange` erneuert ohne Nutzeraktion.
2. Push-Nutzlast enthält nur Referenz; ohne Sitzung neutrale Anzeige mit Produktnamen; 410 vom Push-Dienst löscht das Abonnement.
3. Tenant schaltet Push aus: keine Zustellung, Abonnements bleiben; Einschalten liefert sofort ohne neuen Prompt.
4. Vier Push-würdige Ereignisse an einem Tag: drei Push, das vierte nur In-App; Ankündigungskontingent überschritten: Autor sieht den Hinweis, Beitrag geht In-App und Feed.
5. Ereignis in der Ruhezeit: zurückgehalten und um 07:00 verdichtet; persönliche Erinnerung um 21:00 wird zugestellt, wenn die Person sie so gesetzt hat.
6. Abwesende Person erhält nur Konto- und Plattformnachrichten.
7. Kommentar von einer für den Empfänger unsichtbaren Person: Nachricht ohne Namen.
8. Wiederholter Job erzeugt keine zweite Zustellung (Idempotenzschlüssel).
9. E-Mail ohne Tracking-Pixel; Ein-Klick-Abmeldung je Kategorie wirkt sofort; Konto-Mail ohne Abmeldelink.
10. Aushang enthält Tenant-Marke, Kollektivstand und QR, keine Personendaten, keine Quote unter fünf.
11. Auswertung je Kategorie erst ab fünf Personen; keine Personenliste über API oder Export.
12. Austritt löscht Abonnements, Einstellungen und Einträge.
