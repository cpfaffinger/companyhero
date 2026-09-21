# CompanyHero – Inhaltsmodule M3 bis M7

**Stand:** 21.09.2026  
**Status:** Beschlossen gemäß A-099 bis A-103 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung.  
**Art:** Ausgestaltung der Kategorien der Domäne Feed und Inhalte je Modul.  
**Bezug:** [Feed und Inhalte](feed-und-inhalte.md), [Entitlements](entitlements.md), [Fortschritt](fortschritt.md), [Datenschutz und Nachweis](datenschutz-und-nachweis.md).

Die fünf Inhaltsmodule füllen die Bibliothek „Entdecken“ mit je einer Kategorie. Inhaltsmodell, Redaktion, Medienverarbeitung und Suche gelten für alle gleich (A-052 bis A-056); dieses Thema legt fest, was jedes Modul darüber hinaus mitbringt.

## 1. Gemeinsame Regeln

- Jedes Modul ist eine Kategorie in einer Bibliothek, kein eigener Tab. Bei nur einem Modul heißt der Bereich nach der Kategorie.
- Abschlüsse sind pauschale Handlungen (A-044). Kein Modul erzeugt Messwerte außerhalb einer Challenge.
- Titel in Nutzersprache; Fachbegriff, Kontraindikation und Quelle im Detail.
- Modul-eigene Erinnerungen laufen über die Erinnerungsfenster der Person (A-061), nie über eigene Mechanismen.
- Persönliche Daten eines Moduls sind nur für die Person sichtbar, im Selbstexport enthalten und bei Austritt gelöscht.
- Plattforminhalte liefert die Redaktion des Betreibers; Tenants ergänzen eigene Inhalte in der Kategorie (A-052).

## 2. M3 Bewegung (A-099)

| Element | Festlegung |
|---|---|
| Inhalte | Übungen und Kurzvideos: Mikro-Workouts, Bewegungspausen, Mobilisation, Kräftigung; 2 bis 15 Minuten; Hoch- und Querformat |
| Merkmale je Übung | Körperregionen, Situation (am Platz, unauffällig, unterwegs, mit Band, im Stehen, im Sitzen), Dauer, Intensität (leicht, mittel, fordernd), Hilfsmittel, Variante im Sitzen wo möglich, Kontraindikationshinweis |
| Schnelleinstieg | Situation → Körperregion über Körperkarte oder Chips → drei Vorschläge → Start (A-055) |
| Körperkarte | Vorder- und Rückansicht mit klickbaren Regionen; Textalternative als Liste |
| Wochenplan | Person wählt Wochentage und eine Sammlung; Erinnerung über Erinnerungsfenster; Fortschritt als Wochenstreifen |
| Sammlungen | Einstieg, Schreibtisch, Produktion, Schicht, Rücken, Nacken und Schultern; vom Tenant freischaltbar, eigene Sammlungen möglich |
| Handlung | „Erledigt“ oder 90 % gesehen erzeugt „Inhalt abgeschlossen“ |
| Challenge-Bezug | Metrik „Plattformereignis: Inhalt abgeschlossen“ und Vorlagen wie Bewegte Pause, Ergonomie-Woche |
| Tenant-Inhalte | eigene Videos mit denselben Merkmalen; Untertitel empfohlen |
| Auswertung | Nutzung je Sammlung und Situation als Aggregat ab fünf (A-056) |

## 3. M4 Ergonomie (A-100)

| Element | Festlegung |
|---|---|
| Arbeitsplatztypen | Bildschirmarbeitsplatz, Stehplatz, Produktions- und Montageplatz, Hebe- und Tragetätigkeit, mobiler Arbeitsplatz; der Programm-Manager wählt, welche Typen der Tenant anbietet |
| Check | geführte Schritte je Typ mit Illustration und konkreten Maßangaben (Bildschirmhöhe, Sitz- und Tischhöhe, Anordnung, Fußstellung, Hebehaltung); Eingabe als Auswahl oder Zahl; 5 bis 8 Minuten |
| Ergebnis | drei Empfehlungen in Nutzersprache mit verknüpften Übungen aus M3, falls aktiv; Kategorie je Schritt (passt, anpassen, prüfen lassen) |
| Wiederholung | Erinnerung nach drei Monaten über Erinnerungsfenster; Verlauf nur für die Person |
| Speicherung | Ergebnis und Empfehlungen feldverschlüsselt, 12 Monate, jederzeit selbst löschbar (A-024) |
| Aggregat für den Arbeitsschutz | je Gruppe ab fünf abgeschlossenen Checks: Anzahl Checks und Anteil je Empfehlungskategorie; keine Maßwerte, keine Personen; sichtbar für Programm-Manager und Tenant-Admin |
| Handlung | abgeschlossener Check erzeugt „Ergonomie-Check“ |
| Challenge-Bezug | Metrik „Ergonomie-Check“; Vorlage Ergonomie-Woche |
| Hinweis | Der Check ist keine arbeitsmedizinische Beurteilung; Standardtext mit Verweis auf die Präventivfachkräfte des Tenants, wenn der Tenant-Admin sie hinterlegt hat |

## 4. M5 Wissen (A-101)

| Element | Festlegung |
|---|---|
| Inhalte | Artikel mit Lesezeit und Quellen, tägliche Kurzfakten, Quizze, Serienformate (A-052) |
| Feed | Kurzfakt des Tages als Inhaltskarte laut Redaktionsplan, höchstens einer je Tag |
| Quiz | Einfach- oder Mehrfachauswahl, Erklärung je Frage, bestanden ab 70 %; Ergebnis nur für die Person; Handlung einmal je Quiz |
| Lese-Serie | Serie aus Kurzfakten mit Tagesrhythmus; gelesener Beitrag ist Handlung mit Deckel zwei je Tag |
| Autorenprofile | für Plattform- und Tenant-Redaktion; Klarname der Redaktion, keine Mitglieder |
| Challenge-Bezug | Metriken „Beitrag gelesen“ und „Quiz bestanden“; Vorlagen Wissens-Quiz-Woche, Lese-Serie |
| Auswertung | Aufrufe, Abschlüsse und Quiz-Bestehensquote je Inhalt als Aggregat ab fünf; keine Einzelergebnisse |
| Grenze | keine Ranglisten nach Quiz-Punkten außerhalb einer Challenge |

## 5. M6 Regeneration (A-102)

| Element | Festlegung |
|---|---|
| Inhalte | Atemübungen mit Animation (Muster 4-4-4-4, 4-7-8, verlängertes Ausatmen), Kurzmeditationen und Entspannungsklänge als Audio 2 bis 15 Minuten, Pausenrituale |
| Atemanimation | Vollbild, Zyklusanleitung, ohne Ton möglich, reduzierte Bewegung respektiert; Abschluss ohne Bewertung |
| Empfehlungen je Schichtmodell | der Programm-Manager wählt Sammlungen für Früh-, Spät- und Nachtschicht |
| Stimmungs-Check-in | fünf Symbole ohne Zahlen, optional Stichworte aus einer festen Liste (Schlaf, Arbeit, Familie, Gesundheit, Wetter, Sonstiges), einmal je Tag; ausschließlich privat; feldverschlüsselt; 12 Monate; jederzeit selbst löschbar; **in keinem Aggregat, auch nicht anonym**; keine Sichtbarkeitsstufe; keine Feed-Karte; keine Benachrichtigung darüber |
| Verlauf | private Monatsansicht der Symbole; keine Trendbewertung, kein Vergleich, keine automatisierte Einordnung |
| Hilfsangebote | statischer Hinweis auf vom Tenant-Admin hinterlegte Anlaufstellen (Betriebsarzt, Sozialberatung, externe Beratung) auf der Check-in-Seite; kein Auslöser durch Werte |
| Handlung | Atemübung und Audio beendet erzeugen je eine Handlung. Der Stimmungs-Check-in erzeugt **keine** Handlung, kein Aktivitätsereignis und kein Metering-Ereignis; er hinterlässt außerhalb des feldverschlüsselten Speichers keine Spur |
| Challenge-Bezug | Metriken „Atemübungsminuten“ (Dauer) und „Inhalt abgeschlossen“; Vorlage Zwei Minuten Atempause; Stimmung ist nie Challenge-Metrik |
| Grenze | kein „Burnout-Frühwarnsystem“, keine Belastungsauswertung, keine KI-Einordnung |

## 6. M7 Ernährung (A-103)

| Element | Festlegung |
|---|---|
| Inhalte | Rezepte mit Zutaten, Mengen, Schritten, Zeit, Portionen und Hinweisen (kantinentauglich, vegetarisch, vegan, schnell); Zubereitungsvideos; Gewohnheitsvorlagen |
| Trinkerinnerung | Erinnerungsfenster mit Text „Wasser?“ über A-061; kein Zähler, kein Tagesziel in Litern |
| Gewohnheiten | Vorlagen als Häkchen-Challenges (Wasserwoche, Frühstück, Obst am Platz) |
| Handlung | „Gekocht“ oder Video beendet erzeugt „Inhalt abgeschlossen“ |
| Challenge-Bezug | Häkchen-Metriken; Vorlage Wasserwoche |
| Grenze | keine Kalorienzählung, keine Gewichtsziele, kein Ernährungs-Score, keine Diätpläne, keine Nährwertampeln; Rezepte nennen keine Kalorien |
| Tenant-Inhalte | eigene Rezepte der Kantine oder des Betriebs |

## 7. Schnittmengen dieses Themas

| Schnittmenge | Festlegung |
|---|---|
| Inhaltsmodule ↔ Feed und Inhalte | Kategorien, Typen, Redaktionsplan, Suche und Auswertung nach A-052 bis A-056 |
| Inhaltsmodule ↔ Fortschritt | pauschale Handlungen; Stimmungs-Check-in nur als Handlung ohne Inhalt |
| Inhaltsmodule ↔ Challenges | Plattformereignis-Metriken und Vorlagen mit Ersatzmetrik bei inaktivem Modul |
| Inhaltsmodule ↔ Benachrichtigungen | Wochenplan, Wiederholung des Ergonomie-Checks und Trinkerinnerung nur über Erinnerungsfenster |
| Inhaltsmodule ↔ Datenschutz | Ergonomie- und Stimmungsdaten feldverschlüsselt, 12 Monate, selbst löschbar; Ergonomie-Aggregate ab fünf ohne Maßwerte; Stimmung nie aggregiert |
| Inhaltsmodule ↔ Entitlements | je Modul eine Kategorie; Tenant-Inhalte setzen das Modul voraus |
| Inhaltsmodule ↔ Marke | Nutzersprache, Sperrliste, Tonalität in Leerzuständen |

## 8. Nachweise

1. Schnelleinstieg Bewegung liefert drei Vorschläge ohne Fachbegriff im Titel; Variante im Sitzen sichtbar.
2. Ergonomie-Aggregat erst ab fünf Checks je Gruppe, ohne Maßwerte; Einzelergebnis nur für die Person, nach Löschung nicht mehr vorhanden.
3. Stimmungs-Check-in erscheint in keiner Auswertung, keinem Export des Tenants, keiner Feed-Karte und keiner Benachrichtigung; Datenbankzugriff ohne Schlüssel liefert keine lesbaren Werte.
4. Quiz-Ergebnis nur für die Person; Handlung einmal je Quiz; keine Quiz-Rangliste außerhalb einer Challenge.
5. Trinkerinnerung und Wochenplan laufen über Erinnerungsfenster und zählen zu deren Kontingent.
6. Rezepte ohne Kalorienangabe; Suche nach „Kalorien“ liefert keine Funktion.
