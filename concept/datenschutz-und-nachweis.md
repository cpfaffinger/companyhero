# CompanyHero – Datenschutz und Nachweis

**Stand:** 20.09.2026  
**Status:** Beschlossen gemäß A-020 bis A-026 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung.  
**Domäne:** Datenschutz und Nachweis, mit der Querschnittsregel Sichtbarkeit und k-Anonymität für alle lesenden Domänen gemäß [Domänenkarte](domaenen-und-schnittmengen.md).  
**Bezug:** [Zugang und Identität](zugang-und-identitaet.md), [Backend](architektur-backend.md).

Dieses Dokument beschreibt, was die Plattform technisch und organisatorisch sicherstellt, damit ein Kunde seine Compliance herstellen kann. Es enthält keine Rechtsberatung; die rechtliche Bewertung im Einzelfall trifft der Kunde mit Datenschutzbeauftragtem und Betriebsrat.

## 1. Rollenverteilung und Datenart (A-020)

### 1.1 Rollen

| Rolle | Wer | Umfang |
|---|---|---|
| Verantwortlicher | der Firmen-Tenant | entscheidet über Zweck und Mittel des Programms in seinem Betrieb |
| Auftragsverarbeiter | der Betreiber von CompanyHero | verarbeitet weisungsgebunden; Auftragsverarbeitungsvertrag je Tenant; offengelegte Unterauftragsverarbeiterliste |
| Verantwortlicher für die Arena | der Betreiber von CompanyHero | eigene, abgegrenzte Verarbeitung der Arena-Aggregate; Tenants erhalten ein eigenes Informationsblatt |

### 1.2 Datenart

Aktivitäts-, Schlaf- und Übungsdaten werden durchgehend als Gesundheitsdaten behandelt, unabhängig von ihrer Bezeichnung in der Oberfläche. Der Schutz liegt in der Architektur: Der Arbeitgeber erhält sie nie. Stimmungs- und Belastungsdaten sowie Ergonomie-Ergebnisse gelten zusätzlich als besonders schutzwürdig und werden feldverschlüsselt gespeichert.

### 1.3 Betreiberdaten und Rechtstexte als Konfiguration

Der Rechtsträger des Betreibers ist keine Vorbedingung des Konzepts, sondern eine Einstellung. Der Operator pflegt in der Operator-Konsole:

- Betreiberdaten: Firmierung, Anschrift, Vertretungsberechtigte, Kontakt, Datenschutzbeauftragter, Registerangaben
- Rechtstexte: Impressum, Datenschutzerklärung, Auftragsverarbeitungsvertrag als Vorlage, Unterauftragsverarbeiterliste, Arena-Informationsblatt, Nutzungsbedingungen

Rechtstexte sind versioniert mit Gültigkeitsbeginn. Die Mitglieder-App zeigt Impressum und Datenschutzerklärung unter „Über“ in der jeweils gültigen Version; die Verwaltung zeigt dem Tenant-Admin und der Einsichtsrolle den Auftragsverarbeitungsvertrag und die Unterauftragsverarbeiterliste. Eine neue Version eines Textes wird protokolliert. Platzhalter in Vorlagen werden aus den Betreiberdaten und den Tenant-Daten befüllt.

Die Plattform entsteht ohne konkreten Kunden. Anforderungen kommen ausschließlich aus diesem Konzept. Eine juristische Prüfung der Rechtstexte ist eine Aufgabe des Betreibers vor dem ersten Vertrag mit einem Tenant und kein Bestandteil des Produkts; das Produkt stellt sicher, dass die Texte jederzeit ohne Release austauschbar sind.

## 2. Nicht konfigurierbare Schutzregeln (A-021)

Diese Regeln sind Produkteigenschaften. Kein Tenant, keine Rolle und kein Operator kann sie ändern.

1. **Kein Arbeitgeber sieht individuelle Aktivitätswerte.** Das gilt für alle Rollen des Tenants einschließlich Tenant-Admin und für den Operator.
2. **Aggregate erst ab fünf beitragenden Personen** in der Auswertungsgruppe. Darunter wird nichts angezeigt: nicht gerundet, nicht ungenau, sondern nichts.
3. **Die Beteiligungsquote verlässt den Tenant nicht** in Richtung anderer Tenants und nicht in die Arena. Der Betreiber kennt sie als Auftragsverarbeiter, weil die Abrechnung auf aktiven Mitgliedern beruht; das steht im Auftragsverarbeitungsvertrag.
4. **Inaktivität ist für niemanden sichtbar.** Es existiert keine Abfrage, keine Sortierung und kein Export, der nicht teilnehmende Personen erkennbar macht.
5. **Stimmungs- und Belastungsdaten werden nie aggregiert**, auch nicht anonymisiert. Sie bleiben ausschließlich bei der Person.
6. **Keine Körperbild-Metriken** in der Plattform: kein Gewicht, kein BMI, keine Körpermaße, keine Kalorienzufuhr, kein Körperfett. Nicht als Option, sondern nicht vorhanden.
7. **Freiwilligkeit ist nachweisbar:** Sichtbarkeitsstufe permanent sichtbar und mit einer Berührung änderbar; Austritt als Button; beides im Protokoll; prüfbar durch die Einsichtsrolle.
8. **Kein Nachteil für Nichtteilnahme:** keine individuellen geldwerten Belohnungen, kein Prämien-Shop. Belohnungen sind kollektiv und gelten auch Nichtteilnehmenden.
9. **Wearable-Rohdaten verlassen den Vault nicht** und werden von keinem Dritten und keiner KI verarbeitet.
10. **Datenminimierung im Beitritt:** kein Klarname, keine E-Mail, kein Geburtsdatum, keine Körpermaße als Voraussetzung. Profilangaben nur, wenn eine freigeschaltete Funktion sie benötigt, und dann bei der Person.

## 3. Sichtbarkeitsmodell (A-022)

### 3.1 Stufen

| Stufe | Wirkung |
|---|---|
| **Nur für mich** | Nichts ist für andere sichtbar. Beiträge zählen anonym in Kollektivziele. |
| **Mein Team** | Anzeigename und Beiträge sind für die eigenen Gruppen sichtbar. |
| **Ganze Firma** | zusätzlich firmenweite Ranglisten und Feed-Sichtbarkeit. |

Die Wahl erfolgt beim Beitritt ausdrücklich aus drei gleichrangigen Kacheln ohne Voreinstellung, in der Reihenfolge von restriktiv nach offen und mit neutralen Beschreibungen. Die Stufe ist als Chip in der Profilansicht dauerhaft sichtbar und mit einer Berührung änderbar; Änderungen wirken sofort und rückwirkend auf alle Anzeigen.

### 3.2 Zusätzliche Zustimmungen

- **Arena:** Die Sichtbarkeit einzelner Beiträge in der Arena ist eine separate Zustimmung der Person. Sie wird nur wirksam, wenn zusätzlich der Tenant-Admin Einzelränge für diese Arena erlaubt hat. Fehlt eines von beidem, zählt der Beitrag nur in den Pro-Kopf-Wert der Firma.
- **Buddy-Paar:** Zwei Personen geben sich gegenseitig Serie und Fortschritt frei. Beidseitig zugestimmt, jederzeit einseitig mit sofortiger Wirkung widerrufbar, wirksam ausschließlich zwischen diesen zwei Personen. Es ist die einzige Ausnahme von der Regel in 3.3.
- **Foto-Belege:** Fotos zu Beiträgen sind höchstens teamsichtbar, nie für Admin, Operator oder Arena.

### 3.3 Regel für alle Anzeigen

**Die restriktivere Einstellung gewinnt.** Eine firmenweit sichtbare Challenge zeigt eine Person mit „Nur für mich“ nicht; ihr Beitrag zählt anonym in das Kollektivziel. Kein Admin und keine Challenge-Konfiguration kann das übersteuern. Jede lesende Domäne wendet die zentrale Sichtbarkeitsprüfung an; es gibt keine Filterlogik je Feature.

### 3.4 Anzeigenamen und Mitgliederliste

Der Tenant-Admin sieht eine Mitgliederliste mit Anzeigename, Gruppen, Rolle und Beitrittsdatum, um Rollen zu verwalten und missbräuchliche Konten zu entfernen. Die Liste enthält keine Aktivitäts-, Fortschritts- oder Anmeldedaten und keine Sortierung, die Aktivität verrät. Programm-Manager, Botschafter und Redakteur sehen keine Mitgliederliste.

## 4. Aggregation und k-Anonymität (A-023)

### 4.1 Mindestzahl

Jedes Aggregat mit Personenbezug in der Grundgesamtheit wird nur ausgegeben, wenn mindestens **fünf Personen mit Beitrag** enthalten sind. Das gilt für Beteiligungsquoten, Kollektivstände in Verwaltungsansichten, Ergonomie-Aggregate, Arena-Werte und das Verbrauchsdetail der Abrechnung.

### 4.2 Schutz gegen Differenzbildung

- Auswertungsgruppen sind ausschließlich die vom Tenant gepflegten Gruppen je Dimension und der ganze Tenant. Keine frei wählbaren Filter, keine Kombination zweier Dimensionen.
- Zeiträume sind Challenge-Zeitraum, Kalenderwoche und Kalendermonat. Keine feineren Schnitte in Verwaltungsansichten.
- Wird eine Gruppe umgebaut oder eine Person wechselt die Gruppe, werden Aggregate der betroffenen Gruppen für den laufenden Zeitraum nur ausgegeben, wenn beide Zustände die Mindestzahl erfüllen.
- Kollektivbalken in der Mitglieder-App zeigen unter fünf sichtbaren Beitragenden nur den gerundeten Prozentwert, keine Einzelbeiträge.
- Exporte für den Tenant enthalten ausschließlich Aggregate, die dieselbe Prüfung bestanden haben.

### 4.3 Ranglisten

Eine Rangliste wird nur gerendert, wenn mindestens fünf Personen mit Beitrag in der Auswertungsgruppe sichtbar sind **und** mindestens 40 Prozent der aktivierten Personen dieser Gruppe einen Beitrag geleistet haben. Darunter zeigt die Plattform den Kollektivfortschritt. Bezugsgröße der 40 Prozent sind aktivierte Personen, nie die vom Admin gepflegte Sollstärke.

### 4.4 Arena

In die Arena gelangen je Tenant: Firmenname und Logo, der Pro-Kopf-Wert bezogen auf die gemeldete Sollstärke, die Größenklasse und der Aktualisierungszeitpunkt. Niemals: Beitragssummen, Teilnehmerzahlen, Beteiligungsquote, Team-Werte, Klarnamen, E-Mail-Adressen oder Wearable-Daten. Einzelwerte nur pseudonym und nur mit der doppelten Zustimmung aus 3.2. Ein Tenant mit weniger als fünf beitragenden Personen erscheint als „nimmt teil“ ohne Wert und ohne Rang.

## 5. Datenkategorien, Fristen und Löschung (A-024)

### 5.1 Fristen

| Kategorie | Speicherort | Frist |
|---|---|---|
| Identität: Anzeigename, Anmeldewege, Gruppen, Rolle | Kern, tenantisoliert | bis Austritt, danach 30 Tage Löschfrist |
| Aktivitätsereignisse: Check-ins, Übungsabschlüsse, Beiträge | Kern | 24 Monate im Detail, danach Monatsaggregate je Person; Einzelereignisse gelöscht |
| Abgeleitete Werte: Tagespunkte, Zielerreichung, Serie, Abzeichen | Kern | bis Austritt |
| Wearable-Rohdaten | Vault | 90 Tage, danach nur abgeleitete Tageswerte; bei Trennung des Geräts Löschung binnen 30 Tagen |
| Inhalte und Beiträge im Feed | Objektspeicher und Kern | bis Löschung durch die Person oder Austritt |
| Foto-Belege | Objektspeicher | 90 Tage nach Challenge-Ende oder früher durch die Person |
| Stimmungs-Check-in | Kern, feldverschlüsselt, nur für die Person lesbar | 12 Monate, jederzeit selbst löschbar |
| Ergonomie-Check | Kern, feldverschlüsselt, individuell nur für die Person | 12 Monate, jederzeit selbst löschbar; Tenant erhält nur Aggregate ab fünf |
| Arena-Aggregate | Arena-Dienst | Arena-Zeitraum plus 12 Monate, danach anonym |
| Zustimmungen: Sichtbarkeit, Arena, Buddy | Kern, unveränderlich | 3 Jahre; nach Austritt mit nicht rückführbarer Kennung |
| Prüfprotokoll: Konfigurations-, Rollen- und Zugangsänderungen | Kern, append-only | 3 Jahre |
| Sicherheitsprotokoll: Anmeldungen, Fehlversuche | Kern, pseudonymisiert | 12 Monate |
| Metering-Ereignisse | Ledger, ohne Personen-ID, periodisch gesalzene Zähler-Slots | 7 Jahre |
| Backups | verschlüsselt, außerhalb des Produktivhosts | höchstens 35 Tage |

### 5.2 Austritt

Der Austritt wirkt sofort auf Zugang und Sichtbarkeit und innerhalb von 30 Tagen auf die Löschung:

- gelöscht: Profil, Anmeldewege, eigene Feed-Beiträge, Fotos, Anerkennungen, Stimmungs- und Ergonomiedaten, Buddy-Verbindungen, Abzeichen, persönliche Einstellungen, Push-Abonnements
- anonymisiert: Beiträge zu abgeschlossenen und laufenden Kollektivzielen bleiben als Summenanteil ohne Personenbezug; individuelle Ranglisteneinträge werden entfernt
- behalten mit nicht rückführbarer Kennung: Zustimmungsprotokoll für 3 Jahre, Metering-Slots gemäß Ledger
- Arena: pseudonyme Einzelwerte werden entfernt; Pro-Kopf-Werte des Tenants bleiben

Der Austritt ist ein Button mit einer Bestätigung und einem Klartext dieser Folgen. Es gibt keine Rückfragekette und kein Support-Ticket.

### 5.3 Verwaiste Personen

Eine Person ohne Anmeldung und ohne erfasste Handlung über 24 Monate wird automatisch wie ausgetreten behandelt. Ist eine E-Mail hinterlegt, geht 30 Tage vorher ein Hinweis. Das schließt Personen ein, die alle Anmeldewege verloren haben.

### 5.4 Kündigung eines Moduls oder eines Tenants

Nach Kündigung eines Moduls bleiben dessen Daten 90 Tage für den Tenant-Export lesbar und werden danach zu anonymen Aggregaten verdichtet. Verdiente Abzeichen bleiben im Profil. Nach Kündigung des Tenants werden nach 90 Tagen alle tenantbezogenen Daten gelöscht; der Tenant-Schlüssel für feldverschlüsselte Daten wird sofort mit Ablauf der Frist vernichtet. Metering-Ledger und Zustimmungsprotokoll bleiben gemäß ihren Fristen.

### 5.5 Löschung in Backups

Backups werden höchstens 35 Tage aufbewahrt. Damit ist jede Löschung spätestens 35 Tage nach ihrer Ausführung auch in Backups wirksam. Ein Wiederherstellen aus einem Backup führt den Löschlauf für zwischenzeitlich gelöschte Daten erneut aus.

## 6. Nachweis, Auskunft und Zugriff (A-025)

### 6.1 Zustimmungsprotokoll

Jede Änderung der Sichtbarkeitsstufe, jede Arena-Zustimmung und jede Buddy-Freigabe wird mit Zeitpunkt, vorherigem und neuem Zustand unveränderlich protokolliert. Die Person sieht ihr eigenes Protokoll im Profil und erhält es im Export.

### 6.2 Prüfprotokoll

Append-only, ohne Personenbezug auf Mitglieder, mit Klarnamen der handelnden Funktionsrolle. Erfasst werden: Modulbuchungen und -kündigungen, Änderungen an Marke, Anmeldewegen, Gruppen und Sollstärken, Rollencode-Ausgabe und -Einlösung, Kiosk-Registrierung und -Widerruf, Arena-Beitritte und -Einstellungen, Exporte, Support-Zugriffe des Operators und Änderungen an Preisplänen durch den Operator.

### 6.3 Einsichtsrolle

Die Einsichtsrolle liest: aktive Module, geltende Sichtbarkeitsregeln und Schwellen, was der Tenant-Admin sehen kann und was nicht, die tenantweite Beteiligungsquote ab fünf beitragenden Personen, den Auftragsverarbeitungsvertrag, die Löschfristen und das vollständige Prüfprotokoll. Sie sieht keine Inhalte, keine Person und keinen Einzelwert. Sie kann nichts ändern.

### 6.4 Auskunft und Export

Jede Person exportiert ihre Daten selbst aus dem Profil: maschinenlesbar und als lesbare Übersicht. Der Export enthält alle Daten mit Personenbezug über alle Domänen, das Zustimmungsprotokoll und die Liste der Anmeldewege ohne Geheimnisse. Er enthält keine Daten anderer Personen und keine Buddy-Daten der anderen Seite. Personen ohne eigenes Gerät übertragen ihr Konto zunächst per Kiosk-QR auf ein Gerät.

Der Tenant exportiert Konfiguration, Gruppen, Sollstärken, Challenge-Definitionen, Aggregate nach 4.1 und sein Prüfprotokoll. Er exportiert nie Mitgliederdaten.

### 6.5 Support-Zugriff des Operators

Zugriff auf Tenant-Konfiguration nur mit Vorgangsnummer, zeitlich auf 24 Stunden begrenzt, protokolliert und für die Einsichtsrolle sichtbar. Kein Zugriff auf individuelle Aktivitätswerte, Nachrichten, Stimmungs- oder Ergonomiedaten und nie auf den Vault. Zugriffe des Operators auf Mitgliederdaten für Fehleranalysen erfolgen nur über pseudonyme Kennungen in Logs, nie über Oberflächen.

### 6.6 Gepflegte Nachweisdokumente

Verfahrensverzeichnis, Beschreibung technischer und organisatorischer Maßnahmen, Auftragsverarbeitungsvertrag, Unterauftragsverarbeiterliste, Arena-Informationsblatt und ein Beiblatt für den Betriebsrat werden als versionierte Artefakte in der Operator-Konsole gepflegt (Abschnitt 1.3) und mit jeder Änderung der hier festgelegten Regeln aktualisiert.

## 7. Technische Schutzmaßnahmen (A-026)

**Speicherung und Verschlüsselung**
- Alle Daten mit Personenbezug liegen in der EU. Anonyme Inhalts-Assets wie Videos und Bilder ohne Personenbezug dürfen über ein internationales CDN ausgeliefert werden; Mitgliederdaten und Vault nie.
- Verschlüsselung im Ruhezustand für alle Speicher; TLS 1.3 und HSTS im Transport.
- Feldverschlüsselung für Stimmungs-Check-ins, Ergonomie-Ergebnisse, Wiederherstellungscodes und Vault-Inhalte mit einem Datenschlüssel je Tenant. Schlüssel liegen in einem vom Datenbankzugang getrennten Geheimnisspeicher; Schlüsselvernichtung ist eine wirksame Löschmethode.
- Medien in tenantpräfigierten Pfaden, Zugriff nur über kurzlebige signierte URLs; keine öffentlichen Buckets.

**Anwendung**
- Strikte Content-Security-Policy, keine Inline-Skripte, keine Fremdhosts für Skripte oder Schriften; Schriften und Icons werden selbst ausgeliefert.
- Keine Personendaten in URLs, Query-Strings, Push-Nutzlasten oder Fehlerberichten. Push enthält nur eine Referenz; der Client löst die Anzeige auf.
- Datei-Uploads: Typprüfung am Inhalt, Neukodierung von Bildern, Entfernen aller Metadaten einschließlich Standort, Virenprüfung, Größenlimits.
- Ratenbegrenzung und Bot-Schutz auf Beitritts-, Anmelde- und Kiosk-Routen gemäß Zugangsthema.

**Betrieb**
- Logs, Metriken und Traces ohne Personenbezug; pseudonyme Kennungen; keine Nutzdaten in Fehlerberichten. Keine externen Telemetrie- oder Analysedienste.
- Entwicklung, Staging und Produktion mit getrennten Daten und Geheimnissen; nie Produktionsdaten in Staging, auch nicht anonymisiert.
- Backups verschlüsselt, Wiederherstellung regelmäßig geübt, Aufbewahrung höchstens 35 Tage.

**Prozess**
- Bedrohungsmodellierung je Domäne vor der Umsetzung und bei jeder Funktion, die Sichtbarkeit erweitert.
- Abhängigkeits-Scan und automatisierte Sicherheitsprüfungen in CI.
- Isolationstest in CI: Für jede API-Route erzeugt eine Sitzung von Tenant A auf Ressourcen von Tenant B eine Antwort „nicht gefunden“, nicht „verboten“.

## 8. Schnittmengen dieses Themas

| Schnittmenge | Festlegung |
|---|---|
| Datenschutz ↔ alle lesenden Domänen | Sichtbarkeit und Mindestzahl sind eine zentrale Leseregel; Feed, Challenges, Ranglisten, Verwaltungsansichten und Exporte verwenden dieselbe Prüfung |
| Datenschutz ↔ Zugang | Sichtbarkeitswahl ist Pflichtschritt im Beitritt; Austritt löst Zugangslöschung sofort und Datenlöschung binnen 30 Tagen aus; verwaiste Personen nach 24 Monaten |
| Datenschutz ↔ Metering | Ledger ohne Personen-ID, periodisch gesalzene Slots, Verbrauchsdetail nur aggregiert und ab fünf |
| Datenschutz ↔ Arena | nur Pro-Kopf-Wert, Größenklasse, Name, Logo, Zeitpunkt; Einzelwerte nur bei doppelter Zustimmung; kein Wert unter fünf |
| Datenschutz ↔ Wearable-Vault | 90 Tage Rohdaten, nur abgeleitete Tageswerte nach außen, keine Verarbeitung durch Dritte oder KI |
| Datenschutz ↔ Benachrichtigungen | Push ohne Inhalt; Re-Engagement kollektiv formuliert; Abwesenheitsmodus unterdrückt alles |
| Datenschutz ↔ Betrieb | EU-Speicherung, Backup-Frist 35 Tage, Geheimnisspeicher getrennt von der Datenbank; Produkte gemäß [Betrieb](betrieb.md): OpenBao, Garage, pgBackRest, Beobachtung ohne Fremddienste |

## 9. Nachweise

1. Ein Tenant-Admin erhält über keine Oberfläche, keine API und keinen Export individuelle Aktivitätswerte; die Mitgliederliste enthält keine Aktivitätsspalten.
2. Aggregate mit vier Beitragenden werden in Verwaltung, Export und Arena nicht ausgegeben; mit fünf schon. Gruppenumbau während eines Zeitraums erzeugt keine Differenz.
3. Eine Person mit „Nur für mich“ erscheint in keiner Rangliste und keinem Team-Stand; ihr Beitrag zählt im Kollektivziel. Buddy-Freigabe wirkt nur für den Buddy und endet sofort bei Widerruf.
4. Rangliste erscheint erst bei fünf Beitragenden und 40 Prozent der Aktivierten; darunter Kollektivbalken.
5. Austritt: nach 30 Tagen keine Personendaten mehr auffindbar; Kollektivsummen unverändert; Zustimmungsprotokoll ohne Rückführbarkeit; nach 35 Tagen auch in Backups nicht mehr vorhanden.
6. Stimmungs-Check-ins sind für keine Rolle außer der Person lesbar und in keinem Aggregat enthalten; Feldverschlüsselung nachweisbar durch Datenbankzugriff ohne Schlüssel.
7. Export einer Person enthält alle ihre Daten und keine fremden; Tenant-Export enthält keine Mitgliederdaten.
8. Einsichtsrolle sieht Prüfprotokoll und Regeln, keine Person; Support-Zugriff erscheint im Protokoll.
9. Upload eines Fotos mit Standortdaten: gespeicherte Datei enthält keine Metadaten; Foto ist für Admin nicht abrufbar.
10. Isolationstest über alle Routen: Antwort „nicht gefunden“ bei fremdem Tenant.
