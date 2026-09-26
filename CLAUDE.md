# Arbeitsregeln für Claude in diesem Repository

## Folgeprompt am Ende jeder abgeschlossenen Aufgabe

Nach jeder erfolgreich abgeschlossenen Aufgabe (insbesondere nach der Abnahme einer Stufe des
[technischen Durchstichs](concept/technischer-durchstich.md)) endet die Antwort mit einem **Folgeprompt**
für die nächste Aufgabe beziehungsweise die nächste Stufe (n+1). Der Folgeprompt

- enthält die Anweisung für die nächste Aufgabe vollumfänglich: Stufe, Ziel, Nachweise aus dem Konzept
  (Abschnitte und Registereinträge), offene Punkte aus den bisherigen Abnahmeprotokollen, Arbeitsweise
  (TDD nach Backend 9, Register im selben Commit, Produktwahlen mit Version), Abschluss (grüne CI,
  Abnahmeprotokoll unter `durchstich/abnahme/`, Nachweisstände im Register, README-Tabelle);
- ist so formuliert, dass er ohne diese Session ausführbar ist (Dateipfade relativ zum Repository, keine
  Verweise auf Sessionzustand);
- enthält selbst wieder diese Anweisung, sodass am Ende der nächsten Aufgabe automatisch der Prompt für die
  dann folgende Stufe (n+2) ausgegeben wird.

Reihenfolge der Stufen nach [technischer-durchstich.md](concept/technischer-durchstich.md), Abschnitt 3;
Stufen 4 und 5 dürfen parallel laufen, Stufe 6 setzt beide voraus.

## Abnahme einer Stufe

1. Umsetzung mit rotem Test aus fachlichem Grund, Register-Änderungen im selben Commit (A-030, A-106).
2. Push, CI abwarten; der grüne Lauf wird im Abnahmeprotokoll referenziert (Lauf-ID, Commit, Jobs, Testzahlen).
3. Nachweisstände „technisch nachgewiesen“ mit Datum im Register (`concept/architektur-entscheidungen.md`),
   README-Tabelle auf das Protokoll verweisen, Versionen in `durchstich/versionen.md`.
