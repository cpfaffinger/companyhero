# CompanyHero – Mockups und Referenzscreens

**Stand:** 25.09.2026  
**Art:** Gestaltungsvorlage für den technischen Durchstich (Stufe 5, `concept/technischer-durchstich.md`). Kein Produktcode, keine Entscheidung; verbindlich bleibt `concept/`.  
**Bezug:** [Marke und Theme](../../concept/marke-und-theme.md), [Frontend](../../concept/architektur-frontend.md), [Integrationsregeln §6](../../concept/architektur-integrationsregeln.md).

Bearbeitbares Canvas mit allen Boards: https://claude.ai/artifact/2Wk51KijB6ikWgsK5761e1 (privat, Freigabe über das Share-Menü der Seite). Die Dateien hier sind derselbe Stand.

## Inhalt

| Pfad | Inhalt |
|---|---|
| `canvas/*.dc.html` | ein Board je Datei, eigenständiges HTML mit Inline-Styles; im Browser direkt zu öffnen (der fehlende `support.js` ist nur die Laufzeit des Canvas-Editors und ändert die Darstellung nicht) |
| `canvas/canvas.json` | Anordnung, Titel und Notizen der Boards im Canvas |
| `generator/refscreen.py` | erzeugt die 13 Referenzscreens aus einer Vorlage je Layoutstufe; enthält die Tokenwerte je Marke und Modus als benannte Variablen |
| `generator/merge_canvas.py` | trägt die erzeugten Boards in `canvas.json` ein |
| `referenz-tokens.json` | Rollen aus `marke-und-theme.md` 2.2 mit den hier verwendeten Werten je Marke und Modus |

## Boards

**Reihe 1, Mitglieder-App und Verwaltung (Screens 1 bis 9):** Beitritt (Sichtbarkeit, erstes Abzeichen), Start-Feed mit allen fünf Kartentypen, Challenge mit Meilenstein-Route und Erfassung, Ich, Entdecken, Arena in Plattform-Schale, zweiter Tenant im Dunkelmodus, Verwaltungsübersicht am Desktop. Diese Reihe zeigt das Produkt in der Breite und enthält Module außerhalb des Durchstichs (Inhalte, Arena, Firma).

**Reihe 2, Referenzscreen für Stufe 5 (R1 bis R13):** dieselbe Seite aus dem Durchstich-Umfang, Challenge-Karte als Sammelziel mit Häkchen, Formular mit Select im Fehlerzustand und Textfeld im Fokus, offener Dialog.

| Boards | Layoutstufe | Varianten |
|---|---|---|
| R1 bis R6 | mobil 390 px, untere Leiste | Wiesner (Du), Hödl (Sie), Plattform; je hell und dunkel |
| R7 | mobil 390 px | Großflächenmodus: 56 px Bedienflächen, Text zwei Stufen größer |
| R8, R9 | Tablet 800 px, Navigations-Rail | Wiesner hell, Hödl dunkel |
| R10 | Kiosk 1024 px | Anmeldemaske mit Kennung, PIN, Ziffernblock; immer Großflächenmodus |
| R11 bis R13 | Desktop 1280 px, Seitennavigation 240 px, Kontextspalte 320 px | Wiesner hell, Hödl dunkel, Plattform hell |

Navigation in Reihe 2 ist auf Start, Challenges und Ich reduziert, weil Entdecken und Firma Module voraussetzen, die nicht im Durchstich sind.

## Verwendung im Durchstich

- Die Boards sind das Soll-Bild für die manuelle Abnahme des Referenzscreens (Integrationsregeln §6, Frontend §7). Sie sind keine Angular-Vorlagen: Farben stehen als Hexwerte inline, im Frontend gelten ausschließlich `--ch-*`-Rollen aus dem Backend-Tokensatz (A-013, K01).
- `referenz-tokens.json` ordnet jedem Hexwert seine Rolle zu. Die Werte sind Gestaltungsannahmen; maßgeblich ist die deterministische Ableitung im Backend aus der Saatfarbe. Abweichungen zwischen Mockup und Ableitung sind erwartbar und kein Fehler des Durchstichs.
- Gemessen wurden alle Boards auf horizontalen Überlauf und auf Sichtbarkeit aller drei Teile (Fehlerzustand, Fokus, Dialog) gleichzeitig.
- Bilder sind beschriftete Platzhalter, weil das Konzept selbst produzierte Aufnahmen echter Arbeitsumgebungen verlangt.

## Offene Punkte

1. **Saatfarbe Wiesner.** In Reihe 1 nutzt Wiesner das Standardblau der Plattform, in Reihe 2 ein eigenes Bordeaux, damit sich Firmenmarke und Plattformmarke unterscheiden. Reihe 1 ist noch nicht angeglichen.
2. **Bewegungsdauer.** Reihe 2 nutzt nur 120, 200 und 320 ms gemäß Konzept. Reihe 1 hat Füllanimationen von 900 ms für Balken und Ringe; entweder kürzen oder als vierte Stufe „Füllen“ im Register entscheiden.
3. **Tablet und Desktop der Mitglieder-App** existieren nur als Referenzscreen, nicht für Start, Ich oder Entdecken.
