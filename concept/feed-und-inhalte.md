# CompanyHero – Feed und Inhalte

**Stand:** 20.09.2026  
**Status:** Beschlossen gemäß A-049 bis A-057 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung.  
**Domäne:** Feed und Inhalte gemäß [Domänenkarte](domaenen-und-schnittmengen.md).  
**Bezug:** [Fortschritt](fortschritt.md), [Challenges](challenges.md), [Datenschutz und Nachweis](datenschutz-und-nachweis.md), [Organisation und Mandanten](organisation-und-mandanten.md).

Der Feed ist die Oberfläche, auf der jedes andere Modul sichtbar wird; die Inhaltsbibliothek ist der Ort, an dem der Tenant sein Programm mit Substanz füllt. Beides ist Kern der Kundeninteraktion und entsprechend vollständig festgelegt.

---

# Teil A – Feed

## 1. Grundsätze

- Der Feed ist ein **Ereignis-Stream** aus vier Quellen, kein Beitrags-Stream. Er ist nie leer, weil Systemereignisse auch ohne Nutzeraktivität entstehen.
- **Fünf Kartentypen**, mehr nicht: System-Ereignis, Challenge-Stand, Inhalt, Tenant-Beitrag, Mitglieder-Beitrag. Jeder Typ hat ein festes Layout.
- **Anerkennung statt Likes:** Reaktionen werden nicht gezählt angezeigt.
- **Keine Interaktionsgewichtung:** Reihenfolge ist chronologisch mit Kopfkarte und Anheftung, nie nach Beliebtheit.
- **Sichtbarkeit** folgt der zentralen Leseregel (A-022): Ein Beitrag ist nur für den Kreis sichtbar, den Autor und Sichtbarkeitsstufe erlauben.
- **Keine Werbung, keine Schlösser:** Nicht freigeschaltete Module erzeugen keine Karten und keine Hinweise.

## 2. Feed-Modell (A-049)

### 2.1 Quellen und Priorität

| Priorität | Quelle | Beispiele | Wer erzeugt |
|---|---|---|---|
| 1 | System-Ereignisse | Challenge gestartet, Meilenstein erreicht, Gruppe bei 50 %, Abzeichen verdient, Stufe erreicht, neuer Inhalt, Arena-Stand, Wochenzusammenfassung, Belohnung eingelöst | Domänen über Fachereignisse |
| 2 | Kuratierte Inhalte | Video, Artikel, Rezept, Kurzfakt, Quiz aus freigeschalteten Modulen | Redaktionsplan des Tenants oder des Operators |
| 3 | Tenant-Beiträge | Ankündigungen, Firmenkanal, Botschafter-Beiträge | Programm-Manager, Redakteur, Botschafter |
| 4 | Mitglieder-Beiträge | Text, Bilder, optional verknüpfte eigene Handlung | Mitglieder, opt-in |

### 2.2 Kartentypen

| Karte | Inhalt | Interaktion |
|---|---|---|
| System-Ereignis | Symbol, ein Satz, Bezug (Challenge, Gruppe, Person nach Sichtbarkeit), Zeit | Anerkennung, Öffnen des Bezugs |
| Challenge-Stand | Titel, Kollektivbalken oder Meilenstein-Route, Restzeit, eigener Beitrag, Aktion „Beitrag erfassen“ | Öffnen, Erfassen |
| Inhalt | Titelbild, Nutzertitel, Kategorie, Dauer, Aktion „Starten“ | Öffnen, Favorit, Anerkennung |
| Tenant-Beitrag | Absender mit Rolle, Text, bis zu 4 Bilder oder 1 Dokument, optional Termin oder Link | Anerkennung, Kommentare (falls aktiv), Teilen in Gruppe |
| Mitglieder-Beitrag | Anzeigename, Text, bis zu 4 Bilder, optional verknüpfte eigene Handlung | Anerkennung, Kommentare (falls aktiv), Melden |

### 2.3 Aufbau der Startseite

1. **Kopfkarte**: aktuelle Challenge des Tenants oder, ohne Challenge, das persönliche Tagesziel. Immer vorhanden.
2. **Check-in-Karte**, solange der heutige Check-in fehlt.
3. **Angeheftete Tenant-Beiträge**: höchstens zwei, höchstens sieben Tage, gesetzt vom Programm-Manager.
4. **Stream**: chronologisch absteigend, unendliches Scrollen, Tagesüberschriften.
5. **Wochenrückblick** sonntags als private Karte im Stream.

### 2.4 Zielgruppen und Reichweite

- Jeder Eintrag hat einen **Geltungsbereich**: ganzer Tenant, eine oder mehrere Gruppen, Arena. Eine Person sieht Einträge ihres Tenants und ihrer Gruppen; Arena-Einträge nur in der Arena-Schale mit Plattform-Marke.
- Tenant-Beiträge können auf Gruppen gezielt werden (etwa nur Standort Wien). Botschafter erreichen nur ihre Gruppen.
- Mitglieder-Beiträge erreichen höchstens den Kreis der eigenen Sichtbarkeitsstufe: „Mein Team“ die eigenen Gruppen, „Ganze Firma“ den Tenant. Mit „Nur für mich“ ist kein Beitrag möglich; die Oberfläche erklärt das und bietet den Wechsel der Stufe mit einem Tap.

### 2.5 Verdichtung und Lärmschutz

- Gleichartige System-Ereignisse eines Tages werden zu einer Karte verdichtet („Heute haben 12 Personen ein Abzeichen verdient“). Personenbezogene Sammelkarten erscheinen erst ab fünf Personen (A-023); darunter nur, wenn die einzelnen Personen nach Sichtbarkeit ohnehin sichtbar wären.
- Höchstens acht System-Karten je Tag und Tenant; überzählige werden in eine Tageszusammenfassung gefaltet.
- Kuratierte Inhalte erscheinen höchstens zweimal je Tag im Feed, terminiert über den Redaktionsplan.
- Kein System-Ereignis benennt je einen individuellen Rückstand, eine fehlende Teilnahme oder einen Vergleich zwischen Personen.

### 2.6 Aktualisierung, Verlauf und Aufbewahrung

- Die App fragt den Feed im Vordergrund alle 60 Sekunden mit ETag ab und zeigt bei neuen Einträgen einen Hinweis; manuelles Ziehen aktualisiert sofort.
- Offline zeigt die Startseite Kopfkarte mit letztem Kollektivstand und Altersangabe, Check-in und eigene wartende Beiträge; der Stream ist online.
- Feed-Einträge bleiben 12 Monate im Stream, danach archiviert. Eigene Beiträge bleiben im Profil der Person bis zur Löschung oder zum Austritt.
- Ein Eintrag verschwindet aus dem Feed, wenn sein Bezug gelöscht wird oder die Sichtbarkeit der Person es verlangt; Änderungen der Sichtbarkeitsstufe wirken rückwirkend (A-022).

## 3. Beiträge, Kommentare und Anerkennung (A-050)

### 3.1 Tenant-Beiträge

- Autoren: Programm-Manager (tenantweit), Redakteur (Firmenkanal, tenantweit), Botschafter (eigene Gruppen). Absender ist bei Programm-Manager und Redakteur Klarname und Rolle, bei Botschaftern Anzeigename und Rollenkennzeichen, oder „Team Gesundheit“ als wählbarer Rollenabsender.
- Inhalt: Text bis 2.000 Zeichen mit einfacher Formatierung (Absätze, Fett, Listen, Links), bis zu 4 Bilder oder ein Dokument, optional ein Termin (Titel, Zeit, Ort, ICS-Export) oder ein Link auf Challenge oder Inhalt.
- Zeitsteuerung: sofort oder geplant; Ablaufdatum optional. Anheftung wie 2.3.
- Tonalität: Textvorschläge für Ankündigungen entstehen aus der Tonalitätsstufe des Tenants (Marke); der Autor bearbeitet sie frei.
- Bearbeiten und Löschen jederzeit durch den Autor und den Programm-Manager; Bearbeitung ist als „bearbeitet“ markiert.

### 3.2 Mitglieder-Beiträge

- Der Tenant schaltet Mitglieder-Beiträge tenantweit ein oder aus; Voreinstellung ein. Jedes Mitglied entscheidet selbst, ob es postet.
- Inhalt: Text bis 1.000 Zeichen, bis zu 4 Bilder, optional eine verknüpfte eigene Handlung (Challenge-Beitrag, Abzeichen, abgeschlossener Inhalt). Ob ein Messwert der Handlung mit angezeigt wird, entscheidet die Person je Beitrag.
- Reichweite gemäß 2.4. Bilder werden neu kodiert, Metadaten entfernt, virengeprüft (A-026).
- Bearbeiten und Löschen jederzeit durch den Autor; Löschen entfernt auch Kommentare und Anerkennungen.
- Ratenbegrenzung: höchstens 5 Beiträge je Person und Tag.

### 3.3 Kommentare

- Kommentare sind auf Tenant- und Mitglieder-Beiträgen möglich, eine Ebene tief, bis 500 Zeichen, ohne Bilder.
- Der Programm-Manager schaltet Kommentare tenantweit ein oder aus (Voreinstellung ein); jeder Autor kann sie je Beitrag abschalten.
- Kommentieren kann nur, wer den Beitrag sieht und selbst mindestens „Mein Team“ gesetzt hat. Ein Kommentar ist im Schnitt aus der Reichweite des Beitrags und der Sichtbarkeitsstufe des Kommentators sichtbar: Mit „Mein Team“ sehen ihn nur die eigenen Gruppen, mit „Ganze Firma“ der ganze Kreis des Beitrags. Außerhalb dieses Schnitts erscheint der Kommentar nicht. Der Kommentar zeigt den Anzeigenamen.
- Autor löscht eigene Kommentare; Beitragsautor und Moderation können Kommentare ausblenden.
- Auf Inhaltsobjekten gibt es keine Kommentare; Diskussion findet auf der Feed-Karte statt.

### 3.4 Anerkennung

- Eine Reaktion je Person und Ziel (Beitrag, Kommentar, System-Ereignis), umschaltbar.
- Sichtbar ist, **dass** Anerkennung vorhanden ist, und **von wem**, soweit deren Sichtbarkeit es erlaubt; nie eine Zahl.
- Anerkennung geben ist eine Handlung für Fortschritt (Deckel 3 je Tag); Anerkennung erhalten erzeugt keine Punkte und keine Benachrichtigung je Reaktion, nur eine tägliche Zusammenfassung an den Autor.
- Ziele einer Anerkennung erfahren nicht, wer **nicht** reagiert hat; es gibt keine Listen.

## 4. Moderation und Meldungen (A-051)

| Baustein | Festlegung |
|---|---|
| Melden | Jede Person kann Beiträge und Kommentare melden, mit Grund aus einer Liste und optionalem Text; die gemeldete Person erfährt nicht, wer gemeldet hat |
| Moderationsrollen | Programm-Manager tenantweit; Botschafter für Beiträge und Kommentare in eigenen Gruppen; Redakteur im Firmenkanal |
| Warteschlange | Meldungen erscheinen in der Verwaltung mit Beitrag, Grund und Anzahl der Meldungen; Entscheidung: belassen, ausblenden, entfernen |
| Automatische Zurückhaltung | Beiträge mit Treffern der Sperrliste werden bis zur Prüfung zurückgehalten; drei Meldungen blenden einen Beitrag bis zur Prüfung automatisch aus |
| Sperrliste | plattformweite Grundliste des Operators plus tenant-eigene Ergänzungen; keine Freigabe von Begriffen der Grundliste durch Tenants |
| Folgen | Ausblenden ist reversibel; Entfernen löscht; die betroffene Person erhält einen neutralen Hinweis ohne Nennung des Melders; wiederholte Entfernungen (drei in 30 Tagen) sperren das Posten für 30 Tage, nicht die Mitgliedschaft |
| Protokoll | jede Moderationsentscheidung im Prüfprotokoll mit Klarname der Funktionsrolle beziehungsweise Anzeigename und Rolle des Botschafters; Meldungen ohne Personenbezug des Melders |
| Kein Eingriff in Sichtbarkeit | Moderation ändert nie die Sichtbarkeitsstufe einer Person |

---

# Teil B – Inhalte

## 5. Grundsätze

- Inhalte sind **kuratiert**: keine Duplikate, keine Datenbank-Dumps, keine falschen Dauern. Jeder Inhalt hat einen Verantwortlichen.
- **Nutzersprache** in Titeln; Fachbezeichnung nur im Detail.
- **Eine Bibliothek**, nicht ein Hub je Modul: Der Bereich „Entdecken“ zeigt alle freigeschalteten Kategorien mit Filtern. Bei nur einer Kategorie heißt der Bereich nach ihr.
- **Selbst ausgeliefert:** Videos, Bilder, Audio und Dokumente liegen im eigenen Objektspeicher. Keine Einbettung externer Plattformen, keine Live-Streams.
- **Keine Körperbild-Themen:** keine Kalorienzählung, keine Gewichtsziele, kein Ernährungs-Score.

## 6. Inhaltsmodell (A-052)

### 6.1 Typen

| Typ | Struktur | Abschluss als Handlung |
|---|---|---|
| Video | Datei, Dauer, Poster, Untertitel, Kapitelmarken, Hoch- und Querformat | „Erledigt“ oder 90 % gesehen |
| Übung | Video oder Bildfolge, Nutzertitel, Fachbezeichnung, Körperregionen, Situation (am Platz, unauffällig, unterwegs, mit Band, im Stehen), Dauer, Intensität, Hilfsmittel, Abschnitte (Ausgangsposition, Ablauf, Wiederholungen, Wirkung), Kontraindikationen als Hinweis | „Erledigt“ |
| Artikel | Rich Text, Bilder, Autor, Lesezeit, Quellen | Ende erreicht |
| Kurzfakt | ein Absatz, optional Bild, Quelle | gelesen |
| Rezept | Zutaten mit Mengen, Schritte, Zeit, Portionen, Hinweise (kantinentauglich, vegetarisch), Bild oder Video | „Gekocht“ |
| Audio | Datei, Dauer, Typ (Atemübung, Meditation, Klang), Transkript | Ende erreicht |
| Quiz | Fragen mit Einfach- oder Mehrfachauswahl, Erklärung je Frage, Bestehensschwelle 70 % | bestanden |
| Dokument | PDF oder Bild, Kategorie, Gültigkeit | geöffnet (keine Handlung) |
| Sammlung | geordnete Liste von Inhalten, optional als Serie mit Tagesrhythmus | alle Elemente abgeschlossen |

Jeder Inhalt trägt: Kategorie (Bewegung, Ergonomie, Wissen, Regeneration, Ernährung, Firma), Tags, Sprache, Dauer, Zielgruppenhinweise, Verantwortlichen, Lizenz und Rechteinhaber, Version und Zustand.

### 6.2 Ebenen und Eigentum

| Ebene | Pflege | Sichtbar für |
|---|---|---|
| Plattforminhalte | Operator-Admins in der Operator-Konsole | alle Tenants mit freigeschalteter Kategorie |
| Partnerinhalte | Partner-Admins in der Partner-Konsole | Tenants des Partners |
| Tenantinhalte | Redakteur, Programm-Manager | eigener Tenant |

Ein Tenant schaltet Plattform- und Partnersammlungen frei oder blendet einzelne Inhalte aus. Tenantinhalte in einer Kategorie setzen die Freischaltung dieser Kategorie voraus; Dokumente und Firmenbeiträge setzen den Firmenkanal voraus. Zuordnung der Kategorien zu Modulen und Regeln der Freischaltung stehen in [Entitlements](entitlements.md).

### 6.3 Sprache

Texte je Sprache mit Pflicht in der Sprache des Inhaltsverantwortlichen; Untertitel und Transkripte je Sprache. Fehlt eine Sprache, wird die Standardsprache des Tenants, sonst die Ursprungssprache gezeigt, mit Kennzeichnung.

## 7. Redaktion (A-053)

### 7.1 Lebenszyklus

| Zustand | Bedeutung |
|---|---|
| Entwurf | nur für Redaktion sichtbar |
| Prüfung | optionale Vier-Augen-Freigabe; der Tenant-Admin stellt ein, ob Prüfung Pflicht ist |
| Geplant | Veröffentlichungszeitpunkt gesetzt |
| Veröffentlicht | sichtbar in der Bibliothek; Feed-Karte gemäß Redaktionsplan |
| Zurückgezogen | nicht mehr sichtbar; Fortschritt und Favoriten der Personen bleiben |
| Archiviert | nach 24 Monaten ohne Aufruf oder manuell |

Jede Veröffentlichung erzeugt eine Version; frühere Versionen bleiben für Nachweis und Rückkehr erhalten. Ein Ablaufdatum zieht automatisch zurück.

### 7.2 Redaktionsplan

- Kalenderansicht je Tenant mit geplanten Veröffentlichungen, Feed-Karten, Challenge-Terminen und Serienformaten.
- **Serienformate:** tägliche Kurzfakten, wöchentliche Übung, Rezept der Woche; eine Serie ist eine Sammlung mit Rhythmus, die automatisch Feed-Karten erzeugt.
- **Vorschläge:** Der Plan schlägt zu laufenden Challenges passende Inhalte vor (Metrik, Kategorie, Tags) und warnt bei leeren Wochen.
- Plattform-Serien des Operators kann ein Tenant abonnieren; sie erscheinen dann in seinem Plan und Feed.
- Feed-Karten je Tag sind auf zwei kuratierte Inhalte begrenzt (Abschnitt 2.5).

### 7.3 Redaktionelle Regeln

- Nutzertitel Pflicht; Fachbezeichnung optional im Detail.
- Dauerangaben werden aus der Mediendatei übernommen, nicht von Hand gepflegt.
- Duplikatprüfung bei Anlage über Titel und Mediensignatur.
- Lizenz und Rechteinhaber sind Pflichtfelder; abgelaufene Lizenzen ziehen den Inhalt automatisch zurück.
- Bildsprache: echte Menschen in echten Arbeitsumgebungen; keine Stockfotos mit Fitnessästhetik. Die Regel steht als Prüfpunkt in der Freigabe.
- Kontraindikationen bei Übungen sind Hinweise, keine medizinische Beratung; ein Standardtext steht in jeder Übung.

## 8. Medienverarbeitung (A-054)

| Schritt | Festlegung |
|---|---|
| Upload | direkt in den Objektspeicher über kurzlebige signierte Upload-URLs; Limits: Video 2 GB, Bild 20 MB, Audio 200 MB, Dokument 50 MB |
| Prüfung | Typprüfung am Inhalt, Virenprüfung, Metadaten-Entfernung; fehlgeschlagene Prüfung verwirft die Datei |
| Video | ffmpeg im Worker: adaptive Stufen (360p, 720p, 1080p) als HLS plus MP4-Fallback, Poster, Kapitelmarken, Hoch- und Querformat aus derselben Quelle, wenn beide geliefert werden |
| Bild | Neukodierung in AVIF und WebP mit JPEG-Fallback in vier Größen |
| Audio | AAC in zwei Bitraten, Wellenform-Vorschau |
| Untertitel | WebVTT je Sprache; Pflicht für Plattformvideos, empfohlen für Tenantvideos; Prüfliste zeigt fehlende Untertitel |
| Auslieferung | signierte, kurzlebige URLs aus dem Objektspeicher; Plattform- und Partnermedien ohne Personenbezug dürfen über ein CDN laufen (A-026); Tenantmedien und Mitgliederbilder nie |
| Offline | Videos nur auf ausdrücklichen Wunsch der Person mit Größenangabe lokal speicherbar; Aufbewahrung 30 Tage ohne Nutzung, Löschung bei Austritt |
| Status | Verarbeitung ist ein Job mit sichtbarem Fortschritt in der Redaktion; ein Inhalt kann erst veröffentlicht werden, wenn alle Ableitungen vorliegen |

### 8.1 KI-Authoring in der Redaktion (A-057)

Redakteure und Programm-Manager können eine KI-Schreibhilfe verwenden. Sie ist optional, je Tenant ein- oder ausschaltbar und läuft ausschließlich mit vom Tenant beigestellten Zugangsdaten (Bring Your Own Key).

**Konfiguration durch den Tenant-Admin**

| Einstellung | Festlegung |
|---|---|
| Schalter | KI-Authoring aus (Voreinstellung) oder ein |
| Anbieterstil | OpenAI-kompatible Chat-Completions-API oder Anthropic-Messages-API |
| Endpunkt | Basis-URL frei konfigurierbar; damit sind auch selbst betriebene oder EU-gehostete Endpunkte desselben Stils anbindbar |
| API-Schlüssel | in OpenBao gespeichert, nie im Klartext in Datenbank, Logs oder Oberfläche |
| Modell | Modellbezeichnung als Text |
| Testaufruf | Pflicht vor Aktivierung; schlägt er fehl, bleibt die Funktion aus |
| Monatliches Limit | optionale Obergrenze an Aufrufen je Tenant; bei Erreichen pausiert die Funktion bis zum Monatswechsel |

Es gibt keine Plattform-Zugangsdaten des Betreibers für diese Funktion. Ohne Tenant-Konfiguration existiert die Funktion in der Oberfläche nicht.

**Funktionen im Editor**

- Entwurf aus Stichpunkten, Umformulierung in der Tonalitätsstufe des Tenants, Kürzen, Zusammenfassen, Titelvorschläge in Nutzersprache, Übersetzung in weitere Sprachen, Textalternativen für Bilder, Untertitelkorrektur.
- Jedes Ergebnis ist ein Vorschlag im Editor. Es wird nie automatisch veröffentlicht, nie automatisch in den Feed gestellt und durchläuft denselben Lebenszyklus wie jeder Inhalt (Abschnitt 7.1).
- KI-erzeugte Textteile sind im Editor bis zur Freigabe markiert; nach außen gibt es keine Kennzeichnung, weil der Redakteur die Verantwortung übernimmt.

**Grenzen**

- Übertragen werden ausschließlich der bearbeitete Inhaltstext, vom Redakteur gewählte Kontexttexte (etwa Challenge-Titel) und die Tonalitätsstufe. Nie: Mitgliederdaten, Mitglieder-Beiträge, Kommentare, Aktivitäts- oder Aggregatdaten, Metering, Anzeigenamen.
- Der Anbieter ist Auftragsverarbeiter des Tenants auf Basis des Tenant-Vertrags; die Verwaltung weist darauf hin und der Tenant bestätigt es bei Aktivierung. Der Betreiber führt ihn nicht als eigenen Unterauftragsverarbeiter.
- Die Plattform speichert weder Prompts noch Antworten außerhalb des Entwurfs. Protokolliert werden nur Anzahl, Zeitpunkt und Funktion je Aufruf ohne Inhalt, für Limit und Prüfprotokoll.
- Aufrufe laufen serverseitig über den Worker mit Zeitlimit und Ratenbegrenzung; der Browser spricht nie direkt mit dem Anbieter.
- A-021 bleibt unberührt: Keine KI verarbeitet Wearable- oder personenbezogene Aktivitätsdaten. Es gibt keinen KI-Coach für Mitglieder.

## 9. Entdecken: Bibliothek und persönliche Funktionen (A-055)

### 9.1 Bibliothek

- Ein Bereich mit Suchfeld, Kategoriechips und Filtern: Dauer, Intensität, Situation, Hilfsmittel, Körperregion, Sprache, Typ.
- **Volltextsuche** in PostgreSQL mit deutscher Wortstammbildung und Ähnlichkeitssuche für Tippfehler; Suchergebnisse zeigen Nutzertitel, Kategorie und Dauer.
- **Schnelleinstieg Bewegung:** Situation wählen, dann Körperregion über Körperkarte oder Chips, dann drei Vorschläge, dann Start. Ohne Fachsprache.
- **Sammlungen** und **Serien** als eigene Kacheln; „Fortsetzen“ zeigt begonnene Serien und Videos.
- Reihenfolge: zuerst Fortsetzen und Favoriten, dann Neues, dann Kategorien. Keine Beliebtheitsranglisten.

### 9.2 Persönliche Funktionen

- **Favoriten**, **Fortsetzen** (Position in Video und Serie), **Erledigt**, **Verlauf**: alles nur für die Person sichtbar, im Export enthalten, bei Austritt gelöscht.
- **Wochenplan** (Kategorie Bewegung): Die Person wählt Wochentage und eine Sammlung; die App erinnert über Benachrichtigungen im gewählten Zeitfenster.
- **Quiz**: Ergebnis nur für die Person; bestanden ab 70 % erzeugt die Handlung „Quiz bestanden“; Wiederholung erlaubt, Handlung einmal je Quiz.
- **Anerkennung** auf Inhalten ohne Zähler; keine Kommentare.
- **Melden** eines Inhalts an die Redaktion (etwa fehlerhafte Angaben).

### 9.3 Barrierefreiheit der Inhalte

Untertitel, Transkripte, Textalternativen für Bilder, Großflächenmodus für Übungsanleitungen, Übungen mit Variante „im Sitzen“ wo möglich. Diese Prüfpunkte stehen in der Freigabe.

## 10. Inhaltsauswertung (A-056)

| Empfänger | Sieht | Regel |
|---|---|---|
| Person | eigenen Verlauf, eigene Abschlüsse | vollständig |
| Redakteur, Programm-Manager | je Inhalt: Aufrufe, Abschlüsse, Favoriten, Anerkennungen als Zahlen; je Kategorie: Nutzung je Woche | nur Aggregate ab fünf Personen; kein Personenbezug; keine Liste, wer etwas nicht genutzt hat |
| Tenant-Admin | Nutzung je Kategorie als Zahl für die Kostenvorschau | Aggregate ab fünf |
| Einsichtsrolle | welche Kategorien aktiv sind | keine Nutzungszahlen |
| Operator | tenantübergreifende Aggregate je Plattforminhalt zur Kuratierung | ohne Tenant-Vergleich nach außen; Tenants werden nicht gegeneinander ausgewiesen |

Abspielminuten und Abschlüsse sind zugleich Metering-Ereignisse ohne Personenbezug (Domäne Metering).

## 11. Schnittmengen dieses Themas

| Schnittmenge | Festlegung |
|---|---|
| Feed ↔ alle Domänen | Domänen veröffentlichen Fachereignisse; Feed entscheidet über Karte, Verdichtung und Geltungsbereich; keine Domäne schreibt Feed-Einträge direkt |
| Feed ↔ Datenschutz | zentrale Sichtbarkeitsprüfung je Karte; Sammelkarten ab fünf; Moderation ohne Melderbezug; keine Rückstands- oder Vergleichsaussagen |
| Feed ↔ Fortschritt | Anerkennung geben, Feed-Beitrag, gelesener Kurzfakt, abgeschlossener Inhalt, bestandenes Quiz sind Handlungen; Abzeichen, Stufen, Check-in erzeugen Karten |
| Feed ↔ Challenges | Kopfkarte, Challenge-Stand, Meilensteine, Start, Ende, Belohnung eingelöst; Raster-Reihe |
| Feed ↔ Benachrichtigungen | Push und E-Mail zu Feed-Ereignissen entscheidet Benachrichtigungen; Kommentar- und Anerkennungszusammenfassungen täglich |
| Feed ↔ Organisation | Geltungsbereich über Gruppen; Botschafter auf eigene Gruppen begrenzt |
| Feed ↔ Marke | Tonalitätsvorschläge für Ankündigungen; Arena-Schale mit Plattform-Marke |
| Inhalte ↔ Entitlements | Kategorien nur bei freigeschaltetem Modul; Tenantinhalte setzen die Kategorie voraus; Dokumente den Firmenkanal |
| Inhalte ↔ Metering | veröffentlichte Inhalte, Abspielminuten, Abschlüsse, Speichervolumen als Ereignisse |
| Inhalte ↔ Betrieb | Objektspeicher Garage, Transcodierung im Worker, CDN nur für Plattform- und Partnermedien |
| Inhalte ↔ Challenges | Plattformereignis-Metriken (Inhalt abgeschlossen, Quiz bestanden) speisen Challenges; Redaktionsplan schlägt passende Inhalte vor |

## 12. Nachweise

1. Neuer Tenant ohne Nutzeraktivität: Feed zeigt Kopfkarte, Check-in und mindestens ein System-Ereignis; kein leerer Zustand.
2. Person mit „Nur für mich“: kein Beitrag möglich, Erklärung mit Wechselangebot; Person mit „Mein Team“: Beitrag nur in eigenen Gruppen sichtbar; Wechsel der Stufe wirkt rückwirkend auf alte Beiträge.
3. Sammelkarte „Abzeichen verdient“ erscheint erst ab fünf Personen; darunter nur individuell sichtbare Ereignisse.
4. Anerkennung zeigt keine Zahl; tägliche Zusammenfassung statt Einzelbenachrichtigung; Handlung mit Deckel 3.
5. Drei Meldungen blenden automatisch aus; Moderationsentscheidung im Protokoll; Melder für Betroffene unbekannt; Sichtbarkeitsstufe unverändert.
6. Kommentare tenantweit abgeschaltet: keine Eingabemöglichkeit; je Beitrag abgeschaltet: nur dort.
7. Upload eines Videos mit Standortmetadaten: Ableitungen ohne Metadaten, Poster, drei Stufen, Untertitelprüfung; Veröffentlichung erst nach Abschluss der Verarbeitung.
8. Abgelaufene Lizenz zieht den Inhalt zurück; Favoriten und Fortschritt der Personen bleiben.
9. Suche „rücken“ findet „Rückenübung“ und „Ruecken“; Ergebnis ohne Fachbegriff im Titel.
10. Redakteur sieht Abschlüsse je Inhalt nur ab fünf Personen; keine Personenliste über API oder Export.
11. Nicht freigeschaltete Kategorie: keine Karten, keine Chips, keine Hinweise.
12. Feed-Abfrage mit unverändertem ETag liefert keine Nutzdaten; neue Einträge erzeugen den Hinweis.
13. KI-Authoring: ohne Tenant-Konfiguration keine Oberfläche; Aktivierung nur nach erfolgreichem Testaufruf; Anfrage enthält nur Inhaltstext und Tonalität; Schlüssel nur in OpenBao; Limit pausiert die Funktion; Vorschlag wird nie ohne Freigabe veröffentlicht.
