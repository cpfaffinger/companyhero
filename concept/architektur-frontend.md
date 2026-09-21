# CompanyHero – Frontend-Architektur

**Stand:** 20.09.2026  
**Status:** Beschlossen gemäß A-002, A-003, A-004, A-008, A-009 und A-013 im [Entscheidungsregister](architektur-entscheidungen.md). Noch kein ausführbarer Prototyp.  
**Bezug:** [Backend-Architektur](architektur-backend.md), [Zuständigkeiten und Integrationsregeln](architektur-integrationsregeln.md), [Domänen und Schnittmengen](domaenen-und-schnittmengen.md).

## 1. Sprache und Framework (A-002)

Das CompanyHero-Frontend wird mit Angular und TypeScript entwickelt. Mitglieder-App, Verwaltung und Kiosk entstehen aus einer Codebasis mit gemeinsamen UI-Bausteinen und getrennten Feature-Einstiegen.

Die Begründung ist die einheitliche Anwendungsstruktur, die Typprüfung einschließlich Templates und Formularen und die gute Prüfbarkeit für agentische Entwicklung mit Test Driven Development. Die Entscheidung behauptet keine allgemeine Überlegenheit von Angular bei Coding-Agenten.

Die Angular-Major-Version wird beim Projektaufbau auf die aktuelle stabile Version festgelegt und per Lockfile fixiert. Material und CDK folgen derselben Major-Version. Die Browsermatrix ergibt sich aus den Anforderungen dieser Versionen und der benötigten PWA-Funktionen und wird beim Projektaufbau dokumentiert.

## 2. UI-Basis und Styling (A-003)

Angular Material ist die alleinige Standard-UI-Bibliothek. Eigenes komponentenlokales CSS übernimmt Seitenlayout und produktspezifische Darstellung. Sass wird nur dort verwendet, wo die Material-Theming-API es benötigt. Gemeinsame Design-Tokens verbinden Standard- und Produktkomponenten. Tailwind, weitere UI-Kits und Utility-CSS-Systeme werden nicht eingesetzt; es gibt keinen zusätzlichen Styling-Buildschritt.

Angular CDK dient als Interaktionsgrundlage für begründete Lücken; es entsteht keine zweite Komponentenbibliothek. Material-Anpassungen erfolgen ausschließlich über öffentliche Schnittstellen.

Angular Material allein definiert kein vollständiges Produktlayout. Eigenes CSS bleibt für Seitenaufteilung und spezifische Komponenten erforderlich; diese Zuständigkeiten sind ohne weitere Styling-Bibliothek abgedeckt.

## 3. Gestaltung: eine eindeutige Aufteilung

| Aufgabe | Zuständigkeit |
|---|---|
| Buttons, Eingabefelder, Auswahlfelder, Dialoge und Standardinteraktionen | Angular Material |
| Ergänzende Interaktionsgrundlagen bei echten Lücken | Angular CDK |
| Responsive Seitenraster und äußere Abstände | Eigenes komponentenlokales CSS mit Grid/Flexbox und Media Queries |
| Challenge-Karten, Feed, Abzeichen und Fortschrittsdarstellungen | Gemeinsame CompanyHero-Komponenten mit eigenem CSS/SVG |
| Markenfarben, Typografie, Abstände, Radien und Größenstufen | Gemeinsames Design-System mit semantischen CSS-Variablen |
| Aktive Tenant-/Plattformmarke und Hell-/Dunkelmodus | Ein zentraler Theme-Service |

Material wird über seine öffentlichen Theming-Schnittstellen gestaltet. Eigene CSS-Regeln greifen nicht in private Material-Strukturen ein: keine pauschalen Button- oder Input-Overrides, kein `::ng-deep`, kein `!important` als Reparaturstrategie.

Das Material-Theming erzeugt CSS-Variablen für Farbe, Typografie und Dichte. Die CompanyHero-Tokenrollen werden zentral darauf abgebildet; eigene Komponenten verwenden dieselben semantischen Werte. Quelle: [Angular Material Theming](https://github.com/angular/components/blob/main/guides/theming.md).

Beispiel: Ein eigener CSS-Grid-Container ordnet zwei Material-Formularfelder nebeneinander an. Das Grid besitzt Abstand und Spalten; Material besitzt Rahmen, Beschriftung, Fokus und Fehlerzustand der Felder.

## 4. Firmenbranding und Produktdesign

Gestaltungsgrundlage: ruhige Gestaltung, sichtbarer Fortschritt als Hauptfigur jedes Screens, wechselnde Firmenmarke, selbst ausgelieferte Schrift und barrierearme Bedienung nach WCAG 2.2 AA.

Das Backend liefert je Tenant einen versionierten Tokensatz für hell und dunkel (A-013). Das Frontend enthält keine Palettenableitung und keine Kontrastprüfung; es bildet den Tokensatz auf CSS-Variablen mit dem Präfix `--ch-` ab. Ein Adapter bindet diese an die öffentlichen Material-Theming-Schnittstellen; eigene Komponenten konsumieren sie direkt. Die Rollen umfassen unter anderem `primary`, `surface`, `on-surface`, `progress` und `team`.

Tenant-Konfiguration liefert Markenwerte, kein freies CSS. Semantische Fehler-, Warn- und Erfolgsfarben sowie Typografie, Abstände, Radien und Navigationsstruktur sind plattformweit festgelegt. Alle Overlays verwenden den aktiven Markenkontext. Der Wechsel in die Arena setzt das Plattform-Theme, ohne die Mitgliedschaft zu ändern.

Material bringt eine eigene Formensprache mit. Für Standardbedienelemente wird sie innerhalb der öffentlichen Anpassungsmöglichkeiten akzeptiert. Individuelle Produktflächen werden eigenständig gestaltet.

## 5. Typisierte Verbindung zum Backend (A-009)

Planungsstandard: `C#-API-Verträge → OpenAPI → generierter TypeScript-Client → Angular`.

API-DTOs werden nicht manuell als TypeScript-Interfaces dupliziert. Frontend-Viewmodels werden ausdrücklich daraus abgeleitet. ASP.NET Core unterstützt OpenAPI; der Clientgenerator wird im Durchstich festgelegt. Quellen: [ASP.NET Core OpenAPI](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0), [NSwag](https://github.com/RicoSuter/NSwag).

TypeScript ersetzt keine Laufzeitvalidierung. Berechtigungen und verbindliche Fachberechnungen bleiben beim Backend. Austauschformate für Dezimalwerte, Kalenderdaten, Zeitpunkte und die beiden Idempotenzregime sowie der Umgang mit älteren PWA-Versionen sind im Vertrag beschrieben; die Regeln stehen im [Integrationsdokument](architektur-integrationsregeln.md).

## 6. Anwendungsstruktur, Offline und Tests

**Struktur:** Ein Angular-Stack und gemeinsame UI-Bausteine für Mitglieder-App, Verwaltung und Kiosk. Verwaltungsfeatures werden nicht in das erste Ladepaket der Mitglieder-App importiert; getrennte Feature-Einstiege und Lazy Loading trennen die Bereiche. Ob eine oder zwei Build-Ausgaben ausgeliefert werden, ist eine Deploymententscheidung ohne Einfluss auf die Codestruktur.

**Zustand:** Typed Reactive Forms besitzen den Formularzustand; Signals steuern lokale Ansichten und abgeleitete Werte; RxJS bearbeitet asynchrone Abläufe in der Datenzugriffsschicht. Es gibt keine parallelen beschreibbaren Modelle derselben Daten. Quellen: [Typed Forms](https://angular.dev/guide/forms/typed-forms), [RxJS Interop](https://angular.dev/ecosystem/rxjs-interop).

**Offline (A-008):** Der Angular-Service-Worker verwaltet App-Shell und freigegebene statische Ressourcen. Ein dediziertes Offline-Repository hält den freigegebenen persönlichen Bestand: eigene ungesendete Beiträge, die zur Erfassung nötigen Challenge-Definitionen, den zuletzt bekannten Kollektivstand der eigenen Challenges mit Zeitstempel und das eigene Minimalprofil. Der Kollektivstand wird mit Altersangabe dargestellt. Der Kiosk verwendet dieses Repository nicht; dort liegen unbestätigte Eingaben nur im Sitzungsspeicher.

**Build:** Die Angular-CLI-Buildkette ist der Standard. Node wird für containerisierte Builds und Tests benötigt; es gibt keinen Node-Anwendungsserver.

**Ladebudget:** Die Mitglieder-App hat ein CI-geprüftes Budget von 180 KB komprimiertem JavaScript im ersten Ladevorgang. Es wird am Referenzscreen gemessen; eine Budgetänderung ist eine dokumentierte Entscheidung, keine Nebenwirkung der Frameworkwahl.

**Tests:** Tests prüfen sichtbares Verhalten und öffentliche Schnittstellen über zugängliche Rollen und Test-Harnesses. Visuelle Tests ergänzen Firmenbranding, Layout und Material-Anpassungen. Compiler- und Templateprüfung sowie echte API-Integration sind eigenständige Nachweise. Vitest für Komponententests und Playwright für Ende-zu-Ende-Tests sind der Werkzeugstandard; Versionen werden beim Projektaufbau fixiert. Quellen: [Angular Testing](https://angular.dev/guide/testing), [Playwright TypeScript](https://playwright.dev/docs/test-typescript).

## 7. Referenzscreen

Vor der breiten Umsetzung wird ein Referenzscreen mit Formular, eigener Challenge-Karte und Dialog geprüft: zwei Firmenmarken und die Plattformmarke aus dem Backend-Tokensatz, hell und dunkel, mobil und Desktop, Tastaturbedienung, Großflächenmodus und reduzierte Bewegung. Dabei werden Ladevolumen und Anpassungsaufwand gemessen. Die Prüfliste steht in Abschnitt 6 des [Integrationsdokuments](architektur-integrationsregeln.md).
