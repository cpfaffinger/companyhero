# CompanyHero – Marke und Theme

**Stand:** 21.09.2026  
**Status:** Beschlossen gemäß A-076 bis A-081 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung.  
**Domäne:** Marke und Theme gemäß [Domänenkarte](domaenen-und-schnittmengen.md).  
**Bezug:** [Frontend](architektur-frontend.md), [Integrationsregeln](architektur-integrationsregeln.md), [Organisation und Mandanten](organisation-und-mandanten.md), [Benachrichtigungen](benachrichtigungen.md).

Die Firma ist der Absender. Innerhalb eines Tenants trägt die Oberfläche Name, Logo, Farbe, Anrede und Tonalität des Unternehmens; CompanyHero erscheint nur dort, wo mehrere Firmen sichtbar werden, im Impressum und unter „Über“. Diese Domäne legt fest, was ein Tenant gestalten darf, wie daraus ein Theme entsteht und welche Gestaltungsregeln für alle Tenants gleich bleiben.

## 1. Gestaltungshaltung

1. **Ruhig, nicht sportlich.** Die Zielgruppe reicht von der Büroangestellten mit Wearable bis zum Schichtarbeiter. Das Vorbild ist eine ruhige Produktivitätsanwendung mit warmen Erfolgsmomenten, nicht eine Fitness-App.
2. **Fortschritt ist die Hauptfigur.** Auf jedem Screen ist die auffälligste Fläche ein Fortschritt: kollektiv, wenn möglich, persönlich, wenn nicht.
3. **Dichte über Polsterung.** Über der Falz stehen Informationen, keine Dekoration.
4. **Zwei Farbachsen.** Die Marke trägt Struktur, Navigation und Aktion; eine warme Fortschrittsfarbe trägt Serie, Erfolg und Kollektivziel. Diese Trennung bleibt in jedem Tenant-Theme erhalten.

## 2. Tokenmodell und Design-System (A-076)

### 2.1 Drei Ebenen

`ref.*` Palettenprimitive → `sys.*` semantische Rollen → Komponente. Komponenten verwenden ausschließlich `sys.*`-Rollen als CSS-Variablen mit Präfix `--ch-`. Rohfarben in Komponenten sind ein Lint-Fehler. Ein zentraler Adapter bildet die Rollen auf die öffentlichen Material-Theming-Schnittstellen ab (K01).

### 2.2 Farbrollen

| Rolle | Verwendung | Startwert hell | Startwert dunkel |
|---|---|---|---|
| `primary`, `on-primary` | Marke, primäre Aktion, aktive Navigation | `#2A45C9` / `#FFFFFF` | `#B9C3FF` / `#05157A` |
| `primary-container`, `on-primary-container` | ausgewählte Zustände, ruhige Markenflächen | `#DFE1FF` / `#000F5C` | `#1230AE` / `#DFE1FF` |
| `progress`, `on-progress` | Fortschritt, Serie, Erfolgsmoment | `#E4670A` / `#FFFFFF` | `#FFB77C` / `#4A1F00` |
| `progress-container`, `on-progress-container` | Fortschrittsbahnen, Serien-Chip | `#FFDFC2` / `#331100` | `#6B2E00` / `#FFDFC2` |
| `team`, `team-container`, `on-team-container` | Kollektivziel, Gruppe, Kooperation | `#0F766E` / `#B8F0EA` / `#00201D` | `#7FD8CE` / `#00504A` / `#B8F0EA` |
| `success`, `warning`, `error` | Bestätigung, Hinweis, Fehler; plattformweit fest | `#1F7A3D` / `#9A6300` / `#B3261E` | `#7BDA96` / `#F2C063` / `#FFB4AB` |
| `surface`, `surface-container`, `surface-container-high` | Seitengrund, Karten, erhöhte Karten und Felder | `#FCFCFF` / `#F1F2F9` / `#E9EAF2` | `#121318` / `#1E1F25` / `#292A30` |
| `on-surface`, `on-surface-variant` | Text, Sekundärtext | `#1A1B21` / `#45464F` | `#E3E2E9` / `#C6C6D0` |
| `outline`, `outline-variant` | Ränder, Trenner | `#767780` / `#C6C6D0` | `#90909A` / `#45464F` |
| `chart-1` bis `chart-6` | Diagrammreihen | eigene, für Farbfehlsichtigkeit geprüfte Sequenz | eigene Sequenz |

Startwerte sind Konfiguration des Operators und bilden das Standard-Theme; die Rollen sind verbindlich. Tenant-Themes überschreiben `primary`-, optional Akzent- und abgeleitete `progress`-Rollen; `team`, `success`, `warning`, `error` und Diagrammfarben bleiben plattformweit.

**Farbregeln:** Farbe allein trägt nie Information; höchstens zwei Akzente je Screen (ein Fortschritt, eine Aktion); keine Farbverläufe als Markenmerkmal; Diagrammfarben verwenden nie semantische Rollen.

### 2.3 Typografie

Inter für Text und Bedienelemente, Inter Tight für Überschriften und Kennzahlen, beide variabel und selbst ausgeliefert. Keine Tenant-Schriften.

| Stil | Größe/Zeile | Gewicht | Verwendung |
|---|---|---|---|
| display | 45/52 | 700 | Onboarding, Erfolgsmomente |
| headline-l / headline-m | 32/40, 26/32 | 700 | Seitentitel Desktop und mobil |
| title-l / title-m / title-s | 22/28, 18/24, 16/22 | 600 | Karten- und Listentitel |
| body-l | 16/24 | 400 | Fließtext, Mindestgröße für Inhalte |
| body-m | 14/20 | 400 | Sekundärtext |
| label-l / label-m | 14/20, 12/16 | 600 | Buttons, Tabs, Chips |
| metric-xl / metric-l | 40/44, 28/32 | 700 | Kennzahlen, Tabellenziffern |

Kein Fließtext unter 14 px, kein Text unter 4,5:1 Kontrast, Zahlen mit Tabellenziffern, Zahlenformat der Tenant-Sprache (de-AT: Tausenderpunkt, Dezimalkomma).

### 2.4 Raster, Form, Bewegung

- Abstände auf 4-px-Basis: 4, 8, 12, 16, 20, 24, 32, 40, 48, 64.
- Radien: 8 Eingaben, 12 Karten, 16 große Karten und Sheets, 24 Dialoge, voll für Chips und Buttons.
- Erhöhung über Flächenton, höchstens ein weicher Schatten für schwebende Elemente.
- Bewegung: 120 ms Zustandswechsel, 200 ms Standard, 320 ms betont. `prefers-reduced-motion` schaltet alle nicht informativen Bewegungen ab, einschließlich der Erfolgsanimation.
- **Ein Erfolgsmoment**, überall gleich: Fortschrittsring füllt sich, kurzer warmer Aufleuchtimpuls, Haptik wo verfügbar. Kein Konfetti, keine Pokale, kein Ton.
- Layoutgrenzen 640 px und 1024 px (K07): mobil eine Spalte mit 16 px Rand und unterer Navigationsleiste; Tablet Navigations-Rail; Desktop Seitennavigation, Inhalt bis 1.280 px, Kontextspalte 320 px.

### 2.5 Komponenteninventar

Bewusst klein; jede zusätzliche Komponente ist eine Stelle, an der Tenant-Theming brechen kann.

| Bereich | Komponenten |
|---|---|
| Struktur | kollabierende Kopfzeile mit Glocke, untere Navigationsleiste, Navigations-Rail, Seitennavigation, Kontextspalte, Sheet, Dialog, Snackbar |
| Inhalt | Karte (flach, erhöht, umrandet), Listenzeile, Chip, segmentierte Umschaltung, Suchfeld, Akkordeon, Leerzustand mit Handlungsvorschlag, Ladeplatzhalter |
| Fortschritt | Fortschrittsring, Kollektivbalken mit Meilensteinen, Meilenstein-Route, Serien-Chip, Kennzahlenkachel, Wochenstreifen, Monatsraster, Abzeichenkachel, Ranglistenzeile (rendert unter der Schwelle den Kollektivbalken) |
| Feed | genau fünf Kartentypen (A-049) |
| Eingabe | Textfeld, Zahlenfeld mit Schrittsteuerung, Häkchenkachel, Timer, Datum/Zeitraum, Bild-Upload, Codeeingabe |
| Spezifisch | Körperkarte, Atemanimation, Challenge-Wizard-Schritte, Sichtbarkeitsumschaltung als drei Kacheln, Kostenvorschau-Tabelle, Modul-Vorschaukarte, Kiosk-Anmeldemaske |

Standardbedienelemente kommen aus Angular Material, Produktkomponenten aus dem gemeinsamen Paket; Zuständigkeiten gemäß K02 und K08.

### 2.6 Ikonografie und Bildsprache

Material Symbols Rounded, selbst ausgeliefert, Outline bei 24 px, gefüllt nur für aktive Navigation; keine gemischten Icon-Stile. Bilder zeigen echte Menschen in echten Arbeitsumgebungen, selbst produziert; keine Fitnessmodelle, keine Hochglanz-Sportästhetik, keine generischen Stockfotos. Regel: Wer auf dem Bild nicht plausibel in der Kantine des Kunden sitzen könnte, ist das falsche Bild. Video im Hochformat für mobil und Querformat für Desktop aus derselben Aufnahme.

## 3. Tenant-Theme-Kontrakt und Ableitung (A-077)

### 3.1 Einstellbar und nicht einstellbar

| Einstellbar durch den Tenant | Nicht einstellbar |
|---|---|
| Produktname | Typografie und Schriften |
| Logo hell und dunkel (SVG bereinigt oder PNG ab 512 px) | Abstände, Radien, Komponentenformen |
| Saatfarbe | semantische Farben für Erfolg, Warnung, Fehler; Teamfarbe; Diagrammfarben |
| Akzentfarbe optional, nur mit Mindestabstand zu Saat-, Fortschritts- und Teamfarbe | Navigationsstruktur und -reihenfolge |
| Anrede Du oder Sie | Datenschutzregeln und Schwellen |
| Tonalität sachlich, freundlich, motivierend | Sichtbarkeit fremder Mitgliederdaten |
| Startbild und Willkommenstext | Systemtexte (nur Stufe und Bezeichnungen) |
| Bezeichnungen für Punkte, Serie, Stufen, Gruppendimensionen | Erfolgsmoment und Bewegung |
| Sprachen mit Mehrsprachigkeitsmodul | Icon-Stil |

### 3.2 Theme-Dokument

Ein Tenant-Theme ist ein versioniertes JSON-Dokument mit Schemaversion:

```jsonc
{
  "schema": 1,
  "produktname": "…",
  "logo": { "hell": "<Medienreferenz>", "dunkel": "<Medienreferenz>" },
  "saatfarbe": "#0B5FA5",
  "akzent": null,
  "anrede": "du",
  "tonalitaet": "freundlich",
  "sprachen": ["de"],
  "bezeichnungen": {
    "punkte": "Punkte",
    "serie": "Serie",
    "stufen": ["Neu dabei", "Dabei", "Dranbleiber", "Vorbild", "Urgestein"],
    "dimensionen": { "1": "Standort", "2": "Abteilung", "3": "Schicht" }
  },
  "startbild": "<Medienreferenz>",
  "willkommenstext": "…"
}
```

Bezeichnungen und Willkommenstext sind Textkarten je Sprache. Das Backend validiert Schema, Farbformat, Bildformate und Textlängen; Logos werden bereinigt, Startbilder neu kodiert (A-026).

### 3.3 Ableitung im Backend (A-013)

1. Aus der Saatfarbe entsteht nach dem Material-3-Verfahren im HCT-Farbraum die vollständige Tonpalette für hell und dunkel: `primary`, Container- und On-Farben.
2. `progress` wird aus der Saatfarbe mit erzwungenem Farbtonabstand abgeleitet; bei warmer Saatfarbe ein kontrastierender warmer Ton, nie ein Blau. `team` bleibt plattformweit.
3. Der optionale Akzent wird nur übernommen, wenn er den Mindestabstand einhält; sonst abgeleitet.
4. Alle Rollenpaare werden gegen WCAG 2.2 AA geprüft: 4,5:1 für Text, 3:1 für Bedienelemente und Fortschrittsbahnen. **Die Saatfarbe wird immer angenommen.** Wo sie eine Prüfung nicht besteht, tritt die nächstliegende zulässige Ableitung derselben Farbe an ihre Stelle. Die Marke wird nie abgelehnt, nur dort ersetzt, wo sie unlesbar wäre.
5. Ergebnis ist ein **Tokensatz** je Modus mit allen `sys.*`-Werten, einer Liste der Ersetzungen mit Begründung und der Versionsnummer. Er wird gespeichert und ausgeliefert; die Ableitung ist deterministisch und mit Referenzwerten getestet.
6. Ungültige Eingaben führen zum Standard-Theme; die Verwaltung zeigt den Grund.

### 3.4 Versionierung und Auslieferung

Jede Veröffentlichung erzeugt eine neue Tokensatz-Version. Die App lädt den Tokensatz beim Start und bei Änderung, cached ihn versioniert und setzt die CSS-Variablen auf dem Wurzelelement; der Wechsel geschieht ohne Neuladen. Serverseitig erzeugte Dokumente (Aushang, E-Mail-Layout) verwenden die zum Erzeugungszeitpunkt gültige Version. Frühere Versionen bleiben zwölf Monate abrufbar.

### 3.5 Verwaltung

Der Bereich „Marke und Sprache“ zeigt eine Live-Vorschau in beiden Layouts und beiden Modi mit Referenzscreen (Formular, Challenge-Karte, Dialog), den Kontrastbericht mit Saatfarbe und Ersetzung nebeneinander, die Versionshistorie und „Veröffentlichen“ als sensible Aktion (A-015). Partner-Voreinstellungen sind Startwerte (A-037). Änderungen stehen im Prüfprotokoll.

## 4. Plattformmarke, Standard-Theme und Co-Branding (A-078)

### 4.1 Plattformmarke

Der Operator pflegt die Plattformmarke: Produktname der Plattform, Wortmarke, Bildzeichen, Standard-Theme mit den Startwerten aus 2.2. Die Wortmarke ist zweifarbig in Inter Tight; das Bildzeichen ein aufsteigender Bogen aus drei Segmenten in `primary` und `progress`, lesbar als Fortschrittsbahn und als Gruppe, funktionsfähig einfarbig, bei 16 px und als App-Symbol. Kein Herz, kein Puls, keine Hantel, kein Apfel. Name und Zeichen sind Konfiguration; ein Namenswechsel ist durch das Tokenmodell ohne Codeänderung möglich. Markenrecherche ist Betreibersache.

### 4.2 Geltungsbereiche

| Geltungsbereich | Wann | Erscheinungsbild |
|---|---|---|
| **Tenant** | Start, Challenges, Entdecken, Firma, Ich, Verwaltung, Kiosk | vollständig Tenant: Name, Logo, Farben, Anrede, Tonalität; Plattform nur in Impressum und „Über“ |
| **Plattform** | Arena-Übersicht, Arena-Detail, arenaweite Ranglisten, Arena-Feed-Ereignisse, Operator-Konsole, Anmeldeseite vor Tenant-Zuordnung | neutrale Plattform-Schale im Standard-Theme; das Tenant-Logo erscheint als Teilnehmerkennzeichen gleichrangig neben anderen |

Der Wechsel ist sichtbar gestaltet: Die Arena wird über eine eigene Kopfzeile („Firmenübergreifend“) betreten, Rahmen und Farben wechseln. Beim Verlassen kehrt die Tenant-Marke zurück. Kein dauerhaftes Doppel-Logo. Der Theme-Service verwaltet Markenkontext und Modus gemeinsam; Overlays folgen, kontextgebundene Overlays werden beim Wechsel geschlossen (K05). Der Markenwechsel ändert nie die Mitgliedschaft oder Sitzung.

### 4.3 Hell und Dunkel

`system | light | dark` je Person; beide Modi sind gleichwertig gestaltet und getestet, nicht invertiert. Nur bei `system` wirkt die Betriebssystempräferenz.

## 5. PWA-Manifest je Tenant (A-079)

Die Plattform hat eine Origin (A-032), aber jede Firma soll auf dem Startbildschirm unter ihrem Namen und Symbol erscheinen.

- Das Web-App-Manifest wird **dynamisch je Tenant** unter einem tenant-spezifischen Pfad ausgeliefert. `name` und `short_name` sind der Produktname des Tenants, `theme_color` und `background_color` kommen aus dem Tokensatz, `start_url` und `scope` enthalten den Tenant-Kennzeichner, `id` ist je Tenant eindeutig.
- Icons in allen benötigten Größen einschließlich maskierbarer Varianten werden aus dem Tenant-Logo erzeugt; fehlt ein Logo, aus dem Bildzeichen der Plattform in Tenant-Farbe.
- Der Installationshinweis im Onboarding verweist auf das Tenant-Manifest; nach der Installation zeigt das Startbildschirm-Symbol die Firma.
- Der Service Worker und die Cookie-Sitzung sind davon unberührt; die App-Shell ist für alle Tenants gleich, das Theme wird zur Laufzeit gesetzt.
- Der Kiosk hat ein eigenes Manifest je Gerät mit dem Kiosk-Einstieg als `start_url` und Anzeige im Vollbild.
- Die Anmeldeseite vor Tenant-Zuordnung trägt die Plattformmarke; nach der Zuordnung wechselt die App in den Tenant-Kontext und aktualisiert Theme und Manifest-Verweis.

## 6. Tonalität, Sprache und Textkatalog (A-080)

### 6.1 Tonalitätsstufen

| Stufe | Dieselbe Nachricht |
|---|---|
| sachlich | „Das Team hat 62 % des Ziels erreicht.“ |
| freundlich | „Ihr seid bei 62 % – schön, dass so viele dabei sind.“ |
| motivierend | „62 %! Noch 38 % bis zum Gesundheitstag.“ |

Die Stufe wirkt auf alle generierten Texte: System-Ereignisse, Benachrichtigungen, Leerzustände, Onboarding, Aushang, Ankündigungsvorschläge, Rückblicke.

### 6.2 Textkatalog

- Der Operator pflegt je Textschlüssel drei Tonalitätsvarianten je Sprache mit Platzhaltern für Bezeichnungen, Anrede, Zahlen und Bezüge. Tenants schreiben keine Systemtexte; sie wählen Stufe und Anrede und ersetzen Bezeichnungen.
- Anrede Du oder Sie ist in jeder Variante ausgearbeitet, keine automatische Umformung.
- Fehlt ein Text in der Sprache der Person, gilt die Standardsprache des Tenants, sonst Deutsch; die Lücke erscheint in der Operator-Prüfliste.
- Der Katalog ist versioniert; Änderungen wirken sofort auf neue Texte, nie rückwirkend auf gespeicherte Feed-Einträge.

### 6.3 Sprachregeln für alle Stufen

- Nie Schuld erzeugen, nie mit Einzelpersonen vergleichen, nie „du bist zurückgefallen“, nie Nichtteilnahme benennen.
- Keine Ausrufezeichen in „sachlich“; höchstens eines je Nachricht in „motivierend“.
- In keiner Stufe die Wörter Gesundheitsdaten, Tracking, Monitoring, Auswertung, Überwachung, Krankenstand, Fehlzeiten, Leistung, Ranking als Oberflächenbegriff. Die Sperrliste pflegt der Operator; sie ist Prüfkriterium im Textkatalog und in der Feed-Moderation (A-051).
- Nutzersprache: kurze Sätze, keine Fachbegriffe in Titeln, Fachbegriff nur im Detail.
- Zahlen mit Tabellenziffern und Landesformat; Prozentwerte ohne Nachkommastellen in Nachrichten.

## 7. Barrierefreiheit und Großflächenmodus (A-081)

Ziel ist WCAG 2.2 AA vollständig, weil die Zielgruppe alle Beschäftigten umfasst.

- Kontrast 4,5:1 für Text, 3:1 für Bedienelemente und Fortschrittsbahnen, in beiden Modi und in jedem Tenant-Theme durch die Ableitung sichergestellt.
- Bedienflächen mindestens 48 × 48 px mit 8 px Abstand; Material-Dichte darf sie nicht verkleinern (K07).
- **Großflächenmodus** als persönliche Einstellung, im Onboarding erreichbar: Bedienflächen 56 px, Textgröße zwei Stufen größer, verstärkte Kontraste, reduzierte Dichte, größere Häkchenkacheln. Für Produktionsumgebungen mit Handschuhen, schlechtem Licht und Bildschirm auf Armlänge sowie für ältere Nutzer. Der Kiosk läuft immer im Großflächenmodus.
- Vollständige Tastaturbedienung, sichtbarer Fokusring in `primary` mit 2 px Abstand, logische Reihenfolge, Sprungmarken.
- Screenreader: jede Fortschrittsanzeige mit Textentsprechung („62 Prozent des Teamziels erreicht“), Live-Regionen für Aktualisierungen, Textalternativen für Bilder, Untertitel und Transkripte für Medien (A-054).
- Reduzierte Bewegung wird respektiert; Farbe trägt nie allein Information.
- Diese Prüfpunkte sind Teil des Referenzscreens (Integrationsregeln, Abschnitt 6) und der visuellen Tests; ein Tenant-Theme kann sie nicht unterlaufen.

## 8. Schnittmengen dieses Themas

| Schnittmenge | Festlegung |
|---|---|
| Marke ↔ Frontend | Tokensatz → `--ch-*` → Material-Adapter; Theme-Service für Kontext und Modus; kein Ableitungscode im Frontend (A-013, K01 bis K09) |
| Marke ↔ Organisation | Produktname, Sprache und Zeitzone aus Stammdaten; Partner-Voreinstellungen als Startwerte; Bezeichnungen der Gruppendimensionen (A-034) |
| Marke ↔ Benachrichtigungen | Tonalität, Anrede, Bezeichnungen und Sperrliste für alle Texte; Tokensatz für Aushang und E-Mail-Layout; Tenant-Logo als Push-Symbol |
| Marke ↔ Feed und Inhalte | Tonalitätsvorschläge für Ankündigungen; Sperrliste in der Moderation; Arena-Karten in Plattform-Schale |
| Marke ↔ Fortschritt | Bezeichnungen für Punkte, Serie und Stufen; Erfolgsmoment als einzige Erfolgsanimation |
| Marke ↔ Zugang | Anmeldeseite vor Tenant-Zuordnung in Plattformmarke; Beitritts-Vorschau mit Name und Logo; Kiosk-Manifest |
| Marke ↔ Datenschutz | Rechtstexte unter „Über“ in gültiger Version (A-020); Theme-Änderungen im Prüfprotokoll |
| Marke ↔ Arena | Plattform-Schale mit Tenant-Logos als Teilnehmerkennzeichen |
| Marke ↔ Betrieb | Schriften, Icons und Logos selbst ausgeliefert; Manifest je Tenant über dieselbe Origin |

## 9. Nachweise

1. Zwei Firmenmarken und die Plattformmarke aus dem Backend-Tokensatz auf dem Referenzscreen: identische Farbwerte in App, Vorschau und Aushang; Ersetzungen im Kontrastbericht nachvollziehbar.
2. Orange Saatfarbe: Fortschrittsfarbe mit Farbtonabstand, kein Blau; alle Rollenpaare bestehen 4,5:1 beziehungsweise 3:1 in beiden Modi.
3. Ungültiges Theme führt zum Standard-Theme mit Grund in der Verwaltung.
4. Installierte PWA zeigt Produktname und Symbol des Tenants; zweiter Tenant auf demselben Gerät zeigt seinen eigenen Namen; Kiosk-Manifest startet im Vollbild.
5. Wechsel Firma ↔ Arena: Kopfzeile, Farben und Logo-Rolle wechseln, offene kontextgebundene Overlays schließen, Sitzung unverändert.
6. Tonalitätswechsel ändert Systemtexte, Benachrichtigungen und Aushang sofort, gespeicherte Feed-Einträge nicht; Sperrbegriff im Textkatalog wird abgelehnt.
7. Großflächenmodus: gemessene Bedienflächen mindestens 56 px, Text zwei Stufen größer; Kiosk immer in diesem Modus.
8. Rohfarbe in einer Komponente scheitert am Lint; Tenant-Schrift-Upload existiert nicht.
9. SVG-Logo mit Skript wird bereinigt ausgeliefert; PNG unter 512 px wird abgelehnt.
