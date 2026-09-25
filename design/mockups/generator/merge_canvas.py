import json, pathlib
root = pathlib.Path(__file__).parent
c = json.load(open(root.parent / "canvas" / "canvas.json", encoding="utf-8"))
ref = json.load(open(root / "ref-boards.json", encoding="utf-8"))
Y1, Y2, Y3 = ref["Y1"], ref["Y2"], ref["Y3"]
for k in [k for k in c["boards"] if k.startswith("Ref-")]:
    del c["boards"][k]
c["order"] = [k for k in c["order"] if not k.startswith("Ref-")]
for name, b in ref["boards"].items():
    c["boards"][name] = b
    c["order"].append(name)
n = c.setdefault("notes", {})
n["ref"] = {"kind": "title1", "maxW": 3300, "w": 240, "x": 0, "y": Y1 - 300,
            "text": "Referenzscreen für den Durchstich (Stufe 5) · Challenge-Karte, Formular mit Select-Fehler und Fokus, Dialog · mobil 390 px"}
n["ref-tablet"] = {"kind": "title1", "maxW": 2800, "w": 240, "x": 0, "y": Y2 - 280,
                   "text": "Referenzscreen · Tablet 800 px mit Navigations-Rail · Kiosk 1024 px im Großflächenmodus"}
n["ref-desktop"] = {"kind": "title1", "maxW": 4000, "w": 240, "x": 0, "y": Y3 - 280,
                    "text": "Referenzscreen · Desktop 1280 px mit Seitennavigation und Kontextspalte 320 px"}
n["ref-regeln"] = {"fill": "orange", "size": 24, "w": 420, "x": 2820, "y": Y2,
    "text": "Prüfliste je Board (Integrationsregeln §6, Marke §9):\n\n• Farbwerte identisch mit dem Backend-Tokensatz der Marke; Wiesner hat hier eine eigene warme Saatfarbe (Bordeaux), damit sich Firmenmarke und Plattformmarke unterscheiden. Fortschritt bleibt warm und ist nie Blau.\n• Select im Fehlerzustand, Textfeld im Fokus, Dialog offen, alle drei gleichzeitig sichtbar.\n• Kontrast 4,5:1 Text, 3:1 Bedienflächen und Balken, hell und dunkel.\n• Bedienflächen 48 px, im Großflächenmodus 56 px und Text zwei Stufen größer.\n• Bewegung nur 120/200/320 ms; bei „reduzierte Bewegung“ keine."}
json.dump(c, open(root.parent / "canvas" / "canvas.json", "w", encoding="utf-8"), ensure_ascii=False, indent=2)
print("boards", len(c["boards"]), "order", len(c["order"]))
