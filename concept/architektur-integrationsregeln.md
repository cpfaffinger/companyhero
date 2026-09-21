# CompanyHero – Zuständigkeiten und Integrationsregeln

**Stand:** 20.09.2026  
**Status:** Verbindliche Aufgabenabgrenzung gemäß A-003, A-004, A-005, A-008, A-009 und A-013 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung und keine technisch ausgeführte Abnahme.  
**Bezug:** [Backend](architektur-backend.md), [Frontend](architektur-frontend.md), [Domänen und Schnittmengen](domaenen-und-schnittmengen.md).

## 1. Geltungsbereich

Dieses Dokument legt fest, welche Schicht welche Aufgabe besitzt, und löst die Stellen auf, an denen zwei Schichten dieselbe Aufgabe übernehmen könnten. Es wählt keine Produkte und keine Paketversionen. Die Regeln gelten für Mitglieder-App, Verwaltung und Kiosk.

## 2. Zuständigkeitsmatrix

| Aufgabe | Zuständige Schicht | Abgrenzung |
|---|---|---|
| Buttons, Felder, Checkboxen, Auswahlfelder, Tabs, interaktive Tabellen | Angular Material über gemeinsame Verwendungsmuster | Keine gleichwertigen Parallelkomponenten aus einem weiteren UI-Kit |
| Dialoge, Menüs, Tooltips, Snackbar, Bottom Sheets | Material/CDK | Kein zweites Overlay- oder Fokus-System |
| Seitenraster, äußere Abstände und responsive Anordnung | Eigenes komponentenlokales CSS mit Grid/Flexbox | Kein Eingriff in private Material-Strukturen |
| Feed-Karten, Abzeichen, Fortschritt und Routen | Gemeinsame CompanyHero-Komponenten mit CSS/SVG | Keine wiederholten Implementierungen je Feature |
| Farben, Schriftrollen, Größen, Abstände, Radien und Bewegung | Ein zentrales Design-System | Komponenten und Bibliothek konsumieren dieselben Rollen |
| Palettenableitung, Kontrastprüfung, Tokensatz je Tenant | Backend (A-013) | Keine Ableitung im Frontend |
| Tenant-/Plattformmarke und Hell-/Dunkelmodus | Ein Theme-Service im Frontend | Keine eigene Moduswahl pro Komponente |
| Eingaben, Validierung, dirty/touched | Typed Reactive Forms | Keine zweite beschreibbare Kopie in Signals oder ngModel |
| Lokale Ansichtssteuerung und abgeleitete Anzeigewerte | Signals/computed | Keine mehrfachen Kopien desselben Serverdatenbestands |
| HTTP, Abbruch, Debouncing und asynchrone Abläufe | RxJS in der Datenzugriffsschicht | Keine zusätzlichen Request-Effekte am gemeinsamen Zugriff vorbei |
| API-Aufrufe und übertragene DTOs | Generierter TypeScript-Client aus OpenAPI | Keine manuell duplizierten API-Interfaces |
| Wiederverwendbare Serverdaten im Client | Eine fachliche Datenzugriffsschicht je Bereich | Komponenten verwenden deren Aktionen und Ergebnisse |
| Persönliche Offline-Daten und Schreibaufträge | Ein dediziertes Offline-Repository | Kein zweiter Puffer im Service Worker; nicht am Kiosk |
| Vorgangskennungen am Kiosk | Backend reserviert vor dem Absenden | Kein clientseitig erzeugter Schlüssel am Kiosk |
| Idempotenzschlüssel für Offline-Beiträge | Client erzeugt im Offline-Repository | Serverseitige Deduplikation im selben Namensraum wie Vorgangskennungen |
| Berechtigungen, Entitlements, Punkte und Abrechnung | ASP.NET-Core-Backend | Frontend-Darstellung ist keine Autorisierung |

Schlanke gemeinsame Wrapper und Kompositionen sind erlaubt; es entsteht keine abstrakte Kopie aller Material-APIs. Die gemeinsame UI-Bibliothek enthält weder direkte HTTP-Aufrufe noch fachliche Zugriffsentscheidungen. Für einfache statische Datentabellen genügt semantisches HTML.

## 3. Gestaltung

### K01 – Material-Defaults und Firmenbranding

CompanyHero besitzt eine semantische Tokenquelle mit CSS-Variablen wie `--ch-primary`, `--ch-surface` und `--ch-progress`. Ein zentraler Adapter bindet sie an die öffentlichen Material-Theming-Schnittstellen. Eigene Komponenten verwenden sie direkt. Es gibt keine rohen Markenfarben je Screen.

Die Werte stammen aus dem versionierten Tokensatz, den das Backend je Tenant ableitet, prüft und speichert. Mitglieder-App, Admin-Vorschau und serverseitig erzeugte Dokumente verwenden denselben Tokensatz. Ungültige Konfiguration führt zum geprüften Standard-Theme. Quelle zum Material-Theming: [Material Theming](https://github.com/angular/components/blob/main/guides/theming.md).

### K02 – Eigenes CSS und Material-Innenleben

Eigenes CSS steuert Layout auf eigenen Containern. Material steuert Rahmen, Innenabstände, Fokus, Fehler- und Disabled-Zustände seiner Bedienelemente. Anpassungen erfolgen über öffentliche Material-Theming-APIs. Kein `::ng-deep`, keine Selektoren auf private `.mat-*`- oder `.mdc-*`-Strukturen, kein `!important` als Reparaturstrategie.

Eine besondere Form wird als gemeinsame CompanyHero-Komponente umgesetzt, die Standardinteraktionen komponiert. Falls Material eine benötigte Funktion nicht über öffentliche APIs erfüllt, wird genau für diese Lücke eine CDK-basierte Lösung dokumentiert.

### K03 – CSS-Grundregeln

Es gibt eine kleine globale Basis für Dokument, Box-Sizing, Grundschrift, Hintergrund und ungestylte Inhaltssemantik. Keine pauschalen globalen Regeln für Buttons oder Inputs. Material-Theming wird zentral genau einmal ausgegeben. Feature-CSS bleibt komponentenlokal; globale Sonderfälle für Overlays sind auf benannte öffentliche Panel-Klassen begrenzt.

Die Kaskade wird nicht durch stärkere Selektoren oder nachträgliche Overrides repariert. Das betroffene Element wird von seiner zuständigen Schicht gestaltet.

### K04 – Styling-Buildkette

Die Angular-Buildkette verarbeitet normales CSS und die zentrale Material-SCSS-Themedatei. Es gibt keinen weiteren CSS-Verarbeitungsschritt. CSS-Variablen sind das gemeinsame Laufzeitformat; Sass bleibt auf die Theme-Erzeugung begrenzt. Keine pro Tenant gebauten Bundles.

### K05 – Dark Mode, Firmenwechsel und Overlays

Ein Theme-Service verwaltet `system | light | dark` und den aktiven Markenkontext. Daraus entsteht genau ein effektiver Modus; Tokens und `color-scheme` werden gemeinsam gesetzt. Nur bei `system` wirkt ein Wechsel der Betriebssystempräferenz. Komponenten fragen den Systemmodus nicht unabhängig ab.

Dasselbe Theme gilt für Dialoge, Menüs und andere Overlays. Beim Wechsel zwischen Firma und Arena werden kontextgebundene Overlays kontrolliert geschlossen und offene Änderungen zuvor behandelt. Der Markenwechsel ändert nicht die authentifizierte Mitgliedschaft.

### K06 – Firmenfarbe und Lesbarkeit

Die Saatfarbe des Tenants wird immer angenommen. Das Backend setzt sie in allen Rollen ein, in denen sie die Kontrastprüfung besteht, und ersetzt sie sonst durch die nächstliegende zulässige Ableitung derselben Farbe. Fehler-, Warn- und Erfolgssemantik sind plattformweit festgelegt. Tenant-Konfiguration erlaubt weder freies CSS noch Kontrast-Ausnahmen. Vorschau, Standardbedienelemente und eigene Komponenten verwenden dieselben Werte.

### K07 – Material-Dichte und CompanyHero-Maße

Eine gemeinsame Definition enthält das 4-px-Abstandsraster, Inter und Inter Tight aus eigener Auslieferung sowie die Layoutgrenzen 640 px und 1024 px. CSS und erforderliche TypeScript-Breakpoint-Abfragen beziehen ihre Werte aus derselben Definition. Layout ist grundsätzlich CSS-Aufgabe.

Die Mindestbedienflächen von 48 × 48 px und 56 × 56 px im Großflächenmodus haben Vorrang vor kompakter Material-Dichte. Dichte reduziert umgebenden Leerraum und darf Bedienflächen nicht verkleinern. Gemessene Geometrie entscheidet; ein eingestellter Dichtewert ist kein Nachweis.

### K08 – Interaktionsimplementierung

Material ist der Standard, CDK ergänzt echte Lücken. Eigene Checkbox-Kacheln komponieren die freigegebene Checkbox-Interaktion. Es gibt keine zusätzliche Bibliothek für Menüs, Dialoge oder Formulare. Fokus und Tastatursteuerung gehören dem jeweiligen Bedienelement; Seitenstruktur, Beschriftung und Textalternativen der Angular-Oberfläche.

### K09 – Animationen und Bildsprache

Material steuert seine eigenen Übergänge. Eigene Komponenten verwenden gemeinsame Bewegungswerte; dieselbe Eigenschaft wird nicht parallel animiert. Reduzierte Bewegung wird in beiden Bereichen berücksichtigt und geprüft. Icons sind einheitlich die selbst ausgelieferten Material Symbols Rounded; `mat-icon` ist ein Darstellungsweg dafür, kein zweiter Icon-Stil.

## 4. Zustand und Backend-Anbindung

### K10 – Formularmodell

Typed Reactive Forms besitzen den bearbeitbaren Formularzustand. Für dieselben Formulare werden weder ngModel noch Signal Forms noch externe Formularbibliotheken eingesetzt. Signals dürfen daraus Werte ableiten, führen aber keine zweite beschreibbare Kopie. Beim Submit wird ausdrücklich auf das DTO abgebildet; deaktivierte Felder sind nicht automatisch berechtigt oder zu übertragen. Quelle: [Typed Forms](https://angular.dev/guide/forms/typed-forms).

### K11 – Signals, RxJS und doppelte Requests

Signals halten lokale Ansichtsentscheidungen, computed die Ableitungen; RxJS verarbeitet asynchrone Ströme in der Datenzugriffsschicht. Die Umwandlung geschieht an dokumentierten Grenzen einmal je gemeinsam verwendeter Datenquelle. `toSignal` abonniert sofort und wird nicht wiederholt für dieselbe Quelle angelegt. Quelle: [RxJS Interop](https://angular.dev/ecosystem/rxjs-interop).

Jeder fachliche Bereich hat einen gemeinsamen Serverdatenzugriff. Komponenten beziehen Ergebnisse daraus und ändern sie über benannte Aktionen. Es gibt kein weiteres globales State- oder Query-Framework.

### K12 – C#-DTOs, TypeScript-Kopien und Laufzeitvalidierung

C#-API-Verträge werden als OpenAPI exportiert; daraus entsteht der TypeScript-Client. Generierter Code wird nicht von Hand geändert. Viewmodels sind über ausdrückliche Mapper zulässig. CI prüft Vertragsänderung, Generierung und die Verbraucher.

TypeScript ersetzt keine Prüfung eingehender JSON-Daten. Kritische importierte oder lokal gespeicherte Daten werden beim Einlesen validiert. API-Änderungen berücksichtigen installierte ältere PWAs: additive Änderungen bevorzugt, inkompatible Änderungen mit Übergang oder Versionierung.

### K13 – Zahlen, Zeiten und Fachberechnung

Verbindliche Beträge, Punkte, Rangfreigaben und Buchungsergebnisse kommen vom Backend. Kritische Buchungsvorschauen werden dort berechnet. Clientanzeigen sind keine zweite fachliche Wahrheit.

Präzise Dezimalwerte werden als kulturunabhängige Dezimalstrings mit expliziter Einheit oder Währung übertragen. Identifikatoren und große Ganzzahlen außerhalb des sicheren JavaScript-Bereichs werden als Strings übertragen. Rechen- und Rundungsregeln bleiben in der zuständigen Backend-Domäne; keine Umwandlung präziser Tarife in `number`.

Zeitpunkte besitzen einen expliziten Offset oder UTC. Reine Kalendertage bleiben Datumstrings ohne Zeitzonenverschiebung. Die fachliche Zeitzone von Tagesgrenzen ist Teil des Vertrags. Null, fehlender Wert und Nullzahl bleiben unterscheidbar.

Clientvalidierung liefert schnelle Hinweise; der Server prüft unabhängig und liefert stabile Fehlerkennungen.

### K14 – Navigation und Autorisierung

Die UI bildet serverseitig gelieferte Entitlements und Berechtigungen ab. ASP.NET Core prüft jede Operation unabhängig: Tenant, Rolle, Sichtbarkeit und Modulzugang. Eine ausgeblendete Schaltfläche, ein Header oder ein Firmen-Theme ist keine Zugriffssicherung. Dies gilt auch bei Offline-Synchronisierung.

## 5. Offline, Kiosk, Build und Qualität

### K15 – Persönliche Caches, Schreibwarteschlangen und Kiosk

Der Service Worker verwaltet App-Shell und ausdrücklich freigegebene statische Ressourcen. Persönliche API-Antworten und Schreibaufträge werden dort nicht gecacht. Ein dediziertes Offline-Repository besitzt den freigegebenen persönlichen Bestand aus A-008, getrennt nach Tenant und Person: eigene ungesendete Beiträge, die zur Erfassung nötigen Challenge-Definitionen, der zuletzt bekannte Kollektivstand der eigenen Challenges mit Zeitstempel und das eigene Minimalprofil. Quelle: [Angular Service Worker Configuration](https://angular.dev/ecosystem/service-workers/config).

Offline erfasste Beiträge tragen einen clientseitig erzeugten Idempotenzschlüssel, der bei Wiederholungen unverändert bleibt. Das Backend prüft aktuelle Berechtigungen und dedupliziert im selben Namensraum wie Vorgangskennungen. Updates lesen wartende Aufträge kompatibel oder migrieren sie. Es gibt keine Erfolgsmeldung vor der Serverbestätigung; lokal gespeicherte Beiträge haben einen eigenen Status.

Bei Abmeldung auf einem persönlichen Gerät werden ungesendete Aufträge angezeigt: synchronisieren, Abmeldung abbrechen oder ausdrücklich verwerfen. Sitzungsablauf überträgt nichts unter einem anderen Konto. Vor einer anderen Sitzung werden persönliche lokale Daten entfernt.

Am Kiosk gilt A-005: keine persönliche Offline-Warteschlange, Vorgangskennung vom Backend vor dem Absenden, unbestätigte Eingaben nur im Sitzungsspeicher, Löschung beim Ende der Personensitzung. Die Gerätesitzung des Kiosks gewährt keine Personenrechte; die Personensitzung endet nie nach der Gerätesitzung. Fehlende Verbindung und ausstehende Bestätigung sind sichtbar.

### K16 – Buildkette und Laufzeit

Es gilt die Angular-CLI-Buildkette; intern verwendete Buildwerkzeuge werden über Angular konfiguriert. Node dient dem containerisierten Build und den Frontend-Tests. ASP.NET Core ist das einzige Anwendungsbackend; die Plattform wird clientseitig ausgeliefert. Der Angular-Service-Worker verwaltet die App-Shell; es gibt keinen parallel registrierten zweiten Service Worker.

### K17 – Verwaltung und mobiles Ladebudget

Gemeinsame UI- und Vertragspakete, getrennte Feature-Einstiege und Lazy Loading verhindern eine Auslieferung der Verwaltung an Mitglieder. Das Budget von 180 KB komprimiertem JavaScript im ersten Ladevorgang der Mitglieder-App wird in CI gemessen. Eine Budgetänderung wird als Entscheidung dokumentiert. Ein oder zwei Build-Ausgaben bleiben eine Deploymententscheidung.

### K18 – Browser- und Paketanforderungen

Eine gemeinsame Browsermatrix erfüllt die Anforderungen der gewählten Angular- und Material-Versionen und der benötigten PWA-Funktionen. Material und CDK werden kompatibel zu Angular versioniert; Lockfile und Containerbuild machen die Kombination reproduzierbar.

### K19 – Test Driven Development und Testaussagekraft

Akzeptanzfälle werden vor der Implementierung festgelegt. Verhaltenstests verwenden zugängliche Rollen und öffentliche Test-Harnesses. Visuelle Tests ergänzen Theme- und Layoutprüfungen. API-Integration gegen das echte Backend ergänzt Mocks. Private Material-Klassen oder vollständige Markup-Snapshots sind kein Fachvertrag. Typen und Templates werden zusätzlich zur Testausführung geprüft.

## 6. Geplante Abnahme

Nachweise für den technischen Durchstich:

| Prüfung | Erwartetes Ergebnis |
|---|---|
| Referenzformular, Select und Dialog | Einheitliches Theme, korrekte Fehler- und Fokuszustände |
| Zwei Firmenmarken und Plattformmarke, hell/dunkel | Gemeinsamer Wechsel einschließlich Overlays; Farbwerte identisch mit Backend-Tokensatz und PDF |
| Systemmodus und expliziter Modus | Betriebssystemwechsel wirkt nur bei `system` |
| Layoutgrenzen und Browser-Zoom | Keine widersprüchlichen Layoutwechsel; Inhalte bleiben bedienbar |
| Standard- und Großflächenmodus | Tatsächliche Bedienflächen erfüllen die Produktvorgaben |
| Tastatur und reduzierte Bewegung | Zugängliche Bedienung und unterdrückte unnötige Animationen |
| Formularänderung und Submit | Eine führende Eingabequelle, korrektes DTO und Serverfehler |
| API-Änderung und Dezimalwerte | Erkennbare Vertragsbrüche, keine stillen Präzisionsverluste |
| Mehrere Verbraucher derselben Daten | Kein unkontrollierter doppelter Datenzugriff |
| Offline-Retry und Rechteentzug | Keine doppelte Wirkung; Ablehnung sichtbar; Kollektivstand mit Altersangabe |
| Kiosk-Vorgang mit verlorener Antwort | Genau ein Beitrag; Wiederaufnahme nach erneuter Anmeldung mit Kennung und PIN |
| Logout, Sitzungsablauf, Kiosk-Personenwechsel | Keine Daten einer vorherigen Person; kein Senden unter anderem Konto |
| Produktionsbuild der Mitglieder-App | Keine Admin-Features im Einstieg; Ladebudget gemessen |

Beim Projektaufbau werden automatisierbare Grenzen als Lint-, Abhängigkeits- und CI-Prüfungen umgesetzt: keine konkurrierenden UI-Pakete, keine privaten Material-Selektoren, keine manuell geänderten generierten Clients, keine Palettenableitung im Frontend.
