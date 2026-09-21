# CompanyHero – Metering und Abrechnung

**Stand:** 20.09.2026  
**Status:** Beschlossen gemäß A-069 bis A-075 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung.  
**Domäne:** Metering und Abrechnung gemäß [Domänenkarte](domaenen-und-schnittmengen.md).  
**Bezug:** [Entitlements](entitlements.md), [Organisation und Mandanten](organisation-und-mandanten.md), [Datenschutz und Nachweis](datenschutz-und-nachweis.md), [Fortschritt](fortschritt.md).

Die Preisgestaltung ist maximal variabel und nie fest. Jeder Kunde erhält ein individuelles Paket aus unterschiedlichen Metriken: je Mitglied, Flatrate, je Beitrag, gemischt. Deshalb ist Abrechnung keine Rechnungsfunktion, sondern eine Querschnittsschicht: **Jede Domäne emittiert Ereignisse, Metering bewertet sie, Abrechnung fakturiert sie.** Entitlements entscheiden über Zugang, nie über Geld (A-065).

## 1. Grundsätze

- **Append-only:** Der Ledger ist unveränderlich. Korrekturen sind Gegenbuchungen, keine Änderungen.
- **Ohne Personenbezug:** Kein Ledger-Ereignis enthält Klarnamen oder Mitglieds-IDs. „Aktives Mitglied“ ist ein je Periode neu gesalzener Zähler-Slot: innerhalb einer Periode zählbar, über Perioden nicht verkettbar.
- **Von Anfang an instrumentiert:** Jede abrechnungsrelevante Entität emittiert Ereignisse, auch wenn keine Preisregel darauf verweist. Nachträgliches Instrumentieren erzeugt Lücken.
- **Erklärbar:** Jede Rechnungsposition verweist auf Regel und Mengennachweis. Variable Preise ohne Echtzeit-Transparenz erzeugen Streit; mit Transparenz sind sie ein Vorteil.
- **Nutzung statt Vertragsgröße:** Die Standardmetrik ist das aktive Mitglied. Der Kunde zahlt für Beteiligung, nicht für Karteileichen; das richtet die Interessen von Betreiber und Kunde gleich aus.
- **Keine Hintertür zur Auswertung:** Verbrauchsdetails zeigen nur Aggregate. Über die Buchhaltung entsteht keine Auswertung, die vorne ausgeschlossen ist.

## 2. Metering-Ledger (A-069)

### 2.1 Ereignis

| Feld | Inhalt |
|---|---|
| id | zeitlich sortierbare Kennung |
| organisation_id | Tenant; bei Partnerabrechnung zusätzlich Partner |
| modul | Kern oder M1 bis M12 |
| metrik | aus dem Katalog in Abschnitt 3 |
| subject_ref | anonymer Bezug: Objektkennung (Challenge, Inhalt, Gerät) oder periodengesalzener Zähler-Slot; nie eine Personen-ID |
| menge | Dezimalzahl mit vier Nachkommastellen |
| quelle | selbst, Plattform, automatisch, Verwaltung |
| eingetreten_am | fachlicher Zeitpunkt in der Tenant-Zeitzone |
| periode | Abrechnungsperiode YYYY-MM, in die gebucht wird |
| idempotenzschlüssel | aus Fachereignis-ID und Metrik; verhindert Doppelzählung bei Wiederholungen |
| nachlauf | gesetzt, wenn das Ereignis nach Versiegelung seiner fachlichen Periode eintraf |
| storno_von | Verweis bei Gegenbuchung |
| metadaten | technisch, ohne Personenbezug |

### 2.2 Regeln

- Der Ledger ist je Periode partitioniert. Eine Periode ist offen bis zur Versiegelung, danach unveränderlich.
- **Versiegelung** erfolgt am dritten Kalendertag des Folgemonats um 03:00 Tenant-Zeit, nach Ablauf der Nachfrist für Offline-Beiträge (A-039).
- Ereignisse, die nach Versiegelung für die versiegelte Periode eintreffen, werden in die offene Periode gebucht, behalten ihren fachlichen Zeitpunkt und tragen die Kennzeichnung Nachlauf.
- Testphasen-Ereignisse werden vollständig erfasst und mit „nicht bewertet“ markiert; sie ermöglichen die Aussage „in der Testphase wären das X Euro gewesen“.
- Gegenbuchungen entstehen bei Korrektur oder Löschung des Auslösers (etwa Korrektur eines Beitrags, A-039) und bei Gutschriften.
- Aufbewahrung sieben Jahre (A-024). Exporte des Ledgers für Prüfzwecke enthalten dieselben anonymen Bezüge.

### 2.3 Pseudonymisierung des aktiven Mitglieds

- Je Tenant und Periode erzeugt Metering ein **Periodensalz** in OpenBao.
- Der Zähler-Slot einer Person ist ein schlüsselabhängiger Hash aus Personenkennung und Periodensalz. Innerhalb der Periode ergibt jede Handlung derselben Person denselben Slot; über Perioden hinweg sind Slots nicht verkettbar.
- Das Periodensalz wird **sieben Tage nach Versiegelung vernichtet**. Danach ist keine Rückrechnung mehr möglich; Korrekturen laufen als Gegenbuchung in der offenen Periode.
- Slots erscheinen nie in Oberflächen oder Exporten für Tenant oder Partner; sie dienen ausschließlich der Zählung.

## 3. Metrikkatalog und Emission (A-070)

### 3.1 Katalog

| Metrik | Einheit | Modul | Emittiert von | Typische Verwendung |
|---|---|---|---|---|
| `member.active_month` | aktive Mitglieder je Monat | Kern | Fortschritt | **Standardmetrik** |
| `member.active_day` | aktive Tage | Kern | Fortschritt | tagesgenau bei Saisonbetrieb |
| `member.joined` | Beitritte | Kern | Zugang | Einrichtungspauschalen je Person |
| `tenant.month` | Monate | alle | Entitlements | Flatrate je Modul |
| `module.trial_day` | Testtage | alle | Entitlements | Nachweis, nie bewertet |
| `challenge.active_month` | Challenges je Monat | M1 | Challenges | Programmintensität |
| `challenge.participant_day` | Teilnehmertage | M1 | Challenges | verbrauchsnah |
| `arena.entry` | Beitritte | M2 | Arena | je Teilnahme |
| `arena.size_class` | Größenklasse je Monat | M2 | Arena | Staffel ohne Personenzähler |
| `content.published` | veröffentlichte Inhalte | M3, M5, M6, M7, M10 | Feed und Inhalte | „Euro je Video“ |
| `content.minute_played` | Abspielminuten | M3, M6 | Feed und Inhalte | nutzungsabhängig |
| `content.completion` | Abschlüsse | M3 bis M7 | Feed und Inhalte | ergebnisabhängig |
| `check.completed` | Ergonomie-Checks | M4 | Feed und Inhalte | je Check |
| `wearable.tenant_month` | Monate | M8 | Entitlements | Flatrate je Tenant; **nie je Verbindung**, sonst verriete die Rechnung, wie viele Personen ein Gerät verbunden haben |
| `event.booking` | gebuchte Plätze | M9 | Workshops | je Platzkontingent; **nie je tatsächlichem Teilnehmer** |
| `event.delivered` | durchgeführte Termine | M9 | Workshops | Festpreis oder Provision |
| `event.cancellation` | Stornobetrag | M9 | Workshops | Storno innerhalb der Frist |
| `workshop.credit_redeemed` | eingelöste Belohnungen | M9 | Challenges | meist 0 Euro, protokolliert |
| `channel.post` | Firmenkanal-Beiträge | M10 | Feed und Inhalte | je Beitrag |
| `document.stored` | Dokumente je Monat | M10 | Feed und Inhalte | Speicherbezug |
| `language.active_month` | aktive Zusatzsprachen | M11 | Entitlements | je Sprache |
| `directory.sync_month` | Monate | M12 | Verzeichnis | Flatrate |
| `notification.push_sent` | gesendete Push | Kern | Benachrichtigungen | nur falls Volumen relevant |
| `notification.email_sent` | gesendete E-Mails | Kern | Benachrichtigungen | nur bei Plattformtransport |
| `kiosk.device_month` | Kiosk-Geräte je Monat | Kern | Zugang | optional |
| `storage.gb_month` | GB-Monate | Kern | Feed und Inhalte | Medienspeicher |
| `ai.call` | KI-Aufrufe | Kern | Feed und Inhalte | nur Nachweis, Kosten trägt der Tenant über eigene Zugangsdaten |
| `setup.onetime` | Stück | alle | Verwaltung | Einrichtung, Import, Schulung |

Neue Metriken sind Konfiguration plus eine Emissionsstelle, kein Preisrelease. Eine Metrik ohne Preisregel wird erfasst und nicht bewertet.

### 3.2 Aktives Mitglied

> **Aktives Mitglied** ist eine Person mit mindestens einer gewerteten Handlung im Abrechnungsmonat mit Quelle „selbst“ oder „Plattform“ gemäß A-044: Check-in, Challenge-Beitrag, abgeschlossener Inhalt, gelesener Beitrag, bestandenes Quiz, Feed-Beitrag, gegebene Anerkennung, Ergonomie-Check, Atemübung, Veranstaltungsbesuch mit Check-in.
>
> **Nicht** als Handlung zählen: Öffnen der App, Anmeldung ohne Handlung, empfangene Benachrichtigungen, automatische Wearable-Tageswerte, Kommentare, Systemereignisse.

Öffnen zählt nicht, sonst erzeugte jede Erinnerung Umsatz und der Betreiber hätte ein Interesse an Erinnerungen statt an Beteiligung. Die Definition steht im Vertrag, in der Verwaltung im Klartext und bei jeder Rechnungsposition dieser Metrik.

Für `member.active_month` emittiert Fortschritt bei der ersten gewerteten Handlung einer Person je Monat genau ein Ereignis mit ihrem Slot; weitere Handlungen im Monat erzeugen keine weiteren Ereignisse dieser Metrik. `member.active_day` folgt derselben Logik je Tag.

### 3.3 Emissionsregeln für alle Domänen

- Emission in derselben Transaktion wie die fachliche Änderung (A-006), mit Idempotenzschlüssel aus Fachereignis und Metrik.
- Korrektur oder Löschung des Auslösers erzeugt eine Gegenbuchung mit gleicher Menge und negativem Vorzeichen.
- Kein Ereignis enthält Anzeigenamen, Personen-IDs, Gruppenmitgliedschaften oder Messwerte einer Person; personennahe Metriken verwenden den Slot.
- Emission ist unabhängig vom Entitlement-Zustand: Testphase und aktiv emittieren gleich; Metering entscheidet über die Bewertung.

## 4. Preisregeln, Pläne und Rahmen (A-072)

### 4.1 Preisregel

| Feld | Inhalt |
|---|---|
| geltung | Tenant oder Partner |
| modul, metrik | Bezug |
| modell | siehe 4.2 |
| parameter | modellabhängig |
| währung | EUR |
| periode | monatlich, quartalsweise, jährlich, einmalig |
| gültig_von, gültig_bis | Gültigkeit; Änderungen nur mit `gültig_von` in der Zukunft |
| priorität | Reihenfolge bei Überlagerung |

### 4.2 Modelle

| Modell | Parameter | Beispiel |
|---|---|---|
| `flat` | Betrag | Flatrate je Modul und Monat |
| `per_unit` | Betrag je Einheit | Betrag je aktivem Mitglied |
| `tiered` | Stufen, Preis innerhalb der Stufe | 1 bis 50, 51 bis 250, ab 251 mit fallendem Stückpreis |
| `volume` | Stufen, ein Preis für die gesamte Menge | ab 251 alle Einheiten zum niedrigsten Preis |
| `package` | Inklusivmenge, Grundpreis, Überschreitungspreis | Grundpreis inklusive 100 Mitglieder, darüber Stückpreis |
| `step` | Blockpreis | je begonnene 50 Mitglieder ein Betrag |
| `one_time` | Betrag | Einrichtung |
| `revenue_share` | Prozentsatz | Partnerprovision, Event-Vermittlung |
| `min_max` | Untergrenze, Deckel | Mindestbetrag je Monat, optionaler Höchstbetrag |
| `credit` | Betrag oder Prozentsatz, befristet | Pilotrabatt, Kulanz, Referenzkunden-Nachlass |

`min_max` und `credit` wirken als Modifikatoren auf das Planergebnis; `revenue_share` wirkt auf die Ebene darüber. Modelle sind je Plan frei kombinierbar; genau dieses Nebeneinander von Sitzplatz-, Flat-, Stück- und Provisionsabrechnung in einem Plan ist die Anforderung.

### 4.3 Preisplan

Ein Preisplan ist eine geordnete Menge von Regeln je Tenant. Er ist versioniert; jede Version hat `gültig_von` in der Zukunft oder zum nächsten Monatsersten; vergangene Perioden bleiben unveränderlich. Der Operator pflegt einen **Listenpreisplan** als Vorlage; individuelle Pläne entstehen durch Kopie und Anpassung. Alle Beträge sind Konfiguration; dieses Konzept legt keine Preise fest.

Ein Plan enthält je Tenant eine Untergrenze passend zum Bündel, damit kleine Betriebe nicht faktisch einen Festpreis zahlen und große Betriebe eine kalkulierbare Basis haben; die Werte sind Konfiguration.

### 4.4 Rahmen für Partner

Der Operator gibt jedem Partner einen **Preisrahmen**: je Metrik erlaubte Modelle sowie Unter- und Obergrenzen der Beträge, erlaubte Rabatte, Provisionssatz. Ein Partner legt Tenant-Pläne nur innerhalb des Rahmens an; die Verwaltung prüft bei Speichern. Rahmenänderungen wirken ab dem nächsten Monatsersten; bestehende Tenant-Pläne, die dann außerhalb liegen, werden dem Partner gemeldet und laufen zum übernächsten Monatsende aus, sofern er sie nicht anpasst.

### 4.5 Berechnung

- Mengen mit vier Nachkommastellen, Einzelpreise mit vier, Rundung kaufmännisch auf zwei Nachkommastellen **je Rechnungsposition**.
- Proratierung tagesgenau bei Buchung oder Kündigung mitten im Monat, auch für Flatrates: Tage im Zustand aktiv oder auslaufend geteilt durch Tage des Monats.
- Testtage werden nicht bewertet und nicht proratiert.
- Reihenfolge: Regeln nach Priorität, dann `min_max`, dann `credit`, dann Steuer.

## 5. Kostenvorschau und Verbrauchsdetail (A-073)

Der Bereich „Module und Kosten“ zeigt dem Tenant-Admin permanent:

| Anzeige | Inhalt | Regel |
|---|---|---|
| Laufender Monat | aufgelaufene Menge und Betrag je Regel, Gesamtsumme, Fortschritt im Monat | aus Tagesaggregaten, aktualisiert stündlich und bei jeder Buchung |
| Prognose Monatsende | Hochrechnung aus bisherigem Verlauf und den letzten drei Monaten | als Schätzung gekennzeichnet |
| Simulation vor Buchung | „Dieses Modul erhöht den laufenden Monat um X, den Folgemonat um Y“ | vor dem Klick auf Buchen (A-066) |
| Verbrauchsdetail | je Metrik und Tag aggregiert, mit Mengennachweis | **keine Ledger-Einzelzeilen**; bei personennahen Metriken ausschließlich die Summe |
| Stufenwarnung | „In 6 Tagen überschreitest du 250 aktive Mitglieder, dann sinkt der Stückpreis“ | Sprünge nach unten aktiv, nach oben zwingend |
| Testphase | „In der Testphase wären das X Euro gewesen“ | aus nicht bewerteten Ereignissen |
| Definitionen | Klartext jeder verwendeten Metrik, insbesondere „aktives Mitglied“ | immer sichtbar |
| Rechnungen | Liste, Status, PDF, CSV, XML | sieben Jahre |

Programm-Manager, Botschafter und Einsichtsrolle sehen keine Kosten. Die Einsichtsrolle sieht, welche Metriken vertraglich verwendet werden, ohne Beträge. Partner sehen je Tenant Mengen und Beträge ihrer Pläne sowie ihre Provision.

Tagesaggregate je Tenant, Metrik und Tag werden vom Worker aus dem Ledger geführt; sie sind die einzige Datenquelle für Oberflächen und Exporte des Tenants.

## 6. Rechnungslauf, Steuern, Zahlung und Mahnwesen (A-074)

### 6.1 Ablauf je Periode

1. **Versiegeln** am dritten Kalendertag um 03:00 Tenant-Zeit; Nachläufer gehen in die offene Periode.
2. **Bewerten:** Regeln in Prioritätsreihenfolge auf die Mengen, Proratierung, Modifikatoren.
3. **Positionen** erzeugen: jede Position verweist auf Regel, Metrik, Menge, Mengennachweis (Tagesaggregat) und gegebenenfalls Nachlauf-Kennzeichnung.
4. **Steuer** je Position nach Steuerregel des Tenant-Landes (Abschnitt 6.2), Rundung je Position.
5. **Rechnungsentwurf** mit fortlaufender Nummer je Betreiber-Rechtsträger und Jahr; Entwürfe sind änderbar, nummerierte Rechnungen nicht.
6. **Freigabe** durch einen Operator-Admin; je Tenant kann der Operator automatische Freigabe aktivieren.
7. **Versand** als PDF mit Verbrauchsübersicht, CSV und XML nach EN 16931 an die Rechnungsadresse aus den Stammdaten; Benachrichtigung an Tenant-Admins; Ablage in der Verwaltung.
8. **Zahlungseingang** bucht der Operator; ein späterer Kontoauszugsimport ist ein Betriebswerkzeug ohne Vertragsänderung.

Korrekturen nach Versand erfolgen ausschließlich als Gutschrift oder Nachbelastung mit Verweis auf die Ursprungsrechnung, nie durch Änderung.

### 6.2 Steuern

Steuerregeln pflegt der Operator: Steuersatz je Land des Tenants, Reverse Charge für Unternehmer in anderen EU-Staaten mit gültiger UID, Drittlandregel. Die UID wird bei Anlage erfasst und vom Operator geprüft; die Steuerregel wird je Tenant gesetzt und protokolliert. Die Plattform gibt keine Steuerberatung; die Regeln sind Konfiguration des Betreibers.

### 6.3 Zahlung

Zahlungsweg ist die Überweisung mit Zahlungsziel 14 Tage ab Rechnungsdatum; Bankverbindung und Verwendungszweck stehen auf der Rechnung. Es gibt keinen Zahlungsdienstleister, keine Kartenzahlung und keine Lastschrift.

### 6.4 Mahnwesen und Sperre

| Tag nach Fälligkeit | Schritt | Automatisch |
|---|---|---|
| 7 | Zahlungserinnerung per E-Mail an Rechnungskontakt und Tenant-Admins | ja |
| 21 | Mahnung mit Hinweis auf mögliche Sperre | ja |
| 45 | Sperrvorschlag an den Operator | Vorschlag ja, Sperre nein |

Die Sperre setzt ein Operator-Admin bewusst (A-033). Während der Sperre laufen Flatrates weiter; nutzungsabhängige Metriken entstehen nicht. Entsperren erfolgt nach Zahlungseingang durch den Operator.

### 6.5 Sonderfälle

| Fall | Regel |
|---|---|
| Buchung mitten im Monat | tagesgenau proratiert, Flatrates ebenfalls |
| Kündigung mitten im Monat | berechnet bis Monatsende; Zugang bis Monatsende; abhängige Module gleich (A-066) |
| Testphase | Ereignisse erfasst, nicht bewertet; Übergang in aktiv proratiert ab Testende |
| Nachträgliche Preisänderung | nur mit `gültig_von` in der Zukunft |
| Korrektur | Gegenbuchung im Ledger; nach Versand Gutschrift oder Nachbelastung |
| Doppelzählung | über Idempotenzschlüssel ausgeschlossen |
| Modul gekündigt, Daten in 90-Tage-Frist | Speichermetrik läuft weiter bis zur Löschung, transparent ausgewiesen |
| Verdiente Belohnung | `workshop.credit_redeemed` mit 0 Euro, protokolliert |
| Tenant gekündigt | Schlussrechnung zum Kündigungstermin; Speicher bis zur Löschung nach 90 Tagen |
| Gesperrter Tenant | Flatrates weiter, keine nutzungsabhängigen Mengen |

## 7. Partnerabrechnung (A-075)

Je Partner ist eines von zwei Modellen konfiguriert:

| Modell | Rechnung an Tenants | Rechnung an Partner | Provision |
|---|---|---|---|
| **Direktabrechnung** | ja, nach Tenant-Plan | nein | monatliche Provisionsgutschrift über `revenue_share` auf die Summe der Tenant-Rechnungen, nach Zahlungseingang der Tenants |
| **Sammelabrechnung** | nein | eine Rechnung über alle Tenants mit Positionen je Tenant | keine; der Partner fakturiert seine Tenants selbst |

In beiden Modellen sieht der Partner je Tenant Mengen und Beträge seiner Pläne (A-037), nie Verbrauchsdetails unterhalb der Tagesaggregate. Wechsel des Modells wirkt ab dem nächsten Monatsersten. Bei Auflösung eines Partners werden seine Tenants auf Direktabrechnung mit dem Operator umgestellt.

## 8. Schnittmengen dieses Themas

| Schnittmenge | Festlegung |
|---|---|
| Metering ↔ Entitlements | Entitlements liefern Zustandsereignisse; Metering bewertet Testphase nicht; Kostenvorschau und Simulation speisen den Buchungsablauf; Entitlements prüfen nie Zahlungen |
| Metering ↔ Fortschritt | aktives Mitglied aus gewerteten Handlungen mit Quelle selbst oder Plattform; automatische Tageswerte zählen nicht |
| Metering ↔ Datenschutz | Slots je Periode gesalzen, Salz nach sieben Tagen vernichtet; Verbrauchsdetail nur aggregiert; Einsichtsrolle ohne Beträge; sieben Jahre Aufbewahrung; Metering-Kenntnis der Quote im Auftragsverarbeitungsvertrag |
| Metering ↔ Organisation | Stammdaten liefern Rechnungsadresse, Land, UID; Tenant-Zustände steuern Schlussrechnung und Sperre; Partnermodell je Partner |
| Metering ↔ Challenges, Feed und Inhalte, Benachrichtigungen, Zugang, Arena, Workshops | Emission in derselben Transaktion mit Idempotenzschlüssel; Gegenbuchung bei Korrektur |
| Metering ↔ Betrieb | Ledger partitioniert je Periode; Periodensalz in OpenBao; Rechnungs-PDF aus Tokensatz der Plattform, nicht des Tenants |
| Metering ↔ Benachrichtigungen | Rechnung verfügbar, Stufenwarnung, Erinnerung und Mahnung als Kategorie Verwaltung |

## 9. Nachweise

1. Ein Beitrag zweimal übertragen: ein Ledger-Ereignis; Korrektur erzeugt Gegenbuchung; Summen stimmen.
2. Dieselbe Person mit zehn Handlungen im Monat: genau ein `member.active_month`-Ereignis; im Folgemonat ein neuer, nicht verkettbarer Slot; nach Salzvernichtung keine Rückrechnung möglich.
3. Öffnen der App, Login, Push und automatischer Tageswert erzeugen kein aktives Mitglied; ein Kommentar ebenfalls nicht.
4. Beispielplan mit `tiered`, `flat`, `per_unit`, `revenue_share`, `min_max` und `credit` liefert eine Rechnung, deren Positionen einzeln nachrechenbar sind; Rundung je Position.
5. Buchung am 16. eines Monats mit 30 Tagen: Flatrate zur Hälfte; Kündigung am 10.: voller Monat, Zugang bis Monatsende.
6. Testphase: Ereignisse erfasst, Betrag null, Anzeige „wären X Euro gewesen“; Übergang proratiert.
7. Ereignis nach Versiegelung: in offener Periode mit Nachlauf-Kennzeichnung; versiegelte Periode unverändert.
8. Verbrauchsdetail liefert über Oberfläche, API und Export nur Tagesaggregate; bei aktiven Mitgliedern nur Summen.
9. Partner im Rahmen: Plan außerhalb der Grenzen wird abgelehnt; Direkt- und Sammelabrechnung erzeugen die richtigen Rechnungen und Gutschriften.
10. Mahnlauf: Erinnerung Tag 7, Mahnung Tag 21, Sperrvorschlag Tag 45, keine automatische Sperre.
11. Rechnung als PDF, CSV und EN-16931-XML mit fortlaufender Nummer; Korrektur nur als Gutschrift.
12. Einsichtsrolle sieht verwendete Metriken ohne Beträge; Programm-Manager sieht keine Kosten.
