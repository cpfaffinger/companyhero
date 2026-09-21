# CompanyHero – Organisation und Mandanten

**Stand:** 20.09.2026  
**Status:** Beschlossen gemäß A-033 bis A-037 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung.  
**Domäne:** Organisation und Mandanten gemäß [Domänenkarte](domaenen-und-schnittmengen.md).  
**Bezug:** [Zugang und Identität](zugang-und-identitaet.md), [Datenschutz und Nachweis](datenschutz-und-nachweis.md).

## 1. Organisationsmodell (A-033)

### 1.1 Struktur

Eine Entität `organisation` mit Typ und optionaler Elternbeziehung. Höchstens drei Ebenen:

```
Operator (genau eine Organisation, Wurzel)
  ├── Tenant                       (parent = Operator)
  └── Partner                      (parent = Operator)
        └── Tenant                 (parent = Partner)
```

| Typ | Anzahl | Zweck |
|---|---|---|
| Operator | genau eine | Betreiber der Plattform; Heimat der Operator-Personen und -Rollen |
| Partner | beliebig | Reseller oder Konzern-Holding; legt Tenants an und setzt Rahmen; kein Zugriff auf Inhalte oder Mitgliederdaten der Tenants |
| Tenant | beliebig | Firma mit Mitgliedern, Gruppen, Challenges und eigener Marke |

Ein Partner hat keine Sub-Partner. Standorte, Abteilungen und Schichten sind Gruppen innerhalb eines Tenants, keine Organisationen. Ein Konzern wird als Partner mit einem Tenant je Gesellschaft abgebildet.

### 1.2 Stammdaten je Organisation

| Feld | Operator | Partner | Tenant |
|---|---|---|---|
| Anzeigename | ja | ja | ja |
| Firmierung, Rechtsform, Anschrift, Land, UID | ja | ja | ja |
| Rechnungsanschrift und -kontakt | – | ja | ja |
| Ansprechpartner (Klarname, E-Mail) | ja | ja | ja |
| Zeitzone | – | – | ja, Voreinstellung Europe/Vienna |
| Standardsprache | – | – | ja, Voreinstellung de-AT |
| Sollstärke gesamt | – | – | ja |
| Region (Bundesland) und Branche | – | – | optional, vom Operator gesetzt; nur für Arena-Ligen verwendet |

Marke, Anrede und Tonalität gehören zur Domäne Marke und Theme; Module zu Entitlements; Preise zu Metering und Abrechnung. Die Stammdaten hier sind Grundlage für Auftragsverarbeitungsvertrag und Rechnung.

### 1.3 Lebenszyklus eines Tenants

| Zustand | Bedeutung | Übergang |
|---|---|---|
| Eingerichtet | angelegt, erster Tenant-Admin-Rollencode ausgestellt, noch keine Mitglieder | wird aktiv, sobald der erste Tenant-Admin den Code eingelöst hat |
| Aktiv | regulärer Betrieb | Kündigung oder Sperre |
| Gesperrt | Zugriff für Mitglieder und Tenant-Rollen ausgesetzt; Daten bleiben vollständig erhalten; Mitglieder sehen einen neutralen Hinweis, Tenant-Admins den Grund | Entsperren durch Operator oder Partner; sonst Kündigung |
| Gekündigt | zum Monatsende wirksam; danach 90 Tage lesender Zugriff für Tenant-Admin und Einsichtsrolle zum Export; Mitglieder ohne Zugriff | nach 90 Tagen Löschung gemäß A-024 |
| Gelöscht | tenantbezogene Daten entfernt, Tenant-Schlüssel vernichtet | Endzustand |

Sperren darf der Operator, für Partner-Tenants auch der Partner-Admin; Gründe sind Zahlungsverzug oder Missbrauch und werden protokolliert. Kündigen dürfen Tenant-Admin, Partner-Admin und Operator, jeweils zum Monatsende. Laufende Challenges enden spätestens mit dem Kündigungstermin.

### 1.4 Anlage eines Tenants

Der Operator oder ein Partner-Admin legt den Tenant mit Stammdaten an, wählt gegebenenfalls einen Partner, setzt Module und Preisplan im jeweiligen Rahmen und stellt den ersten Tenant-Admin-Rollencode aus. Der Code wird außerhalb der Plattform übergeben. Mit der Einlösung entsteht die erste Person des Tenants mit Klarname, E-Mail oder externem Anbieter und Passkey oder externem Anbieter (A-014, A-015).

### 1.5 Keine Selbstregistrierung

Firmen registrieren sich nicht selbst. Tenants werden ausschließlich vom Operator-Admin oder von einem Partner-Admin angelegt. Es gibt keine öffentliche Registrierungsroute, keine Testphase ohne angelegten Tenant und keine automatische Vertragsannahme. Interessenten erreichen den Betreiber außerhalb der Plattform.

## 2. Gruppen und Sollstärken (A-034)

### 2.1 Dimensionen und Gruppen

- Ein Tenant definiert bis zu drei **Dimensionen** mit frei gewählter Bezeichnung, etwa Standort, Abteilung, Schicht. Voreinstellung sind diese drei Bezeichnungen; ein Tenant kann Dimensionen umbenennen oder deaktivieren.
- Jede Dimension enthält eine **flache Liste** von Gruppen. Keine Verschachtelung.
- Eine Person gehört je Dimension zu höchstens einer Gruppe. Die Zugehörigkeit wählt die Person beim Beitritt aus der Liste; sie kann sie jederzeit selbst ändern. Ein Beitrittscode kann Gruppen vorbelegen.
- Gruppen werden nicht gelöscht, solange Personen zugeordnet sind; sie werden **archiviert**. Archivierte Gruppen sind nicht mehr wählbar, bleiben aber für vergangene Auswertungen bestehen. Vor der Archivierung wählen betroffene Personen beim nächsten Öffnen der App eine neue Gruppe; bis dahin gelten sie in dieser Dimension als nicht zugeordnet.
- Zusammenlegen zweier Gruppen ist eine Operation mit Protokoll; Beiträge behalten die ursprüngliche Gruppe (Abschnitt 3.3).

### 2.2 Sollstärke

- Die **Sollstärke** ist die vom Tenant gemeldete Zahl der Beschäftigten, die eingeladen sind: einmal für den ganzen Tenant und je Gruppe. Die Tenant-Sollstärke ist unabhängig von der Summe der Gruppen, weil nicht jede Person einer Gruppe angehört.
- Sie ist der Nenner aller Beteiligungsanzeigen und wird überall als Schätzgröße ausgewiesen („62 % von 180 gemeldeten Personen“).
- Sie steuert nie Sichtbarkeit: Mindestzahl und Ranglistenschwelle beziehen sich auf tatsächlich aktivierte Personen (A-023).
- Änderungen der Sollstärke gelten ab einem Stichtag; die Historie bleibt erhalten, damit vergangene Quoten stabil bleiben.
- Die Verwaltung erinnert vierteljährlich an die Pflege und warnt bei Gruppen mit Sollstärke unter fünf, weil für diese Gruppen keine Aggregate erscheinen werden.
- Pflege durch Tenant-Admin und Programm-Manager, einzeln oder per CSV-Import von Gruppen und Sollstärken. Ein Personenimport existiert nicht; Beitritt ist immer anonym über Codes.

## 3. Mitgliedschaft (A-035)

### 3.1 Zustände

| Zustand | Bedeutung |
|---|---|
| Aktiv | Person ist Mitglied des Tenants |
| Ausgetreten | durch die Person selbst (A-019) oder automatisch nach 24 Monaten Inaktivität (A-024); Löschung binnen 30 Tagen |
| Entfernt | durch einen Tenant-Admin, etwa bei Missbrauch; wirkt wie Austritt; die Person sieht beim nächsten Anmeldeversuch einen Hinweis ohne Begründungstext; protokolliert |

Es gibt keinen Sperrzustand für einzelne Mitglieder und keine Obergrenze der Mitgliederzahl je Tenant.

### 3.2 Rechte des Tenant-Admins über Mitglieder

Sichtbar sind Anzeigename, Gruppen, Rollen und Beitrittsdatum (A-022). Möglich sind: Rolle entziehen, Mitglied entfernen, Rollencodes ausstellen. Nicht möglich: Anzeigename ändern, Gruppen einer Person ändern, Sichtbarkeit ändern, Nachrichten an eine einzelne Person senden.

### 3.3 Gruppenwechsel und Zuordnung

Ein Beitrag wird der Gruppe zugeordnet, der die Person zum Zeitpunkt des Beitrags angehörte. Ein späterer Gruppenwechsel verändert vergangene Kollektivstände nicht. Für laufende Challenges mit Gruppenwertung zählt die Person ab dem Wechsel für die neue Gruppe; bereits geleistete Beiträge bleiben bei der alten.

## 4. Rollen und Rechte (A-036)

### 4.1 Rollenkatalog

| Rolle | Ebene | Anzeigename | Anmeldung |
|---|---|---|---|
| Operator-Admin | Operator | Klarname | Passkey oder externer Anbieter |
| Operator-Support | Operator | Klarname | Passkey oder externer Anbieter |
| Partner-Admin | Partner | Klarname | Passkey oder externer Anbieter |
| Tenant-Admin | Tenant | Klarname | Passkey oder externer Anbieter |
| Programm-Manager | Tenant | Klarname | Passkey oder externer Anbieter |
| Redakteur | Tenant | Klarname | alle Wege |
| Einsichtsrolle | Tenant | Klarname | alle Wege |
| Gesundheitsbotschafter | Gruppe(n) eines Tenants | frei | alle Wege |
| Mitglied | Tenant | frei | alle Wege |

Eine Person kann mehrere Rollen ihrer Organisation halten. Rollen werden über Rollencodes vergeben (A-014) und vom Aussteller oder einer höheren Rolle entzogen. Jeder aktive Tenant hat mindestens einen Tenant-Admin; der letzte kann seine Rolle nicht selbst abgeben. Operator- und Partner-Rollen wirken ausschließlich über ihre Konsolen und nie innerhalb eines Tenants; wer zusätzlich Mitglied eines Tenants sein will, braucht eine eigene Login-Identität dafür.

### 4.2 Rechtematrix

| Fähigkeit | Op-Admin | Op-Support | Partner-Admin | Tenant-Admin | Programm-Manager | Botschafter | Redakteur | Einsicht | Mitglied |
|---|---|---|---|---|---|---|---|---|---|
| Partner anlegen, Tenants anlegen, sperren, kündigen | ja | – | Tenants im eigenen Partner | kündigen | – | – | – | – | – |
| Module und Preispläne setzen | ja | – | im Rahmen des Partners | Module buchen und kündigen | – | – | – | – | – |
| Stammdaten des Tenants | ja | lesen | eigene Tenants | ja | – | – | – | lesen | – |
| Marke und Anmeldewege | – | lesen | Voreinstellungen | ja | – | – | – | lesen | – |
| Gruppen, Dimensionen, Sollstärken | – | – | – | ja | ja | – | – | lesen | eigene Zuordnung |
| Rollencodes ausstellen | Operator-Rollen, erster Tenant-Admin | – | erster Tenant-Admin | alle Tenant-Rollen | Botschafter | – | – | – | – |
| Mitgliederliste (Anzeigename, Gruppen, Rolle, Beitritt) | – | – | – | ja | – | – | – | – | – |
| Mitglied entfernen | – | – | – | ja | – | – | – | – | – |
| Kiosk-Geräte registrieren und widerrufen | – | – | – | ja | – | – | – | – | – |
| Challenges tenantweit anlegen, Vorlagen freigeben, Kadenz | – | – | – | – | ja | – | – | – | – |
| Challenges für eigene Gruppen aus freigegebenen Vorlagen | – | – | – | – | ja | ja | – | – | – |
| Tenant-Beiträge und Dokumente | – | – | – | – | ja | eigene Gruppen | ja | – | – |
| Beteiligungsquoten je Gruppe ab fünf | – | – | – | ja | ja | eigene Gruppen | – | tenantweit | – |
| Arena-Beitritt und -Einstellungen | Arenen anlegen | – | – | ja | – | – | – | lesen | – |
| Kostenvorschau, Rechnungen, Verbrauchsdetail aggregiert | ja | – | eigene Tenants | ja | – | – | – | – | – |
| Prüfprotokoll | ja | eigener Zugriff | eigene Tenants | ja | – | – | – | ja | – |
| Datenschutzregeln und Schwellen ändern | – | – | – | – | – | – | – | – | – |
| Individuelle Aktivitätswerte anderer | – | – | – | – | – | – | – | – | nur Freigegebenes |
| Eigene Daten, Sichtbarkeit, Export, Austritt | – | – | – | – | – | – | – | – | ja |

Die letzten beiden Zeilen sind Produkteigenschaft (A-021). Operator-Support-Zugriff folgt A-025: nur mit Vorgangsnummer, 24 Stunden, protokolliert.

### 4.3 Gesundheitsbotschafter

Ein Botschafter ist an eine oder mehrere Gruppen gebunden. Er legt Challenges nur für diese Gruppen und nur aus den vom Programm-Manager freigegebenen Vorlagen an, schreibt Beiträge für seine Gruppen und sieht deren Beteiligungsquote ab fünf Beitragenden. Er sieht keine Mitgliederliste und keine Einzelwerte. Empfohlen ist ein Botschafter je 50 gemeldete Personen; die Verwaltung zeigt das Verhältnis.

## 5. Partner (A-037)

### 5.1 Rahmen für Tenants

Ein Partner setzt für seine Tenants:

| Vorgabe | Wirkung |
|---|---|
| Erlaubte Module | Tenants buchen nur innerhalb dieser Menge |
| Preisrahmen | Preispläne der Tenants liegen innerhalb des vom Operator dem Partner zugewiesenen Rahmens; Regeln in [Metering und Abrechnung](metering-und-abrechnung.md), Abschnitt 4.4 |
| Voreinstellungen für Marke | Logo, Saatfarbe, Anrede, Tonalität als Startwerte; der Tenant kann sie überschreiben |
| Voreinstellungen für Anmeldewege | Startwerte; der Tenant bestimmt endgültig (A-015) |

### 5.2 Was ein Partner sieht

Stammdaten, Zustand, gebuchte Module, Preisplan, aggregierte Abrechnungsmengen (Anzahl aktiver Mitglieder je Periode als Zahl) und das Prüfprotokoll seiner Tenants. Nicht: Mitgliederlisten, Inhalte, Challenges, Beteiligungsquoten, Gruppen oder Sollstärken. Die Abrechnungsmenge ist für Provisionen notwendig und steht als solche im Auftragsverarbeitungsvertrag.

### 5.3 Wechsel und Auflösung

Ein Tenant kann vom Operator einem anderen Partner zugeordnet oder aus einem Partner gelöst werden; Rahmen und Voreinstellungen werden dann neu angewendet, bestehende Buchungen bleiben bis zur nächsten Abrechnungsperiode gültig. Wird ein Partner aufgelöst, werden seine Tenants dem Operator direkt zugeordnet.

## 6. Schnittmengen dieses Themas

| Schnittmenge | Festlegung |
|---|---|
| Organisation ↔ Zugang | Beitritt erzeugt Person, Mitgliedschaft und Gruppenzuordnungen in einer Transaktion; Rollencodes werden von Zugang eingelöst und von Organisation als Rollen geführt; Tenant-Zustand „gesperrt“ oder „gekündigt“ beendet Mitgliedersitzungen |
| Organisation ↔ Datenschutz | Mitgliederliste ohne Aktivitätsdaten; Entfernen wirkt wie Austritt; Sollstärke steuert nie Sichtbarkeit; Gruppenumbau ohne Differenzbildung; alle Änderungen im Prüfprotokoll |
| Organisation ↔ Entitlements | Module vererben sich vom Partner-Rahmen; Tenant bucht innerhalb des Rahmens |
| Organisation ↔ Metering | Stammdaten liefern Rechnungsanschrift; Partner erhält Abrechnungsmengen als Zahl für Provisionen |
| Organisation ↔ Challenges | Gruppen und Dimensionen sind die einzigen Auswertungsgruppen; Beiträge behalten die Gruppe zum Beitragszeitpunkt |
| Organisation ↔ Marke | Partner-Voreinstellungen sind Startwerte der Tenant-Marke |

## 7. Nachweise

1. Hierarchie Operator → Partner → Tenant; ein vierter Ebenenversuch wird abgelehnt.
2. Partner-Admin legt Tenant an und stellt ersten Tenant-Admin-Rollencode aus; erhält danach keinen Zugriff auf Mitgliederliste, Challenges oder Quoten.
3. Tenant-Sperre beendet Mitgliedersitzungen und zeigt Mitgliedern einen neutralen Hinweis; Entsperren stellt den Zustand ohne Datenverlust wieder her.
4. Kündigung zum Monatsende: 90 Tage lesender Zugriff für Tenant-Admin und Einsichtsrolle, danach Löschung mit Schlüsselvernichtung.
5. Gruppenwechsel einer Person verändert vergangene Kollektivstände nicht; Archivierung einer Gruppe verlangt Neuwahl.
6. Sollstärke-Änderung mit Stichtag lässt vergangene Quoten unverändert; Warnung bei Sollstärke unter fünf.
7. Letzter Tenant-Admin kann seine Rolle nicht abgeben; Entfernen eines Mitglieds wirkt wie Austritt und steht im Protokoll.
8. Rechtematrix: jede Zelle mit „–“ wird über API und Oberfläche abgelehnt.
