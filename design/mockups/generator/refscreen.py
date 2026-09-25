import json, pathlib
from string import Template

P = pathlib.Path(__file__).parent.parent / "canvas"

NEUTRAL = {
  "light": dict(S="#FCFCFF", SC="#F1F2F9", SCH="#E9EAF2", ONS="#1A1B21", ONSV="#45464F", OL="#767780", OLV="#C6C6D0",
                T="#0F766E", TC="#B8F0EA", ONTC="#00201D", ERR="#B3261E", SCRIM="rgba(26,27,33,.45)"),
  "dark":  dict(S="#121318", SC="#1E1F25", SCH="#292A30", ONS="#E3E2E9", ONSV="#C6C6D0", OL="#90909A", OLV="#45464F",
                T="#7FD8CE", TC="#00504A", ONTC="#B8F0EA", ERR="#FFB4AB", SCRIM="rgba(0,0,0,.6)"),
}
BRANDS = {
  "wiesner": dict(name="Wiesner Aktiv", du=True, letter="W",
    light=dict(P="#9B1B3A", ONP="#FFFFFF", PC="#FFD9DE", ONPC="#400012", PR="#B45309", ONPR="#FFFFFF", PRC="#FFE0B0", ONPRC="#3D2400"),
    dark=dict(P="#FFB2BE", ONP="#5F0F24", PC="#7D1230", ONPC="#FFD9DE", PR="#FFB84D", ONPR="#452B00", PRC="#6B4300", ONPRC="#FFE0B0")),
  "hoedl": dict(name="Hödl Fit", du=False, letter="H",
    light=dict(P="#0F7A45", ONP="#FFFFFF", PC="#A3F2C3", ONPC="#00210F", PR="#E4670A", ONPR="#FFFFFF", PRC="#FFDFC2", ONPRC="#331100"),
    dark=dict(P="#6FDB9A", ONP="#003919", PC="#0A5C33", ONPC="#A3F2C3", PR="#FFB77C", ONPR="#4A1F00", PRC="#6B2E00", ONPRC="#FFDFC2")),
  "plattform": dict(name="CompanyHero", du=True, letter="",
    light=dict(P="#2A45C9", ONP="#FFFFFF", PC="#DFE1FF", ONPC="#000F5C", PR="#E4670A", ONPR="#FFFFFF", PRC="#FFDFC2", ONPRC="#331100"),
    dark=dict(P="#B9C3FF", ONP="#05157A", PC="#1230AE", ONPC="#DFE1FF", PR="#FFB77C", ONPR="#4A1F00", PRC="#6B2E00", ONPRC="#FFDFC2")),
}
SIZE = {
  "std":   dict(FB=16, FM=14, FS=12, FT=18, FH=26, HC=48, TILE=72, ICO=24, GAP=12),
  "gross": dict(FB=20, FM=18, FS=14, FT=22, FH=30, HC=56, TILE=96, ICO=28, GAP=16),
}

HEAD = Template("""<!doctype html>
<html lang="de">
<head>
<meta charset="utf-8">
<title>$TITLE</title>
<script src="./support.js"></script>
</head>
<body>
<x-dc>
<helmet>
<link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;600;700&family=Inter+Tight:wght@600;700&display=swap" rel="stylesheet">
<style>
body{margin:0;font-family:Inter,system-ui,sans-serif;background:$S;color:$ONS}
a{color:$P}a:hover{color:$ONPC}
.ch-stack > *{animation:ch-rise 320ms cubic-bezier(.2,.7,.2,1) both}
.ch-stack > :nth-child(2){animation-delay:60ms}.ch-stack > :nth-child(3){animation-delay:120ms}.ch-stack > :nth-child(4){animation-delay:180ms}
.ch-fill{animation:ch-fill 320ms cubic-bezier(.2,.7,.2,1) 200ms both}
.ch-dialog{animation:ch-pop 200ms cubic-bezier(.2,.7,.2,1) 400ms both}
.ch-scrim{animation:ch-fade 200ms ease 400ms both}
button,a{transition:background-color 120ms ease,color 120ms ease,transform 120ms ease,filter 120ms ease}
button:not(:disabled):hover,a:hover{filter:brightness(.97)}button:not(:disabled):active{transform:scale(.98)}
*:focus-visible{outline:2px solid $P;outline-offset:2px}
@keyframes ch-rise{from{opacity:0;transform:translateY(8px)}to{opacity:1;transform:none}}
@keyframes ch-fill{from{width:0}}
@keyframes ch-pop{from{opacity:0;transform:translateY(12px) scale(.98)}to{opacity:1;transform:none}}
@keyframes ch-fade{from{opacity:0}to{opacity:1}}
@media (prefers-reduced-motion:reduce){.ch-stack>*,.ch-fill,.ch-dialog,.ch-scrim{animation:none}button,a{transition:none}}
</style>
</helmet>
""")

FOOT = Template("""</x-dc>
<script type="text/x-dc" data-dc-script data-props='{"$$preview":{"width":$W,"height":$H}}'>
class Component extends DCLogic {
  renderVals() { return {}; }
}
</script>
</body>
</html>
""")

def logo(b, v):
    if b == "plattform":
        return ('<svg width="34" height="24" viewBox="0 0 28 20" aria-hidden="true"><path d="M2 18 A16 16 0 0 1 8 8" fill="none" stroke="%(P)s" stroke-width="4" stroke-linecap="round"></path><path d="M10 6.5 A16 16 0 0 1 18 4" fill="none" stroke="%(P)s" stroke-width="4" stroke-linecap="round"></path><path d="M20.5 4.5 A16 16 0 0 1 26 10" fill="none" stroke="%(PR)s" stroke-width="4" stroke-linecap="round"></path></svg>'
                '<span style="font-family: \'Inter Tight\', Inter, sans-serif; font-size: 18px; font-weight: 700; color: %(P)s">Company<span style="color: %(PR)s">Hero</span></span>') % v
    return ('<div style="width: 32px; height: 32px; border-radius: 8px; background: %(P)s; display: flex; align-items: center; justify-content: center; color: %(ONP)s; font-family: \'Inter Tight\', Inter, sans-serif; font-weight: 700; font-size: 14px">%(LETTER)s</div>'
            '<span style="font-family: \'Inter Tight\', Inter, sans-serif; font-size: 18px; font-weight: 700">%(NAME)s</span>') % v

CHECK = '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M5 12l4 4L19 6"></path></svg>'
CHEV = '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 9l6 6 6-6"></path></svg>'

CONTENT = Template("""
<div class="ch-stack" style="display: flex; flex-direction: column; gap: ${GAP}px">
  <div style="border-radius: 16px; background: $SC; padding: ${PAD}px; display: flex; flex-direction: column; gap: 14px">
    <div style="display: flex; align-items: center; gap: 8px; flex-wrap: wrap">
      <span style="padding: 3px 10px; border-radius: 999px; background: $TC; color: $ONTC; font-size: ${FS}px; font-weight: 600">Firmenziel</span>
      <span style="font-size: ${FS}px; color: $ONSV">noch 9 Tage</span>
      <span style="flex-grow: 1"></span>
      <span style="font-size: ${FS}px; color: $ONSV">Stand vor 4 Min</span>
    </div>
    <div style="display: flex; align-items: flex-end; gap: 12px; flex-wrap: wrap">
      <div style="display: flex; flex-direction: column; gap: 2px; flex-grow: 1; min-width: 160px">
        <div style="font-family: 'Inter Tight', Inter, sans-serif; font-size: ${FH}px; line-height: 1.2; font-weight: 600">Rad oder Fuß zur Arbeit</div>
        <div style="font-size: ${FM}px; line-height: 1.45; color: $ONSV">Sammelziel · Häkchen · ganze Firma</div>
      </div>
      <div style="font-family: 'Inter Tight', Inter, sans-serif; font-size: 40px; line-height: 44px; font-weight: 700; color: $PR; font-variant-numeric: tabular-nums">48 %</div>
    </div>
    <div style="display: flex; flex-direction: column; gap: 6px" role="img" aria-label="48 Prozent des Firmenziels erreicht, nächster Meilenstein bei 50 Prozent">
      <div style="position: relative; height: 14px; border-radius: 999px; background: $PRC">
        <div class="ch-fill" style="position: absolute; left: 0; top: 0; height: 14px; width: 48%; border-radius: 999px; background: $PR"></div>
        <div style="position: absolute; top: 2px; left: 25%; width: 10px; height: 10px; margin-left: -5px; border-radius: 999px; background: $SC; border: 2px solid $PR; box-sizing: border-box"></div>
        <div style="position: absolute; top: 2px; left: 50%; width: 10px; height: 10px; margin-left: -5px; border-radius: 999px; background: $SC; border: 2px solid $OLV; box-sizing: border-box"></div>
        <div style="position: absolute; top: 2px; left: 75%; width: 10px; height: 10px; margin-left: -5px; border-radius: 999px; background: $SC; border: 2px solid $OLV; box-sizing: border-box"></div>
      </div>
      <div style="display: flex; justify-content: space-between; font-size: ${FS}px; color: $ONSV"><span>Start</span><span>25 %</span><span style="font-weight: 600; color: $ONS">50 %</span><span>75 %</span><span>Ziel</span></div>
    </div>
    <div style="display: flex; align-items: center; gap: 12px; flex-wrap: wrap">
      <div style="font-size: ${FM}px; line-height: 1.45; color: $ONSV; flex-grow: 1">$BEITRAG_HEUTE: <strong style="color: $ONS">noch offen</strong></div>
      <button type="button" style="height: ${HC}px; padding: 0 20px; border-radius: 999px; border: none; background: $P; color: $ONP; font-family: Inter, sans-serif; font-size: ${FM}px; font-weight: 600; cursor: pointer; display: flex; align-items: center; gap: 8px">$CHECK Heute erledigt</button>
    </div>
  </div>
  <form style="border-radius: 12px; background: $S; border: 1px solid $OLV; padding: ${PAD}px; display: flex; flex-direction: column; gap: 18px; margin: 0">
    <div style="display: flex; align-items: baseline; gap: 8px"><div style="font-size: ${FT}px; line-height: 1.3; font-weight: 600">Beitrag nachtragen</div><span style="flex-grow: 1"></span><div style="font-size: ${FS}px; color: $ONSV">bis 3 Tage rückwirkend</div></div>
    <div style="display: flex; flex-direction: column; gap: 4px">
      <div style="position: relative; height: ${HC}px; border: 2px solid $ERR; border-radius: 8px; padding: 0 14px; display: flex; align-items: center; background: $S; box-sizing: border-box">
        <label for="tag" style="position: absolute; top: -9px; left: 12px; padding: 0 4px; background: $S; font-size: ${FS}px; line-height: 16px; color: $ERR">Tag</label>
        <select id="tag" aria-invalid="true" aria-describedby="tag-hint" style="appearance: none; border: none; background: transparent; outline: none; flex-grow: 1; font-family: Inter, sans-serif; font-size: ${FB}px; color: $ONS; padding: 0; cursor: pointer"><option>Heute</option><option>Gestern</option><option selected>Vorgestern</option></select>
        <span style="color: $ONSV; display: flex">$CHEV</span>
      </div>
      <div id="tag-hint" style="font-size: ${FS}px; line-height: 16px; color: $ERR; padding: 0 16px">$FEHLER</div>
    </div>
    <div style="display: flex; flex-direction: column; gap: 4px">
      <div style="position: relative; height: ${HC}px; border: 2px solid $P; border-radius: 8px; padding: 0 14px; display: flex; align-items: center; background: $S; box-sizing: border-box">
        <label for="notiz" style="position: absolute; top: -9px; left: 12px; padding: 0 4px; background: $S; font-size: ${FS}px; line-height: 16px; color: $P">$NOTIZ</label>
        <input id="notiz" type="text" value="Bis zum Bahnhof gegangen" style="border: none; background: transparent; outline: none; flex-grow: 1; min-width: 0; width: 0; font-family: Inter, sans-serif; font-size: ${FB}px; color: $ONS; padding: 0">
        <span aria-hidden="true" style="width: 2px; height: 22px; background: $P; margin-left: 2px"></span>
      </div>
      <div style="font-size: ${FS}px; line-height: 16px; color: $ONSV; padding: 0 16px">Fokuszustand · 24 von 200 Zeichen</div>
    </div>
    <div style="display: flex; gap: 8px; justify-content: flex-end; flex-wrap: wrap">
      <button type="button" style="height: ${HC}px; padding: 0 20px; border-radius: 999px; border: none; background: transparent; color: $P; font-family: Inter, sans-serif; font-size: ${FM}px; font-weight: 600; cursor: pointer">Abbrechen</button>
      <button type="submit" disabled style="height: ${HC}px; padding: 0 24px; border-radius: 999px; border: none; background: $SCH; color: $OL; font-family: Inter, sans-serif; font-size: ${FM}px; font-weight: 600; cursor: not-allowed">Eintragen</button>
    </div>
  </form>
</div>
""")

DIALOG = Template("""
<div class="ch-scrim" style="position: absolute; inset: 0; background: $SCRIM; display: flex; align-items: flex-end; justify-content: center; padding: $DLGPAD; z-index: 5">
  <div class="ch-dialog" role="dialog" aria-modal="true" aria-labelledby="dlg-t" style="width: 100%; max-width: $DLGW; box-sizing: border-box; background: $SCH; color: $ONS; border-radius: $DLGR; padding: 24px 24px 32px 24px; display: flex; flex-direction: column; gap: 16px; box-shadow: 0 12px 32px rgba(0,0,0,.18)">
    <div id="dlg-t" style="font-family: 'Inter Tight', Inter, sans-serif; font-size: ${FH}px; line-height: 1.25; font-weight: 600">Beitrag von gestern löschen?</div>
    <div style="font-size: ${FB}px; line-height: 1.5; color: $ONSV">$DLG_TEXT Der Kollektivstand wird neu berechnet. Verdiente Abzeichen bleiben.</div>
    <div style="display: flex; gap: 8px; justify-content: flex-end; flex-wrap: wrap">
      <button type="button" style="height: ${HC}px; padding: 0 20px; border-radius: 999px; border: none; background: transparent; color: $P; font-family: Inter, sans-serif; font-size: ${FM}px; font-weight: 600; cursor: pointer">Abbrechen</button>
      <button type="button" style="height: ${HC}px; padding: 0 24px; border-radius: 999px; border: none; background: $P; color: $ONP; font-family: Inter, sans-serif; font-size: ${FM}px; font-weight: 600; cursor: pointer">Löschen</button>
    </div>
  </div>
</div>
""")

def navitem(label, path, active, v, vertical=True, size=24):
    fill = v["P"] if active else "none"
    stroke = v["P"] if active else v["ONSV"]
    color = v["P"] if active else v["ONSV"]
    bg = v["PC"] if active else "transparent"
    icons = {
        "Start": '<path d="M4 11l8-7 8 7v9a1 1 0 0 1-1 1h-5v-6h-4v6H5a1 1 0 0 1-1-1z"></path>',
        "Challenges": '<path d="M4 20l6-6 4 4 6-8"></path><path d="M16 10h4v4"></path>',
        "Ich": '<circle cx="12" cy="8" r="4"></circle><path d="M4 21a8 8 0 0 1 16 0z"></path>',
    }
    svg = f'<svg width="{size}" height="{size}" viewBox="0 0 24 24" fill="{fill}" stroke="{stroke}" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">{icons[label]}</svg>'
    if vertical:
        return (f'<a href="#" aria-current="{"page" if active else "false"}" style="display: flex; flex-direction: column; align-items: center; gap: 4px; text-decoration: none; color: {color}; font-size: {v["FS"]}px; font-weight: 600">'
                f'<span style="display: flex; align-items: center; justify-content: center; width: 56px; height: 32px; border-radius: 999px; background: {bg}">{svg}</span>{label}</a>')
    return (f'<a href="#" aria-current="{"page" if active else "false"}" style="display: flex; align-items: center; gap: 12px; height: 44px; padding: 0 16px; border-radius: 999px; background: {bg}; text-decoration: none; color: {color}; font-size: 14px; font-weight: 600">{svg}{label}</a>')

def build_vars(brand, mode, size):
    b = BRANDS[brand]; v = {}
    v.update(NEUTRAL[mode]); v.update(b[mode]); v.update(SIZE[size])
    v["NAME"] = b["name"]; v["LETTER"] = b["letter"]; v["PAD"] = 16 if size == "std" else 20
    du = b["du"]
    v["BEITRAG_HEUTE"] = "Dein Beitrag heute" if du else "Ihr Beitrag heute"
    v["NOTIZ"] = "Notiz (nur für dich)" if du else "Notiz (nur für Sie)"
    v["FEHLER"] = "Für diesen Tag hast du schon ein Häkchen gesetzt." if du else "Für diesen Tag haben Sie schon ein Häkchen gesetzt."
    v["DLG_TEXT"] = "Dein Häkchen von gestern wird entfernt." if du else "Ihr Häkchen von gestern wird entfernt."
    v["CHECK"] = CHECK; v["CHEV"] = CHEV
    v["LOGO"] = logo(brand, v)
    return v

def mobile(brand, mode, size, title):
    v = build_vars(brand, mode, size); W, H = 390, (1000 if size == "std" else 1220)
    v.update(W=W, H=H, TITLE=title, DLGPAD="0", DLGW="none", DLGR="24px 24px 0 0")
    bell = f'<button type="button" aria-label="Benachrichtigungen" style="width: {v["HC"]}px; height: {v["HC"]}px; border-radius: 999px; border: none; background: transparent; display: flex; align-items: center; justify-content: center; cursor: pointer"><svg width="{v["ICO"]}" height="{v["ICO"]}" viewBox="0 0 24 24" fill="none" stroke="{v["ONS"]}" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 16V11a6 6 0 0 1 12 0v5l1.5 2h-15z"></path><path d="M10 20a2 2 0 0 0 4 0"></path></svg></button>'
    nav = ''.join(navitem(l, "#", l == "Challenges", v) for l in ["Start", "Challenges", "Ich"])
    body = (f'<div style="width: {W}px; height: {H}px; box-sizing: border-box; background: {v["S"]}; color: {v["ONS"]}; display: flex; flex-direction: column; position: relative; overflow: hidden">'
            f'<div style="display: flex; align-items: center; gap: 12px; padding: 56px 16px 12px 16px">{v["LOGO"]}<span style="flex-grow: 1"></span>{bell}</div>'
            f'<div style="padding: 0 16px 16px 16px">{CONTENT.substitute(v)}</div>'
            f'<nav aria-label="Hauptnavigation" style="position: absolute; left: 0; right: 0; bottom: 0; height: 80px; border-top: 1px solid {v["OLV"]}; background: {v["SC"]}; display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); padding: 8px 0 16px 0; box-sizing: border-box">{nav}</nav>'
            f'{DIALOG.substitute(v)}</div>')
    return HEAD.substitute(v) + body + FOOT.substitute(v), W, H

def tablet(brand, mode, title):
    v = build_vars(brand, mode, "std"); W, H = 800, 1000
    v.update(W=W, H=H, TITLE=title, DLGPAD="0 24px 40px 24px", DLGW="420px", DLGR="24px")
    rail = ''.join(navitem(l, "#", l == "Challenges", v) for l in ["Start", "Challenges", "Ich"])
    body = (f'<div style="width: {W}px; height: {H}px; box-sizing: border-box; background: {v["S"]}; color: {v["ONS"]}; display: flex; position: relative; overflow: hidden">'
            f'<nav aria-label="Hauptnavigation" style="width: 88px; flex-shrink: 0; background: {v["SC"]}; border-right: 1px solid {v["OLV"]}; display: flex; flex-direction: column; align-items: center; gap: 16px; padding: 24px 0; box-sizing: border-box">'
            f'<div style="width: 40px; height: 40px; border-radius: 10px; background: {v["P"]}; color: {v["ONP"]}; display: flex; align-items: center; justify-content: center; font-family: \'Inter Tight\', Inter, sans-serif; font-weight: 700; margin-bottom: 8px">{v["LETTER"] or "CH"}</div>{rail}</nav>'
            f'<main style="flex-grow: 1; display: flex; flex-direction: column; gap: 20px; padding: 32px 40px; box-sizing: border-box; max-width: 680px">'
            f'<div style="display: flex; align-items: center; gap: 12px"><h1 style="margin: 0; font-family: \'Inter Tight\', Inter, sans-serif; font-size: 32px; line-height: 40px; font-weight: 700">Challenges</h1><span style="flex-grow: 1"></span><span style="font-size: 14px; color: {v["ONSV"]}">{v["NAME"]}</span></div>'
            f'{CONTENT.substitute(v)}</main>{DIALOG.substitute(v)}</div>')
    return HEAD.substitute(v) + body + FOOT.substitute(v), W, H

def desktop(brand, mode, title):
    v = build_vars(brand, mode, "std"); W, H = 1280, 960
    v.update(W=W, H=H, TITLE=title, DLGPAD="0 24px 40px 24px", DLGW="420px", DLGR="24px")
    side = ''.join(navitem(l, "#", l == "Challenges", v, vertical=False) for l in ["Start", "Challenges", "Ich"])
    ctx = (f'<aside style="width: 320px; flex-shrink: 0; border-left: 1px solid {v["OLV"]}; padding: 32px 24px; box-sizing: border-box; display: flex; flex-direction: column; gap: 16px">'
           f'<div style="font-size: 12px; font-weight: 600; letter-spacing: .06em; text-transform: uppercase; color: {v["ONSV"]}">Dein Anteil</div>'
           f'<div style="display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 8px">'
           f'<div style="padding: 12px; border-radius: 8px; background: {v["SC"]}"><div style="font-family: \'Inter Tight\', Inter, sans-serif; font-size: 28px; line-height: 32px; font-weight: 700; font-variant-numeric: tabular-nums">11</div><div style="font-size: 12px; color: {v["ONSV"]}">Häkchen gesetzt</div></div>'
           f'<div style="padding: 12px; border-radius: 8px; background: {v["SC"]}"><div style="font-family: \'Inter Tight\', Inter, sans-serif; font-size: 28px; line-height: 32px; font-weight: 700; font-variant-numeric: tabular-nums">14</div><div style="font-size: 12px; color: {v["ONSV"]}">von 21 Tagen dabei</div></div></div>'
           f'<div style="display: flex; align-items: center; gap: 6px; padding: 6px 12px; border-radius: 999px; background: {v["PRC"]}; color: {v["ONPRC"]}; font-size: 14px; font-weight: 600; align-self: flex-start"><svg width="16" height="16" viewBox="0 0 24 24" fill="{v["PR"]}" aria-hidden="true"><path d="M12 2c1 4 5 5 5 10a5 5 0 0 1-10 0c0-2 1-3 1-3s0 3 2 3c2 0 0-5 2-10z"></path></svg>14 Tage Serie</div>'
           f'<div style="font-size: 12px; line-height: 16px; color: {v["ONSV"]}">Nur für dich sichtbar. Gegenüber anderen gilt „Mein Team“.</div></aside>')
    body = (f'<div style="width: {W}px; height: {H}px; box-sizing: border-box; background: {v["S"]}; color: {v["ONS"]}; display: flex; position: relative; overflow: hidden">'
            f'<nav aria-label="Hauptnavigation" style="width: 240px; flex-shrink: 0; background: {v["SC"]}; border-right: 1px solid {v["OLV"]}; display: flex; flex-direction: column; gap: 4px; padding: 20px 12px; box-sizing: border-box">'
            f'<div style="display: flex; align-items: center; gap: 10px; padding: 4px 8px 16px 8px">{v["LOGO"]}</div>{side}</nav>'
            f'<main style="flex-grow: 1; display: flex; flex-direction: column; gap: 20px; padding: 32px 40px; box-sizing: border-box; min-width: 0">'
            f'<h1 style="margin: 0; font-family: \'Inter Tight\', Inter, sans-serif; font-size: 32px; line-height: 40px; font-weight: 700">Challenges</h1>'
            f'{CONTENT.substitute(v)}</main>{ctx}{DIALOG.substitute(v)}</div>')
    return HEAD.substitute(v) + body + FOOT.substitute(v), W, H

def kiosk(brand, mode, title):
    v = build_vars(brand, mode, "gross"); W, H = 1024, 768
    v.update(W=W, H=H, TITLE=title)
    keys = ''.join(f'<button type="button" style="height: 72px; border-radius: 12px; border: 1px solid {v["OLV"]}; background: {v["S"]}; color: {v["ONS"]}; font-family: \'Inter Tight\', Inter, sans-serif; font-size: 28px; font-weight: 600; cursor: pointer">{k}</button>' for k in ["1","2","3","4","5","6","7","8","9"])
    keys += f'<button type="button" aria-label="Löschen" style="height: 72px; border-radius: 12px; border: 1px solid {v["OLV"]}; background: {v["SC"]}; color: {v["ONS"]}; font-size: 20px; cursor: pointer">⌫</button>'
    keys += f'<button type="button" style="height: 72px; border-radius: 12px; border: 1px solid {v["OLV"]}; background: {v["S"]}; color: {v["ONS"]}; font-family: \'Inter Tight\', Inter, sans-serif; font-size: 28px; font-weight: 600; cursor: pointer">0</button>'
    keys += f'<button type="button" style="height: 72px; border-radius: 12px; border: none; background: {v["P"]}; color: {v["ONP"]}; font-family: Inter, sans-serif; font-size: 18px; font-weight: 600; cursor: pointer">Weiter</button>'
    pin = ''.join(f'<div style="width: 56px; height: 64px; border-radius: 8px; border: 2px solid {c}; background: {v["S"]}; display: flex; align-items: center; justify-content: center; font-size: 28px; color: {v["ONS"]}">{t}</div>' for c, t in [(v["OLV"], "●"), (v["OLV"], "●"), (v["P"], ""), (v["OLV"], "")])
    body = (f'<div style="width: {W}px; height: {H}px; box-sizing: border-box; background: {v["S"]}; color: {v["ONS"]}; display: flex; position: relative; overflow: hidden">'
            f'<div style="width: 440px; flex-shrink: 0; background: {v["SC"]}; padding: 48px 40px; box-sizing: border-box; display: flex; flex-direction: column; gap: 24px">'
            f'<div style="display: flex; align-items: center; gap: 12px">{v["LOGO"]}</div>'
            f'<div style="font-family: \'Inter Tight\', Inter, sans-serif; font-size: 36px; line-height: 44px; font-weight: 700">Kiosk<br>Halle 2, Tor 3</div>'
            f'<div style="font-size: 20px; line-height: 30px; color: {v["ONSV"]}">Melde dich mit deiner Kennung und PIN an. Deine Eingaben zählen sofort zum Firmenziel.</div>'
            f'<div style="border-radius: 16px; background: {v["S"]}; padding: 20px; display: flex; flex-direction: column; gap: 12px; margin-top: auto">'
            f'<div style="display: flex; align-items: center; gap: 8px"><span style="padding: 4px 12px; border-radius: 999px; background: {v["TC"]}; color: {v["ONTC"]}; font-size: 14px; font-weight: 600">Firmenziel</span><span style="font-size: 14px; color: {v["ONSV"]}">Rad oder Fuß zur Arbeit</span></div>'
            f'<div style="display: flex; align-items: center; gap: 12px"><div style="flex-grow: 1; height: 14px; border-radius: 999px; background: {v["PRC"]}; position: relative"><div class="ch-fill" style="position: absolute; left: 0; top: 0; height: 14px; width: 48%; border-radius: 999px; background: {v["PR"]}"></div></div><span style="font-family: \'Inter Tight\', Inter, sans-serif; font-size: 28px; font-weight: 700; color: {v["PR"]}; font-variant-numeric: tabular-nums">48 %</span></div></div></div>'
            f'<main class="ch-stack" style="flex-grow: 1; padding: 48px 56px; box-sizing: border-box; display: flex; flex-direction: column; gap: 24px">'
            f'<div style="display: flex; flex-direction: column; gap: 6px"><label for="kennung" style="font-size: 18px; font-weight: 600; color: {v["ONSV"]}">Kennung</label><div style="height: 64px; border: 2px solid {v["OL"]}; border-radius: 8px; padding: 0 20px; display: flex; align-items: center; background: {v["S"]}; box-sizing: border-box"><input id="kennung" type="text" value="W-4812" style="border: none; background: transparent; outline: none; flex-grow: 1; font-family: \'Inter Tight\', Inter, sans-serif; font-size: 28px; letter-spacing: .08em; color: {v["ONS"]}"></div></div>'
            f'<div style="display: flex; flex-direction: column; gap: 6px"><div style="font-size: 18px; font-weight: 600; color: {v["ONSV"]}">PIN</div><div style="display: flex; gap: 12px" role="group" aria-label="PIN, 4 Stellen">{pin}</div></div>'
            f'<div style="display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; max-width: 360px">{keys}</div>'
            f'<div style="display: flex; gap: 24px; margin-top: auto; flex-wrap: wrap"><a href="#" style="font-size: 18px; font-weight: 600; text-decoration: none; padding: 16px 0">Neu hier? Mit Code beitreten</a><a href="#" style="font-size: 18px; font-weight: 600; text-decoration: none; padding: 16px 0; color: {v["ONSV"]}">PIN vergessen</a></div>'
            f'</main></div>')
    return HEAD.substitute(v) + body + FOOT.substitute(v), W, H

boards = {}
def emit(fname, html, w, h, x, y, title):
    (P / fname).write_text(html, encoding="utf-8")
    boards[fname] = {"x": x, "y": y, "w": w, "h": h, "title": title, "is_interactive": True}

Y1 = 2700
mob = [("wiesner","light","std","Wiesner · hell · Du"), ("wiesner","dark","std","Wiesner · dunkel"), ("hoedl","light","std","Hödl · hell · Sie"),
       ("hoedl","dark","std","Hödl · dunkel"), ("plattform","light","std","Plattform · hell"), ("plattform","dark","std","Plattform · dunkel"),
       ("wiesner","light","gross","Wiesner · Großflächenmodus")]
for i, (b, m, s, t) in enumerate(mob):
    html, w, h = mobile(b, m, s, f"Referenzscreen mobil: {t}")
    emit(f"Ref-Mobil-{b}-{m}{'-gross' if s=='gross' else ''}.dc.html", html, w, h, i*470, Y1, f"R{i+1} · {t}")

Y2 = Y1 + 1220 + 120
html, w, h = tablet("wiesner", "light", "Referenzscreen Tablet: Wiesner hell"); emit("Ref-Tablet-wiesner-light.dc.html", html, w, h, 0, Y2, "R8 · Tablet 800 · Wiesner hell")
html, w, h = tablet("hoedl", "dark", "Referenzscreen Tablet: Hödl dunkel"); emit("Ref-Tablet-hoedl-dark.dc.html", html, w, h, 880, Y2, "R9 · Tablet 800 · Hödl dunkel")
html, w, h = kiosk("wiesner", "light", "Kiosk-Anmeldemaske im Großflächenmodus"); emit("Ref-Kiosk-wiesner.dc.html", html, w, h, 1760, Y2, "R10 · Kiosk 1024 · Großflächenmodus")

Y3 = Y2 + 1000 + 120
for i, (b, m, t) in enumerate([("wiesner","light","Wiesner hell"), ("hoedl","dark","Hödl dunkel"), ("plattform","light","Plattform hell")]):
    html, w, h = desktop(b, m, f"Referenzscreen Desktop: {t}")
    emit(f"Ref-Desktop-{b}-{m}.dc.html", html, w, h, i*1360, Y3, f"R{11+i} · Desktop 1280 · {t}")

json.dump({"boards": boards, "Y1": Y1, "Y2": Y2, "Y3": Y3}, open(P.parent / "generator" / "ref-boards.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("ok", len(boards), "boards", Y1, Y2, Y3)
