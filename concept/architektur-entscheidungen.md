# CompanyHero – Entscheidungsregister

**Stand:** 26.09.2026  
**Status:** A-001 bis A-111 angenommen durch Christopher. Dokumentierte Entscheidungen; technisch nachgewiesene Nachweise tragen den Zusatz „technisch nachgewiesen“ mit Datum und Verweis auf das Abnahmeprotokoll unter `durchstich/abnahme/`.

Dieses Register führt die Beschlüsse. Die Themenexporte erläutern ihre Umsetzung: [Backend](architektur-backend.md), [Frontend](architektur-frontend.md), [Zuständigkeiten und Integrationsregeln](architektur-integrationsregeln.md), [Domänen und Schnittmengen](domaenen-und-schnittmengen.md), [Zugang und Identität](zugang-und-identitaet.md), [Datenschutz und Nachweis](datenschutz-und-nachweis.md), [Betrieb](betrieb.md), [Organisation und Mandanten](organisation-und-mandanten.md), [Challenges](challenges.md), [Fortschritt](fortschritt.md), [Feed und Inhalte](feed-und-inhalte.md), [Benachrichtigungen](benachrichtigungen.md), [Entitlements](entitlements.md), [Metering und Abrechnung](metering-und-abrechnung.md), [Marke und Theme](marke-und-theme.md), [Workshops und Events](workshops-und-events.md), [Arena](arena.md), [Wearable-Vault](wearable-vault.md), [Onboarding und Rollout](onboarding-und-rollout.md), [Verwaltung und Konsolen](verwaltung-und-konsolen.md), [Inhaltsmodule](inhaltsmodule.md), [Mehrsprachigkeit](mehrsprachigkeit.md), [Verzeichnis und Netzwerk](verzeichnis-und-netzwerk.md), [Technischer Durchstich](technischer-durchstich.md). Jede Entscheidung steht hier in ihrer gültigen Form. Wird eine Entscheidung geändert, wird der Eintrag ersetzt und die betroffenen Dateien werden gemeinsam angepasst.

## A-001 – Backend

- **Status:** angenommen, 20.09.2026.
- **Umfang:** C# mit ASP.NET Core auf .NET 10 LTS; alle eigenen Serverkomponenten unter Linux in Docker.
- **Begründung:** typisierte Fachlogik, ausdrückliche Projektgrenzen, integrierter Anwendungs-Testhost und gute automatisierte Prüfbarkeit für agentische Entwicklung mit Test Driven Development.
- **Folgen:** kein weiteres Anwendungsbackend in einer anderen Sprache. Datenbank, Anwendungsstruktur und Jobverarbeitung sind in A-011, A-012 und A-006 festgelegt.
- **Nachweise:** reproduzierbarer Containerbuild, Fachtests, API-Tests mit WebApplicationFactory und Isolationstests gegen echtes PostgreSQL gemäß [Backendplanung](architektur-backend.md).

## A-002 – Frontend

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Angular mit TypeScript für Mitglieder-App, Verwaltung und Kiosk aus einer gemeinsamen Codebasis mit getrennten Feature-Einstiegen.
- **Begründung:** einheitliche Anwendungsstruktur, Typ- und Templateprüfung einschließlich Formularen, testbare Komponenten.
- **Folgen:** Angular-CLI-Buildkette; Node ausschließlich als containerisiertes Build- und Testwerkzeug; kein Node-Anwendungsserver. Die Angular-Major-Version wird beim Projektaufbau auf die aktuelle stabile Version festgelegt und per Lockfile fixiert; Material und CDK folgen derselben Major-Version.
- **Nachweise:** `strict` und `strictTemplates`, Verhaltenstests, gemessenes Ladebudget gemäß [Frontendplanung](architektur-frontend.md).

## A-003 – Gestaltung

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Angular Material ist die alleinige Standard-UI-Bibliothek. Eigenes komponentenlokales CSS übernimmt Seitenlayout und produktspezifische Komponenten. Ein gemeinsames Design-System mit semantischen CSS-Variablen versorgt beide. Angular CDK ergänzt begründete Lücken. Tailwind, weitere UI-Kits und Utility-CSS-Systeme werden nicht eingesetzt.
- **Begründung:** eindeutige Zuständigkeiten, ein Styling-System, keine konkurrierenden Konventionen und keine zusätzliche Buildkette.
- **Folgen:** Material-Anpassungen nur über öffentliche Theming-Schnittstellen; keine Eingriffe in private Material-Strukturen; Sass nur für die zentrale Theme-Erzeugung.
- **Nachweise:** Referenzscreen mit Firmen- und Plattformmarke, hell/dunkel, Overlays, Tastatur und Mindestbedienflächen. **Technisch nachgewiesen** am 26.09.2026 (`durchstich/abnahme/stufe-5.md`, Nachweise 1 bis 8).

## A-004 – Zuständigkeiten für Zustand und Daten

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Typed Reactive Forms besitzen den Formularzustand, Signals die lokale Ansichtssteuerung, RxJS die asynchronen Abläufe in der Datenzugriffsschicht; je Fachbereich eine Datenzugriffsschicht. Berechtigungen und verbindliche Fachwerte entscheidet das Backend.
- **Begründung:** keine konkurrierenden schreibbaren Zustände und keine duplizierte Fachlogik.
- **Folgen:** kein zusätzliches globales State- oder Query-Framework. Details in [K10 bis K14](architektur-integrationsregeln.md).
- **Nachweise:** mehrere Verbraucher ohne unkontrollierte Doppelrequests, korrektes Submit-Mapping, serverseitige Autorisierung unabhängig von der Oberfläche.

## A-005 – Kiosk: Gerätesitzung, Personensitzung und Online-Erfassung

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Beiträge am Gemeinschaftsgerät werden online erfasst; es gibt keine persönliche Offline-Warteschlange am Kiosk.
- **Sitzungsmodell:** Ein Kiosk ist ein vom Tenant registriertes Gerät. Die **Gerätesitzung** wird über ein widerrufbares Gerätegeheimnis in einem Secure-/HttpOnly-Cookie geführt; sie ist an genau einen Tenant gebunden und gewährt keinerlei Personenrechte. Die **Personensitzung** entsteht durch Anmeldung mit Kiosk-Kennung und PIN innerhalb der Gerätesitzung, ist serverseitig geführt, endet nach 60 Sekunden ohne Eingabe (je Tenant 30 bis 120 Sekunden), spätestens nach 10 Minuten und niemals nach der Gerätesitzung. Details in A-018.
- **Vorgänge:** Vor dem Absenden eines Beitrags reserviert das Backend eine Vorgangskennung, gebunden an Tenant und Person. Wiederholungen nutzen dieselbe Kennung; gleiche Kennung mit abweichendem Inhalt wird abgelehnt. Speicherung und Idempotenznachweis sind atomar. Eine verlorene Antwort bedeutet „Bestätigung ausstehend“.
- **Wiederaufnahme:** Nach erneuter Anmeldung derselben Person sind offene Vorgänge serverseitig auffindbar; ihr Zustand wird vor erneuter Erfassung geklärt. Ein terminal abgebrochener Vorgang nimmt keinen verspäteten Beitrag mehr an.
- **Sitzungsende:** Persönliche Eingaben und Kennungen liegen nur im Sitzungsspeicher und werden beim Ende der Personensitzung entfernt; bestätigte Servervorgänge bleiben erhalten. Eine folgende Person sieht nichts aus der vorherigen Sitzung.
- **Nachweise:** Commit mit verlorener Antwort, erneute Anmeldung, verspäteter Request, parallele Wiederholung, Auto-Logout, Personenwechsel, Widerruf des Gerätegeheimnisses. Nachweisstand: Vorgangskennung mit verlorener Antwort, verspätetem Request nach Abbruch, paralleler Wiederholung und Bindung an Tenant und Person technisch nachgewiesen am 25.09.2026 (`durchstich/abnahme/stufe-3.md`); Sitzungsteile (Auto-Logout nach 60 Sekunden mit Tenant-Einstellung 30 bis 120, absolutes Ende nach 10 Minuten, Personenwechsel ohne Rest, Widerruf des Gerätegeheimnisses beendet Personensitzungen, Vorgangskennung nur aus der Kiosk-Personensitzung) technisch nachgewiesen am 26.09.2026 (`durchstich/abnahme/stufe-4.md`).

## A-006 – Jobverarbeitung: logische Queue in PostgreSQL

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Eine logische, zentrale und dauerhafte Job-Queue wird in PostgreSQL geführt. Sie liegt in derselben Datenbank wie die Fachdaten und wird von horizontal skalierbaren Worker-Replikaten desselben Images bedient. Es gibt keinen separaten Broker und keinen eigenständigen Dispatcher-Prozess.
- **Atomare Kopplung:** Der Job-Eintrag wird in derselben Datenbanktransaktion geschrieben wie die fachliche Änderung. Damit ist die Outbox-Garantie ohne Relay erfüllt: Kein bestätigter Fachvorgang verliert seine Folgearbeit, und kein Job existiert ohne den zugehörigen Fachvorgang.
- **Datenmodell:** Die Queue-Tabelle liegt im Plattformbereich ohne Tenant-RLS und enthält nur Routinginformationen: Job-ID, Tenant-ID, Jobtyp, fachliche Referenz, Ausführungszeitpunkt, Lease-Ablauf, Versuchszähler, Status und Idempotenzschlüssel. Fachliche Nutzdaten bleiben in tenantbezogenen Tabellen und werden vom Worker erst nach Setzen des Tenant-Kontexts geladen.
- **Verarbeitung:** Worker beanspruchen Jobs mit `SELECT … FOR UPDATE SKIP LOCKED` und setzen einen Lease. Jeder Job läuft in einem eigenen Tenant-Kontext. Wiederholungen mit wachsender Wartezeit, ein Maximum an Versuchen, danach Dead-Letter-Status mit kontrolliertem Replay. Bestätigung erst nach dauerhafter Verarbeitung; mehrfache Zustellung wird erwartet und fachlich über Eindeutigkeitsschlüssel dedupliziert.
- **Fairness:** Begrenzte Parallelität je Tenant und eine Beanspruchungsreihenfolge, die Tenants abwechselnd bedient, verhindern die Verdrängung kleiner Mandanten. Zeitgesteuerte Aufgaben werden über einen datenbankseitigen Sperrmechanismus gegen Doppelausführung geschützt.
- **Fachereignisse:** Ereignisse zwischen Modulen werden als tenantbezogene Ereigniszeilen gespeichert; die Zustellung an andere Module erfolgt als Job. Module abonnieren Ereignisse, nicht Tabellen anderer Module.
- **Erweiterungspfad:** Ein externer Broker wird nur eingeführt, wenn gemessener Durchsatz oder eine erforderliche Integration es verlangt. Der Wechsel betrifft dann ausschließlich den Transport hinter der Queue-Schnittstelle, nicht die Modulverträge.
- **Werkzeug:** eigene, bewusst kleine Umsetzung auf `SKIP LOCKED` in der Plattforminfrastruktur; Prüfung der Bibliotheken und Begründung in A-108.
- **Nachweise:** parallele Worker ohne Doppelwirkung, Absturz vor und nach Commit, abgelaufener Lease, falsche Tenant-Job-Zuordnung, Replay, Lastspitzen und Fairness über mehrere Tenants. Nachweisstand: atomare Kopplung, Kontext je Job, parallele Worker ohne Doppelwirkung, geordnetes Beenden, abgelaufener Lease, verlorener Lease, Dead-Letter mit Replay, Fairness über zwei Tenants, zeitgesteuerte Aufgabe ohne Doppelausführung technisch nachgewiesen am 25.09.2026 (`durchstich/abnahme/stufe-3.md`); Lastspitzen mit dem Lasttest der Abschlusskriterien (A-106).

## A-007 – Sitzung und Anmeldung

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Angular und ASP.NET-Core-API laufen unter derselben öffentlichen Origin. Die CompanyHero-Sitzung ist serververwaltet und wird über ein Secure-/HttpOnly-Cookie geführt. **Authentifizierung ist Basisfunktion der Plattform.** Dazu gehören gleichrangig: Anmeldung über externe Identitätsanbieter (Microsoft Entra ID, Google und weitere je Tenant konfigurierbare OpenID-Connect-Anbieter), Magic-Link per E-Mail sowie Passkey mit Wiederherstellungscode für Personen ohne E-Mail. Die Anmeldung über externe Anbieter ist kein zubuchbares Modul.
- **Begründung:** Onboarding jeder Kundenorganisation setzt funktionierende Anmeldung voraus; externe Identitätsprüfung und interne Sitzung lassen sich verbinden, ohne Provider-Tokens im Browser zu verwalten.
- **Protokoll:** OpenID Connect auf OAuth 2.0 mit Authorization Code Flow und PKCE. ASP.NET Core übernimmt Login-Redirect, Callback und Codeaustausch. Anbieter ohne OpenID Connect erhalten nur mit geprüftem anbieterspezifischem Identitätsadapter Zugang.
- **Claim-Minimierung:** Persistiert werden ausschließlich Issuer und Subject beziehungsweise ein geprüfter stabiler Anbieterschlüssel als Verknüpfung zur internen Person. E-Mail- und Namensclaims werden für den Anmeldevorgang verarbeitet und nicht gespeichert, es sei denn, die Person hinterlegt ihre E-Mail ausdrücklich für den Magic-Link. Der Anzeigename in der Plattform wird nie aus Anbieterclaims befüllt.
- **Identität und Mandant:** Keine automatische Kontoverknüpfung aufgrund gleicher E-Mail. Externe Anmeldung begründet weder Mitgliedschaft noch Rollen; beides wird separat geprüft. Mehrere Anmeldewege verweisen auf dieselbe interne Person, damit Offline-Beiträge nach erneuter Anmeldung zugeordnet bleiben.
- **Schutzgrenzen:** Etablierte Middleware prüft Signatur, Issuer, Audience, State und Nonce. Nur freigegebene Anbieterkonfigurationen und Redirect-Ziele. SameSite berücksichtigt den externen Callback; zustandsändernde Aufrufe erhalten CSRF-Schutz. Kein Cross-Origin-Credential-CORS. Keine Provider-Tokens und keine Sitzungsgeheimnisse in localStorage oder IndexedDB.
- **Betrieb:** Mehrere Backend-Instanzen nutzen einen gemeinsamen Sitzungsspeicher in PostgreSQL, persistente und geschützte Data-Protection-Schlüssel sowie serverseitigen Sitzungswiderruf.
- **Ausgestaltung:** Anbieterkonfiguration, Kontotypen, Laufzeiten, Kontoverknüpfung und Logout sind in A-015 bis A-017 festgelegt. Bibliotheken: A-111 (OIDC-Middleware und WebAuthn).
- **Nachweise:** Microsoft-, Google- und generischer OIDC-Login, fehlerhafte Callback- und Tokenwerte, fremder Tenant, Kontoverknüpfung, Claim-Minimierung im Datenbestand, CSRF, Logout/Widerruf und Instanzwechsel. **Technisch nachgewiesen** am 26.09.2026 (`durchstich/abnahme/stufe-4.md`): Login über Microsoft, Google und tenant-eigenen Anbieter mit ausschließlich Hash aus Issuer und Subject im Datenbestand, fehlerhafte Callback-Werte und Anbieter abgelehnt, Verknüpfung nur aus bestehender Sitzung, CSRF-Doppelcookie (Sitzung ohne passenden Header gilt als nicht angemeldet), Abmeldung aus allen Sitzungen und Callback über zwei API-Instanzen mit gemeinsamem Sitzungsspeicher und gemeinsamen Data-Protection-Schlüsseln in PostgreSQL.
- **Technische Quellen:** [Microsoft: OIDC mit Cookie-Sitzung](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0), [Google: OIDC](https://developers.google.com/identity/openid-connect/reference).

## A-008 – Begrenzte Offline-Erfassung auf persönlichen Geräten

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Nur freigegebene Offline-Aktionen und ein festgelegter lokaler Datenbestand. Verwaltung, Abrechnung und umfassende persönliche Historien bleiben online.
- **Freigegebener lokaler Datenbestand:** die eigenen ungesendeten Beiträge und Check-ins; die Challenge-Definitionen, die zur Erfassung nötig sind; der zuletzt bekannte Kollektivstand der eigenen Challenges als Tenant-Aggregat mit Zeitstempel, angezeigt mit Altersangabe; das eigene Minimalprofil für die Oberfläche; auf ausdrücklichen Wunsch lokal gespeicherte Inhaltsvideos (A-054). Keine Daten anderer Personen, keine Verwaltungsdaten, keine Authentifizierungstokens.
- **Offline-Freigabe:** zeitlich begrenzt und getrennt von der serverseitigen Sitzung. Nach Ablauf wird die persönliche Oberfläche gesperrt; wartende Beiträge bleiben bis zur erneuten Anmeldung derselben internen Person im selben Tenant oder zur ausdrücklichen Löschung erhalten. Beim Wiederverbinden werden aktuelle Rechte geprüft; Ablehnungen bleiben sichtbar.
- **Kontowechsel und Logout:** Wartende Beiträge werden angezeigt; Synchronisieren, Abbrechen oder bewusstes Verwerfen. Vor einer anderen persönlichen Sitzung werden lokale Daten entfernt. App-Updates migrieren wartende Beiträge oder lesen sie kompatibel. Trennung nach Identität und Tenant gilt auch zwischen Tabs.
- **Grenzen:** kein sofortiger Rechtewiderruf ohne Verbindung; lokale Sperre schützt nicht vor Zugriff auf das Browserprofil; lokale Speicherung ist kein Backup.
- **Ausgestaltung:** Offline-Dauer und Verhalten bei Geräteverlust sind in A-019 festgelegt.
- **Nachweise:** Ablauf, Wiederanmeldung, entzogene Rechte, Kontowechsel, parallele Tabs, Update-Migration, wiederholte Synchronisierung ohne Doppelwirkung, Altersanzeige des Kollektivstands.

## A-009 – Verbindlicher API-Vertrag und Idempotenz

- **Status:** angenommen, 20.09.2026.
- **Umfang:** C#-DTO → tatsächliches JSON → OpenAPI → generierter TypeScript-Client. Präzise Dezimalwerte als kulturunabhängige Strings mit expliziter Einheit oder Währung; Identifikatoren und große Ganzzahlen als Strings. Datum, Zeitpunkt, Zeitzone, Null und fehlendes Feld folgen [K13](architektur-integrationsregeln.md).
- **Zwei Idempotenzregime, ein Namensraum:** Für Online-Vorgänge mit Vorschau, insbesondere am Kiosk, reserviert das Backend eine **Vorgangskennung**. Für offline erfasste Beiträge erzeugt der Client einen **Idempotenzschlüssel** als zeitlich sortierbare eindeutige Kennung. Beide werden serverseitig im selben Eindeutigkeitsraum je Tenant und Person geführt. Gleiche Kennung mit abweichendem Inhalt wird abgelehnt. Der Vertrag benennt je Endpunkt, welches Regime gilt.
- **Folgen:** DTO-Serializer und Schema beschreiben dieselbe Darstellung. `strict` und `strictTemplates` sind verbindlich. Generierter Code wird nicht manuell geändert. Ältere installierte PWAs werden bei Vertragsänderungen berücksichtigt: additive Änderungen bevorzugt, inkompatible Änderungen mit Übergang oder Versionierung.
- **Offen:** fachliche Rundungsregeln je Domäne. Clientgenerator entschieden in A-109.
- **Nachweise:** echte API-Antworten durch den generierten Client prüfen; Präzision, Null/fehlend/Nullzahl, große Ganzzahlen, Datumswerte, beide Idempotenzregime und inkompatible Änderungen. Nachweisstand: beide Idempotenzregime in einem Namensraum je Tenant und Person serverseitig technisch nachgewiesen am 25.09.2026 (`durchstich/abnahme/stufe-3.md`); echte Antworten durch den generierten Client (Präzision als Strings, Null/fehlend, Datumswerte mit Offset, Idempotenzschlüssel aus dem Client) und erkennbare inkompatible Änderungen (Vertragstest rot, Gegenprobe) technisch nachgewiesen am 26.09.2026 (`durchstich/abnahme/stufe-5.md`); Clientgenerator: A-109.

## A-010 – Entscheidungsführung

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Jede Entscheidung erhält ID, Status, Umfang, Begründung, Folgen und Nachweise. Offene Detailfragen werden benannt und dem Themengebiet zugeordnet, in dem sie entschieden werden.
- **Begründung:** Agenten und Team müssen Beschlüsse von Vorschlägen unterscheiden können.
- **Folgen:** „angenommen“ bedeutet verbindlicher Plan, nicht technisch bewiesen. Der Ordner `concept` enthält ausschließlich den gültigen Stand; geänderte Entscheidungen werden ersetzt, nicht historisiert. Keine stillschweigende Annahme weiterer Technologieprodukte.
- **Nachweise:** Dokumentreview auf widerspruchsfreie Statusangaben und Verweise; technische Nachweise je Beschluss bis zur Umsetzung.

## A-011 – Datenbank und Mandantenmodell

- **Status:** angenommen, 20.09.2026.
- **Umfang:** PostgreSQL ist die Datenbank für Fachdaten, Job-Queue, Sitzungsspeicher und Ereignisse. Zielversion ist die aktuelle unterstützte Major-Version zum Projektstart, derzeit PostgreSQL 18, mit aktuellem Patchstand. Datenzugriff über EF Core mit Npgsql; gezieltes SQL für spezielle Operationen. Schema, Constraints und RLS-Policies werden über versionierte Migrationen geführt.
- **Mandantenmodell:** gemeinsame Datenbank und gemeinsame Tabellen mit `tenant_id NOT NULL` auf jeder tenantbezogenen Zeile, zusammengesetzte Fremdschlüssel über `tenant_id`, Row Level Security mit transaktionslokalem Kontext. Ein späterer Wechsel einzelner Großkunden auf getrennte Datenbanken bleibt über den zentralen Datenzugriff möglich, wird aber nicht vorab gebaut.
- **Begründung:** überwiegend relationale Daten, gemeinsame Transaktionen zwischen Modulen, ein Migrationspfad, geringe Betriebskosten für viele kleine Firmen.
- **Folgen:** Geldbeträge und Tarife mit definierter Dezimalpräzision, keine binäre Gleitkommaarithmetik. Konfigurierbare Challenge-Regeln dürfen versioniertes JSONB verwenden; Mitgliedschaften, Buchungen, Beiträge und Rechnungspositionen bleiben strukturierte Tabellen.
- **Nachweise:** siehe Abschnitt Nachweise der [Backendplanung](architektur-backend.md). Nachweisstand: Nr. 1 bis 3 und 9 (Isolation zweier Tenants mit RLS und Laufzeitrechten, Kontext je Request und Job, Tenant-Admin ohne Einzelwerte, Modulschemata) technisch nachgewiesen am 25.09.2026 (`durchstich/abnahme/stufe-2.md`); Nr. 4 bis 8 und 10 folgen in den Stufen 3 bis 5, Nr. 10 ist mit Stufe 1 bereits erbracht.

## A-012 – Anwendungsarchitektur: modularer Monolith

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Eine gemeinsam versionierte Backend-Codebasis mit fachlichen Modulen gemäß [Domänenkarte](domaenen-und-schnittmengen.md). HTTP-API und Worker sind zwei Prozesse aus derselben Codebasis und demselben Image, unabhängig skalierbar. Arena und Wearable-Vault sind eigene Dienste mit eigenem Datenspeicher.
- **Modulregeln:** Jedes Modul besitzt seine Tabellen und Schreiboperationen in einem eigenen Datenbankschema oder Namensraum. Andere Module greifen ausschließlich über öffentliche Anwendungsfunktionen oder Fachereignisse zu, nie über fremde Tabellen. Jedes Modul ist ein eigenes Projekt mit ausdrücklich erlaubten Abhängigkeiten; Architekturtests erzwingen die Abhängigkeitsrichtung. Innerhalb eines Moduls sind Schnittstelle, Anwendungsablauf, Fachregeln und Infrastruktur getrennt.
- **Begründung:** häufige gemeinsame Transaktionen zwischen Modulen, kleines Team, ein Deploymentpfad. Modulare Grenzen halten den späteren Herauslösungspfad offen, ohne Netzwerkgrenzen vorwegzunehmen.
- **Folgen:** kein Netzwerkdienst je Modul, kein allgemeines Repository-Framework, kein flächendeckendes Event Sourcing. Querschnitte erhalten benannte Eigentümer statt eigener Dienste.
- **Nachweise:** Architekturtests grün, Modulbau ohne Zugriff auf fremde Schemata, Herauslösung eines Moduls im Durchstich als Trockenübung dokumentiert. Technisch nachgewiesen am 25.09.2026 (`durchstich/abnahme/stufe-2.md`, Nachweise 13 bis 15 und Abschnitt 3).

## A-013 – Theme-Ableitung im Backend

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Die Ableitung der vollständigen Farbpalette aus der Tenant-Saatfarbe, die Kontrastprüfung nach WCAG 2.2 AA und die Ersetzung durchgefallener Rollen durch die nächstliegende zulässige Ableitung erfolgen ausschließlich im Backend. Ergebnis ist ein versionierter, gespeicherter Tokensatz je Tenant für hell und dunkel.
- **Begründung:** Mitglieder-App, Admin-Vorschau und serverseitig erzeugte Dokumente wie das Aushang-PDF benötigen dieselbe Ableitung. Eine einzige Implementierung vermeidet abweichende Farben zwischen Bildschirm und Druck.
- **Folgen:** Das Frontend konsumiert den Tokensatz und bildet ihn auf `--ch-*`-Variablen und die öffentlichen Material-Theming-Schnittstellen ab; es enthält keine Palettenableitung. Die Admin-Vorschau ruft die Ableitung über einen Backend-Endpunkt ohne Persistierung auf. Ungültige Eingaben führen zum geprüften Standard-Theme. Die Ableitung ist deterministisch und mit Referenzwerten getestet.
- **Umsetzung:** eigene Portierung des veröffentlichten Verfahrens, entschieden in A-110.
- **Nachweise:** identische Farbwerte in App, Vorschau und PDF für zwei Firmenmarken; Kontrastprüfung mit bekannten Grenzfällen; Standard-Theme bei ungültiger Eingabe. Nachweisstand: identische Werte in App und Vorschau, Kontrastprüfung mit Grenzfällen (Orange, Gelb, Grau, Schwarz, Weiß) und Standard-Theme bei ungültiger Eingabe technisch nachgewiesen am 26.09.2026 (`durchstich/abnahme/stufe-5.md`); das PDF folgt mit dem Aushang (Stufe 6, Benachrichtigungen) und verwendet denselben gespeicherten Tokensatz.

## A-014 – Beitritt und Rollenvergabe

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Beitritt ausschließlich über Beitrittscodes (acht Zeichen, QR-fähig, mehrere je Tenant, 180 Tage Standardgültigkeit, Nutzungslimit, Widerruf). Anzeigename frei wählbar und je Tenant eindeutig. Eine Person gehört zu genau einem Tenant; eine Login-Identität ist mit höchstens einer aktiven Person verknüpft. Rollen werden über personalisierte Einmalcodes vergeben (14 Tage, eine Rolle, ein Tenant). Funktionsrollen handeln unter Klarnamen und benötigen E-Mail oder externen Anbieter.
- **Begründung:** Beitritt ohne Firmen-E-Mail für jede Belegschaft; keine gespeicherte Zuordnung von Klarname zu Anzeigename für Mitglieder, mit der einzigen Ausnahme der freiwilligen, nach 30 Tagen gelöschten Klarname-Freigabe je Veranstaltungsanmeldung (A-084); einfache und prüfbare Isolation. Rollencodes werden außerhalb der Plattform oder für Funktionsrollen per E-Mail übergeben; Aussteller je Rolle gemäß [Zugang und Identität](zugang-und-identitaet.md), Abschnitt 2.3, einschließlich Operator-Admin und Partner-Admin für Tenant-Admin-Codes; Botschaftercodes tragen die Gruppen.
- **Folgen:** Firmenwechsel bedeutet Austritt und neuer Beitritt. Der Beitritt erzeugt Person, Mitgliedschaft, Gruppen, Kiosk-Kennung und Anmeldewege in einer Transaktion. Details in [Zugang und Identität](zugang-und-identitaet.md), Abschnitt 2.
- **Nachweise:** Beitritt ohne E-Mail unter 90 Sekunden bis zur ersten Handlung; Rollencode setzt Klarnamen; dieselbe Identität ist keiner zweiten aktiven Person zuordenbar. Nachweisstand: Beitritt ohne E-Mail mit Passkey und Wiederherstellungscode, Rollencode setzt den Klarnamen, dieselbe externe Identität ist keiner zweiten aktiven Person zuordenbar (plattformweiter Identitätsindex) technisch nachgewiesen am 26.09.2026 (`durchstich/abnahme/stufe-4.md`); die 90 Sekunden bis zur ersten Handlung misst Stufe 8 (Onboarding).

## A-015 – Anmeldewege und Anbieter

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Für Mitglieder gleichrangig verfügbar: externe Anbieter (Microsoft mit Arbeits-, Schul- und persönlichen Konten, Google, tenant-eigene OpenID-Connect-Anbieter im Self-Service mit Discovery-Validierung), Magic-Link (15 Minuten, einmalig), Passkey und Wiederherstellungscode. Tenant-Admin, Partner-Admin, Programm-Manager und Operator-Rollen melden sich nur mit Passkey oder externem Anbieter an; sensible Aktionen verlangen eine Anmeldung, die nicht älter als 15 Minuten ist.
- **Begründung:** Onboarding jeder Kundenorganisation ohne Vorbedingung; phishing-resistente Anmeldung für Rollen mit Verwaltungsrechten; keine unprüfbaren MFA-Claims.
- **Folgen:** plattformweiter Identitätsindex mit Hash aus Issuer und Subject; keine E-Mail- und Namensclaims im Datenbestand; Anbieter ohne OpenID Connect werden nicht generisch unterstützt.
- **Tenant-Hoheit:** Der Tenant bestimmt die Anmeldewege seiner Mitglieder: je Weg aktivieren oder deaktivieren, externe Anbieter erzwingen. Mindestens ein Weg außer dem Kiosk bleibt aktiv; Voreinstellung sind alle Wege. Bei erzwungenem Anbieter entfällt der Kiosk-Beitritt, die Kiosk-Nutzung bleibt. Für privilegierte Rollen bleibt Passkey immer erlaubt. Deaktivierte Wege bleiben für Personen, die nur diesen Weg besitzen, 30 Tage nutzbar. Details in [Zugang und Identität](zugang-und-identitaet.md), Abschnitt 3.4.
- **Nachweise:** Login über alle Wege; Magic-Link für Tenant-Admin abgelehnt; sensible Aktion ohne frische Anmeldung abgelehnt; Discovery-Validierung eines fehlerhaften Anbieters schlägt fehl; deaktivierter Weg nach 30 Tagen gesperrt; erzwungener Anbieter verhindert Kiosk-Beitritt, nicht Kiosk-Nutzung. **Technisch nachgewiesen** am 26.09.2026 (`durchstich/abnahme/stufe-4.md`).

## A-016 – Kontomodell und Wiederherstellung

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Person tenantbezogen; daran beliebig viele Passkeys, höchstens eine E-Mail, beliebig viele Anbieterverknüpfungen. Verknüpfen nur aus bestehender Sitzung durch vollständiges Durchlaufen des neuen Weges; keine automatische Verknüpfung. Wiederherstellungscode mit zwölf Zeichen, einmalig, nach Verwendung sofort erneuert. Keine Wiederherstellung durch Tenant-Admin oder Operator; bei Verlust aller Wege neuer Beitritt mit neuer Person.
- **Begründung:** Eine Admin-Wiederherstellung würde die Zuordnung Klarname zu Anzeigename erzeugen, die es nicht geben soll.
- **Folgen:** Verwaiste Personen werden gemäß A-024 nach 24 Monaten ohne Anmeldung und Handlung automatisch ausgetreten.
- **Nachweise:** Verknüpfung, Trennung, Login über Wiederherstellungscode mit Erneuerung, Ablehnung der Verknüpfung über gleiche E-Mail. **Technisch nachgewiesen** am 26.09.2026 (`durchstich/abnahme/stufe-4.md`); der letzte Weg bleibt, ein Wiederherstellungscode bleibt für Personen ohne E-Mail und Anbieter immer aktiv.

## A-017 – Sitzungslaufzeiten und Widerruf

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Mitglieder 30 Tage gleitend, 180 Tage absolut. Privilegierte Rollen 8 Stunden gleitend, 24 Stunden absolut. Kiosk-Gerätesitzung ohne Ablauf mit automatischer Rotation des Gerätegeheimnisses alle 30 Tage und sofortigem Widerruf. Kiosk-Personensitzung gemäß A-005. „Abmeldung aus allen Sitzungen“ in der Profilansicht; Rollenentzug, Austritt und Anbieterwiderruf beenden betroffene Sitzungen sofort.
- **Begründung:** lange Mitgliedssitzungen für Offline-Phasen, kurze Sitzungen für Verwaltungsrechte.
- **Folgen:** Sitzungsspeicher in PostgreSQL; Widerruf wirkt beim nächsten Request über alle Instanzen.
- **Nachweise:** Verlängerung, absolutes Ende, Widerruf über zwei API-Instanzen, Sitzungsende bei Rollenentzug. **Technisch nachgewiesen** am 26.09.2026 (`durchstich/abnahme/stufe-4.md`): gleitende Verlängerung 30 Tage, absolutes Ende 180 Tage, privilegierte Sitzung 8 und 24 Stunden, Abmeldung aus allen Sitzungen wirkt auf der zweiten API-Instanz beim nächsten Request, Rollencode beendet die Mitgliedssitzung.

## A-018 – Kiosk-Zugang

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Geräteregistrierung über einmaligen Registrierungscode (15 Minuten) aus der Verwaltung; Gerätegeheimnis als Cookie. Persönliche Anmeldung mit sechsstelliger zufälliger Kiosk-Kennung (auch als persönlicher QR-Code) und vierstelliger PIN; keine Namensliste am Gerät. Drosselung: fünf Fehlversuche sperren die Kennung für 15 Minuten, 20 Fehlversuche in 15 Minuten sperren das Gerät für 15 Minuten. Am Kiosk möglich: Check-in, Beitrag, Kollektivstand, Anmeldung zu Veranstaltungen (A-084), PIN-Änderung, Übertragung auf ein eigenes Gerät per einmaligem QR-Link (fünf Minuten). Kennung und PIN sind außerhalb einer Kiosk-Gerätesitzung kein Anmeldeweg.
- **Begründung:** Eine PIN allein identifiziert in einer Belegschaft niemanden eindeutig; eine Namensliste würde die Sichtbarkeitsregel verletzen; Kennung plus PIN mit Drosselung ist ausreichend für ein Gerät ohne Historie und Einstellungen.
- **Folgen:** Kiosk-Kennung wird für jede Person beim Beitritt erzeugt und ist in der App sichtbar; PIN-Neusetzung ohne eigenes Gerät über den Wiederherstellungscode am Kiosk.
- **Nachweise:** Beitritt am Kiosk, Übertragung per QR, Drosselung, Gerätewiderruf beendet Personensitzungen, keine Namensliste erreichbar. **Technisch nachgewiesen** am 26.09.2026 (`durchstich/abnahme/stufe-4.md`); Fehlversuche im Sicherheitsprotokoll nur pseudonymisiert.

## A-019 – Offline-Freigabe und Austritt

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Offline-Freigabe gilt 7 Tage ab dem letzten Serverkontakt aus einer Mitgliedssitzung; sie ist ein lokaler Ablaufzeitpunkt ohne Geheimnis. Nach Ablauf Sperre bis zur Online-Anmeldung; wartende Beiträge bleiben bis zur Synchronisierung derselben Person oder zur Löschung erhalten. Austritt beendet sofort alle Sitzungen und löscht alle Login-Identitäten, Wiederherstellungscodes, Passkeys, Kiosk-Kennung und PIN sowie den Eintrag im Identitätsindex; ein späterer Beitritt erzeugt eine neue Person.
- **Begründung:** begrenzte lokale Datenhaltung bei Geräteverlust; Austritt als ein Button ohne Rückführbarkeit.
- **Folgen:** Kiosk ohne Offline-Freigabe; Datenfolgen des Austritts gemäß A-024.
- **Nachweise:** Ablauf nach 7 Tagen, Synchronisierung nach Wiederanmeldung, keine Synchronisierung nach Austritt, erneuter Login mit derselben externen Identität führt in den Beitritt. **Technisch nachgewiesen** am 26.09.2026 (`durchstich/abnahme/stufe-4.md`); der Ablauf der Offline-Freigabe ist ein Client-Test (Vitest), Synchronisierung und Austritt sind Integrationstests.

## A-020 – Rollenverteilung und Datenart

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Der Firmen-Tenant ist Verantwortlicher, der Betreiber von CompanyHero Auftragsverarbeiter mit Vertrag je Tenant und offengelegter Unterauftragsverarbeiterliste. Für die Arena-Aggregate ist der Betreiber Verantwortlicher mit eigenem Informationsblatt. Aktivitäts-, Schlaf- und Übungsdaten werden durchgehend als Gesundheitsdaten behandelt; Stimmungs- und Ergonomiedaten zusätzlich feldverschlüsselt. Betreiberdaten und Rechtstexte (Impressum, Datenschutzerklärung, Auftragsverarbeitungsvertrag, Unterauftragsverarbeiterliste, Arena-Informationsblatt, Nutzungsbedingungen) sind versionierte Konfiguration in der Operator-Konsole, ohne Release austauschbar.
- **Begründung:** Die Plattform trifft keine Rechtsgrundlagenentscheidung; sie stellt Werkzeuge und Nachweise bereit. Der Schutz liegt in der Architektur, nicht in der Einordnung. Die Plattform entsteht ohne konkreten Kunden; der Rechtsträger wird nachträglich eingetragen.
- **Folgen:** Mitglieder-App zeigt Impressum und Datenschutzerklärung in der gültigen Version; Verwaltung und Einsichtsrolle sehen Auftragsverarbeitungsvertrag und Unterauftragsverarbeiterliste; jede neue Textversion wird protokolliert. Die juristische Prüfung der Texte ist Aufgabe des Betreibers vor dem ersten Tenant-Vertrag, kein Produktbestandteil.
- **Nachweise:** Rechtstexte lassen sich ohne Deployment versionieren und werden in App und Verwaltung in der gültigen Version angezeigt; Platzhalter werden korrekt befüllt.

## A-021 – Nicht konfigurierbare Schutzregeln

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Zehn Regeln als Produkteigenschaft, von keiner Rolle änderbar: keine individuellen Werte für den Arbeitgeber; Aggregate erst ab fünf Beitragenden; Beteiligungsquote verlässt den Tenant nicht; Inaktivität nirgends sichtbar; Stimmungsdaten nie aggregiert; keine Körperbild-Metriken; nachweisbare Freiwilligkeit; kein Nachteil für Nichtteilnahme; Wearable-Rohdaten bleiben im Vault; Datenminimierung im Beitritt.
- **Begründung:** Diese Regeln sind der Grund, warum das Produkt in Betriebe mit Betriebsrat kommt. Konfigurierbarkeit würde sie zur Verhandlungsmasse machen.
- **Folgen:** keine Schalter, keine Rollenausnahmen, keine Operator-Ausnahmen; die Einsichtsrolle kann die Regeln prüfen. Wortlaut in [Datenschutz und Nachweis](datenschutz-und-nachweis.md), Abschnitt 2.
- **Nachweise:** Tests für jede Regel über Oberfläche, API und Export.

## A-022 – Sichtbarkeitsmodell

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Drei Stufen: Nur für mich, Mein Team, Ganze Firma. Ausdrückliche Wahl beim Beitritt aus drei gleichrangigen Kacheln ohne Voreinstellung. Stufe dauerhaft sichtbar und mit einer Berührung änderbar, Wirkung sofort und rückwirkend. Arena-Einzelsichtbarkeit als separate Zustimmung der Person plus Freigabe des Tenant-Admins. Buddy-Paar als einzige Ausnahme von „die restriktivere Einstellung gewinnt“. Foto-Belege höchstens teamsichtbar. Tenant-Admin sieht eine Mitgliederliste ohne Aktivitätsdaten.
- **Begründung:** keine Voreinstellung vermeidet Nudging-Kritik bei gleichem Aufwand; zentrale Leseregel statt Filterlogik je Feature.
- **Folgen:** Jede lesende Domäne verwendet die zentrale Sichtbarkeitsprüfung.
- **Nachweise:** Person mit „Nur für mich“ in keiner Anzeige; Buddy-Widerruf sofort wirksam; Arena-Einzelwert nur bei doppelter Zustimmung.

## A-023 – Aggregation und k-Anonymität

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Aggregate nur ab fünf Personen mit Beitrag. Auswertungsgruppen sind ausschließlich Tenant-Gruppen je Dimension und der ganze Tenant; keine Kombination zweier Dimensionen; Zeiträume nur Challenge-Zeitraum, Kalenderwoche, Kalendermonat. Gruppenumbau löst keine Differenzbildung aus. Ranglisten zusätzlich erst ab 40 Prozent Beteiligung der aktivierten Personen der Gruppe. Arena erhält nur Pro-Kopf-Wert auf Sollstärke, Größenklasse, Name, Logo, Zeitpunkt.
- **Begründung:** Schutz vor Rückrechnung einzelner Beiträge und vor Erkennbarkeit schwacher Beteiligung.
- **Folgen:** Exporte und Verwaltungsansichten verwenden dieselbe Prüfung wie die App; Sollstärke steuert nie Sichtbarkeit.
- **Nachweise:** vier Beitragende ergeben keine Ausgabe, fünf schon; Gruppenumbau ohne Differenz; Rangliste erst bei beiden Schwellen.

## A-024 – Fristen, Löschung und Austritt

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Fristen je Datenkategorie gemäß [Datenschutz und Nachweis](datenschutz-und-nachweis.md), Abschnitt 5, darunter 24 Monate Aktivitätsdetail, 90 Tage Wearable-Rohdaten, 12 Monate Stimmungs- und Ergonomiedaten, 3 Jahre Zustimmungs- und Prüfprotokoll, 7 Jahre Metering, höchstens 35 Tage Backups. Austritt: Zugang sofort, Löschung binnen 30 Tagen, Kollektivanteile anonym, Zustimmungsprotokoll mit nicht rückführbarer Kennung. Verwaiste Personen nach 24 Monaten ohne Anmeldung und Handlung automatisch ausgetreten. Modulkündigung 90 Tage Lesbarkeit, Tenant-Kündigung Löschung nach 90 Tagen mit Schlüsselvernichtung.
- **Begründung:** Datenminimierung mit Erhalt von Jahresvergleichen; Löschung muss Backups erreichen; verwaiste Konten dürfen nicht dauerhaft bestehen.
- **Folgen:** Backup-Aufbewahrung ist gemäß A-031 auf höchstens 35 Tage begrenzt; Wiederherstellung führt Löschläufe erneut aus.
- **Nachweise:** Austrittsdaten nach 30 Tagen nicht auffindbar, nach 35 Tagen auch in Backups nicht; Kollektivsummen unverändert.

## A-025 – Nachweis, Auskunft und Zugriff

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Unveränderliches Zustimmungsprotokoll je Person; append-only Prüfprotokoll aller Konfigurations-, Rollen- und Zugangsänderungen mit Klarnamen der Funktionsrolle; Einsichtsrolle liest Regeln, Module, tenantweite Quote ab fünf und Prüfprotokoll, nie Personen; Selbstexport jeder Person maschinenlesbar und lesbar; Tenant-Export ohne Mitgliederdaten; Operator-Support nur mit Vorgangsnummer, 24 Stunden, protokolliert, nie Vault oder Einzelwerte; gepflegte Nachweisdokumente.
- **Begründung:** Nachweisbarkeit ersetzt Vertrauensappelle gegenüber Betriebsrat und Datenschutzbeauftragtem.
- **Folgen:** Protokollschnittstelle wird von allen Domänen verwendet; Support-Zugriffe sind für die Einsichtsrolle sichtbar.
- **Nachweise:** Export vollständig und frei von Fremddaten; Support-Zugriff im Protokoll; Einsichtsrolle ohne Personenzugriff.

## A-026 – Technische Schutzmaßnahmen

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Personendaten ausschließlich in der EU; CDN nur für anonyme Assets. Verschlüsselung im Ruhezustand und TLS 1.3. Feldverschlüsselung mit Datenschlüssel je Tenant in getrenntem Geheimnisspeicher für Stimmungs-, Ergonomie-, Wiederherstellungs- und Vault-Daten. Signierte kurzlebige Medien-URLs. Strikte Content-Security-Policy ohne Fremdhosts; selbst ausgelieferte Schriften und Icons. Keine Personendaten in URLs, Push oder Fehlerberichten. Uploads neu kodiert, Metadaten entfernt, virengeprüft. Logs ohne Personenbezug, keine externen Telemetriedienste. Nie Produktionsdaten in Staging. Bedrohungsmodellierung je Domäne; Isolationstest aller Routen in CI mit Antwort „nicht gefunden“.
- **Begründung:** Vault-Prinzip und Betriebsratsfähigkeit vertragen keine fremde Telemetrie und keine Fremdhosts; Schlüsselvernichtung macht Löschung sofort wirksam.
- **Folgen:** Geheimnisspeicher, Objektspeicher und Beobachtung sind in A-029 unter diesen Vorgaben festgelegt: OpenBao, Garage, OpenTelemetry mit Prometheus, Loki, Tempo und Grafana.
- **Nachweise:** CSP-Verstöße in CI erkannt; Foto ohne Metadaten; Datenbankzugriff ohne Schlüssel liefert keine lesbaren Stimmungsdaten; Isolationstest grün.

## A-027 – Betriebsziele

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Verfügbarkeit 99,5 % je Kalendermonat ohne angekündigte Wartungsfenster; RPO 15 Minuten; RTO 4 Stunden; Planungslast 100 Tenants, 25.000 Personen, 300 API-Anfragen/s, 50 Beiträge/s; wöchentliches Wartungsfenster von höchstens 30 Minuten.
- **Begründung:** auf einem Einzelhost erreichbar; Verlust eines Tages an Check-ins wäre für ein Beteiligungsprodukt nicht hinnehmbar, 15 Minuten über WAL-Archivierung schon.
- **Folgen:** Werte werden gemessen, bevor sie einem Tenant zugesagt werden; höhere Zusagen oder gemessene Last über der Planungslast lösen A-028 Abschnitt Übergang aus.
- **Nachweise:** Neuaufbau innerhalb der RTO mit Datenstand innerhalb der RPO; Lasttest mit Planungslast gemäß [Betrieb](betrieb.md), Abschnitt 9.

## A-028 – Hosting und Orchestrierung

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Hosting stellt der Betreiber selbst bereit; Vorgabe an die Umgebung sind Linux-Hosts in der EU, Objektspeicher an zweitem Standort und Zwei-Faktor an allen Verwaltungskonten. Einstieg mit einem Produktivhost, einem Staging-Host und einem Beobachtungshost unter Docker Compose. Übergang zu k3s mit replizierter PostgreSQL erst bei Vertrag über 99,5 %, Last über Planungslast oder Kapazitätsgrenze; Compose-Definitionen bleiben eins zu eins übertragbar. Nur 443 und 80 öffentlich; SSH mit Schlüssel und festgelegten Quelladressen; Container ohne Root und ohne Docker-Socket; Server in UTC, Tenant-Standardzeitzone Europe/Vienna. Drei Umgebungen mit denselben Images; Staging nie mit Produktionsdaten.
- **Begründung:** kleines Team, wenige Komponenten, keine Orchestrierung ohne Bedarf.
- **Nachweise:** externer Zugriff auf interne Dienste scheitert; Staging-Aufbau aus derselben Definition. Nachweisstand: beide in Compose auf dem CI-Runner nachgewiesen am 25.09.2026 (Stufe 1, A-107); Wiederholung auf den Hosts des Betreibers steht aus.

## A-029 – Plattformkomponenten

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Caddy (Reverse Proxy, ACME-TLS), Garage (S3-kompatibler Objektspeicher), OpenBao (Transit für Tenant-Datenschlüssel, KV für Anwendungsgeheimnisse, Entsiegelung durch zwei Personen), pgBackRest (Datenbank-Backup), OpenTelemetry Collector, Prometheus, Loki, Tempo, Grafana (nur intern, Passkey oder externer Anbieter), Uptime Kuma vom Beobachtungshost. Aufbewahrung Logs 30 Tage, Metriken 90 Tage, Traces 7 Tage. Pflicht-Dashboards und Alarme gemäß [Betrieb](betrieb.md), Abschnitt 3.2.
- **Begründung:** selbst betreibbar, freie Lizenzen, keine externen Telemetriedienste, Geheimnisspeicher getrennt von der Datenbank gemäß A-026.
- **Folgen:** kein Sentry, kein MinIO, kein HashiCorp Vault; keine weiteren Betriebsprodukte ohne Registereintrag.
- **Nachweise:** Alarme innerhalb von 5 Minuten bei Ausfall einzelner Komponenten; Logs und Traces ohne Personendaten.

## A-030 – Lieferkette

- **Status:** angenommen, 20.09.2026.
- **Umfang:** GitHub mit privatem Repository; kein Branch-Schutz und keine Pull-Request-Pflicht. Stufen 1 bis 5 des Durchstichs entstanden direkt auf `master`; ab Stufe 6 arbeitet jede Stufe in einem eigenen Branch `stufe-<n>-<name>` und wird nach der Abnahme mit grüner CI per Merge (Fast-Forward oder Merge-Commit, keine Historienumschreibung) auf `master` gebracht; Dependabot-Vorschläge werden im laufenden Stufenbranch übernommen und ihre Pull Requests geschlossen. GitHub Actions bei jedem Push auf jedem Branch für Build, Prüfungen, Tests in Testcontainern, Architekturtests, Lint-Grenzen, Ladebudget, Image-Bau mit Trivy und Betriebsnachweise; Push nach GHCR und Wiederherstellungsübung nur für `master`; GitHub Container Registry mit Digest-Pins und wöchentlichem Neubau; Deployment per SSH mit Migrations-Container vor dem Anwendungsstart, Gesundheitsprüfung und Rollback auf vorherigen Digest; Migrationen vorwärtskompatibel; Staging vor Produktion mit ausdrücklicher Freigabe.
- **Begründung:** GitHub-Runner liefern Docker für Testcontainers ohne eigene Runner; ein kleines Team arbeitet ohne Verzweigungsaufwand, ab Stufe 6 mit genau einem Branch je Stufe, damit `master` stets den abgenommenen Stand trägt und die Stufenarbeit eine eigene CI-Historie hat (Änderung am 26.09.2026 in Stufe 4).
- **Folgen:** CI läuft nach jedem Push auf jedem Branch; ein Deployment auf Staging oder Produktion setzt einen grünen CI-Lauf des betreffenden Commits auf `master` voraus; ein roter Lauf auf `master` wird als Nächstes repariert; ein Stufenbranch wird erst mit grünem Lauf gemergt; manuelle Hosteingriffe werden binnen einer Woche in Code überführt.
- **Nachweise:** fehlerhafte Migration bricht vor Anwendungsstart ab; Rollback ohne Datenbank-Rollback; kritischer Trivy-Fund blockiert das Release. **Technisch nachgewiesen** am 25.09.2026, Stufe 1 ([Protokoll](../durchstich/abnahme/stufe-1.md)).

## A-031 – Backups und Wiederherstellung

- **Status:** angenommen, 20.09.2026.
- **Umfang:** PostgreSQL täglich Basissicherung und WAL alle 5 Minuten über pgBackRest; Garage täglich inkrementell; OpenBao-Snapshots täglich und nach Rotation; Konfiguration im Repository; Host-Snapshots täglich. Alles verschlüsselt an einen zweiten Standort. Aufbewahrung höchstens 35 Tage. Backup-Schlüssel in OpenBao und offline. Vierteljährliche Wiederherstellungsübung auf Staging mit Protokoll; erste Übung im Durchstich. Ausbleibendes Backup ist eine Störung.
- **Begründung:** RPO und RTO aus A-027; Löschwirkung in Backups aus A-024.
- **Folgen:** Löschläufe werden nach jeder Wiederherstellung erneut ausgeführt.
- **Nachweise:** Neuaufbau innerhalb der RTO; gelöschte Daten nach 35 Tagen in keinem Backup. Nachweisstand: Neuaufbau innerhalb der RTO technisch nachgewiesen am 25.09.2026 in der Umgebung nach A-107 (Stufe 1); Löschwirkung in Backups offen bis zur Einführung der Löschläufe.

## A-032 – Domains, E-Mail und Push

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Eine Plattform-Origin für App, Verwaltung, Kiosk, Operator-Konsole und API; keine tenant-eigenen Domains; eigene Staging-Origin; die Plattformdomain ist Konfiguration und keine Vorbedingung. TLS über ACME mit HSTS. E-Mail als konfigurierbarer Kanal auf Plattform- und Tenant-Ebene mit den Transporten SMTP (mit oder ohne TLS, mit oder ohne Authentifizierung), Amazon SES, SendGrid und Mailgun; Zugangsdaten in OpenBao; Testnachricht vor Aktivierung; Tracking deaktiviert; SPF, DKIM und DMARC je Absenderdomain; einstellbarer Rückfall auf Plattformversand bei Tenant-Fehler. Web Push über VAPID mit verschlüsselter Nutzlast, die nur eine Referenz enthält. Kein Status-Portal zum Start.
- **Begründung:** eine Origin hält Cookie-Sitzung, CSRF-Schutz, OIDC-Redirects und Service Worker einfach (A-007); Tenants sollen unter eigener Absenderdomain und mit eigener Mailinfrastruktur versenden können.
- **Folgen:** Ein plattformweit konfigurierter Anbieter ist Unterauftragsverarbeiter des Betreibers; ein vom Tenant konfigurierter Transport ist Auftragsverarbeiter des Tenants. Details in [Betrieb](betrieb.md), Abschnitt 6.
- **Nachweise:** Versand über alle fünf Transportvarianten mit bestandenem SPF/DKIM/DMARC; Rückfall; Push ohne Inhalt in der Nutzlast.

## A-033 – Organisationsmodell und Tenant-Lebenszyklus

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Eine Entität `organisation` mit Typ und optionaler Elternbeziehung; höchstens Operator → Partner → Tenant; genau eine Operator-Organisation als Heimat der Operator-Personen; keine Sub-Partner; Standorte, Abteilungen und Schichten sind Gruppen im Tenant. Stammdaten je Organisation einschließlich Rechnungsanschrift, Zeitzone (Europe/Vienna) und Standardsprache (de-AT) beim Tenant. Zustände Eingerichtet, Aktiv, Gesperrt, Gekündigt, Gelöscht; Sperre durch Operator oder Partner mit Grund; Kündigung durch Tenant-Admin, Partner oder Operator zum Monatsende; 90 Tage Lesezugriff, dann Löschung nach A-024. Anlage ausschließlich durch Operator-Admin oder Partner-Admin mit erstem Tenant-Admin-Rollencode; keine Selbstregistrierung von Firmen, keine öffentliche Registrierungsroute.
- **Begründung:** ein Personen-, Sitzungs- und Rollenmodell für alle Ebenen; flache Hierarchie hält Vererbung und Berechtigungen prüfbar; vertriebsgeführte Anlage passt zu individueller Preisgestaltung und Verkauf an Firmenvertreter.
- **Nachweise:** vierte Ebene abgelehnt; Sperre und Entsperrung ohne Datenverlust; Kündigung mit 90 Tagen Lesezugriff und Löschung.

## A-034 – Gruppen und Sollstärken

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Bis zu drei frei benannte Dimensionen mit flachen Gruppenlisten; eine Gruppe je Dimension je Person, selbst gewählt und selbst änderbar; Gruppen werden archiviert, nicht gelöscht; Zusammenlegen mit Protokoll. Sollstärke je Tenant und je Gruppe mit Stichtagshistorie, überall als Schätzgröße ausgewiesen, nie sichtbarkeitssteuernd; vierteljährliche Erinnerung; Warnung unter fünf. CSV-Import für Gruppen und Sollstärken; kein Personenimport.
- **Begründung:** flache Gruppen verhindern kleine Blätter unter der Mindestzahl und Differenzbildung zwischen Ebenen; Stichtagshistorie hält vergangene Quoten stabil.
- **Folgen:** Auswertungsgruppen sind ausschließlich diese Gruppen und der ganze Tenant (A-023).
- **Nachweise:** Archivierung verlangt Neuwahl; Sollstärke-Änderung lässt vergangene Quoten unverändert.

## A-035 – Mitgliedschaft

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Zustände Aktiv, Ausgetreten, Entfernt; kein Sperrzustand für Mitglieder; keine Obergrenze je Tenant. Tenant-Admin sieht Anzeigename, Gruppen, Rollen, Beitrittsdatum und kann Rollen entziehen, Mitglieder entfernen und Rollencodes ausstellen; er kann weder Anzeigename, Gruppen noch Sichtbarkeit einer Person ändern und keine Einzelperson anschreiben. Beiträge bleiben der Gruppe zum Beitragszeitpunkt zugeordnet.
- **Begründung:** Werkzeuge gegen Missbrauch ohne Werkzeuge gegen Personen; stabile vergangene Kollektivstände.
- **Nachweise:** Entfernen wirkt wie Austritt und steht im Protokoll; Gruppenwechsel verändert vergangene Stände nicht.

## A-036 – Rollenkatalog und Rechte

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Neun Rollen: Operator-Admin, Operator-Support, Partner-Admin, Tenant-Admin, Programm-Manager, Redakteur, Einsichtsrolle, Gesundheitsbotschafter (gruppengebunden), Mitglied. Rechtematrix gemäß [Organisation und Mandanten](organisation-und-mandanten.md), Abschnitt 4.2. Mehrere Rollen je Person möglich; mindestens ein Tenant-Admin je aktivem Tenant; Operator- und Partner-Rollen wirken nur über ihre Konsolen, nie innerhalb eines Tenants. Botschafter: nur eigene Gruppen, nur freigegebene Vorlagen, Quote ab fünf, keine Mitgliederliste.
- **Begründung:** klare Trennung zwischen Betreiben, Verkaufen, Verwalten, Programm machen und Teilnehmen; die Kernregel gegen Einzelwerte bleibt in jeder Zelle erhalten.
- **Folgen:** Wer Partner- oder Operator-Rolle und Mitgliedschaft in einem Tenant will, braucht getrennte Login-Identitäten.
- **Nachweise:** jede „–“-Zelle wird über API und Oberfläche abgelehnt; letzter Tenant-Admin kann seine Rolle nicht abgeben.

## A-037 – Partner-Ebene

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Partner setzen für ihre Tenants erlaubte Module, einen Preisrahmen innerhalb des vom Operator zugewiesenen Rahmens sowie Voreinstellungen für Marke und Anmeldewege, die der Tenant überschreiben darf. Partner sehen Stammdaten, Zustand, Module, Preisplan, aggregierte Abrechnungsmengen und die Protokolleinträge ihrer Tenants zu Anlage, Zustand, Modulen, Preisen und Rahmen; nie Mitgliederlisten, Inhalte, Challenges, Quoten, Gruppen, Sollstärken oder tenant-interne Konfigurationsänderungen. Buchungen außerhalb eines neuen Rahmens laufen zum übernächsten Monatsende aus. Tenant-Wechsel zwischen Partnern und Partner-Auflösung durch den Operator.
- **Begründung:** Reseller und Konzern-Holdings brauchen Anlage und Rahmen, nicht Einblick; Abrechnungsmengen sind für Provisionen nötig und im Auftragsverarbeitungsvertrag benannt.
- **Nachweise:** Partner-Admin ohne Zugriff auf Mitgliederdaten und Quoten; Rahmenverletzung bei Modulbuchung abgelehnt.

## A-038 – Challenge-Modell

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Eine Challenge ist eine Komposition aus sieben Achsen (Zeitform, Metrik, Erfassung, Normalisierung, Aggregation, Wettbewerbsform, Sichtbarkeit) und drei Blöcken (Fairness, Beitritt, Belohnung). Sammelziel ist die voreingestellte Form. Metrikkatalog mit frei definierbaren Metriken; Körperbild-Metriken existieren nicht; Schlaf nur binär. Aggregation ausschließlich entlang der Gruppendimensionen und des Tenants, keine Ad-hoc-Teams. Belohnungen kollektiv als Abzeichen oder Tenant-Belohnung mit Freitext und Zielbedingung; nie Geld oder Einzelgutscheine. Sensible Gewohnheiten als eigene Kategorie mit erzwungener Sichtbarkeit „Nur ich“, ohne Teilnehmerzahl, Feed und Aggregate. Texte je Sprache mit Pflicht in der Standardsprache des Tenants.
- **Begründung:** Aus einer Engine entstehen alle Formate; Kooperation als Standard schützt Beteiligung; feste Auswertungsgruppen schützen die Mindestzahl.
- **Folgen:** Wizard blockiert pro Kopf ohne Sollstärke und zeigt Wirkungshinweise bei Rangliste und Duell. Details in [Challenges](challenges.md), Abschnitt 2.
- **Nachweise:** alle sieben Formen aus den Achsen konfigurierbar; sensible Kategorie ohne Aggregate.

## A-039 – Erfassung und Korrektur

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Erfassungszeitpunkt in Tenant-Zeitzone; Rückdatierung bis drei Kalendertage für Häkchen, Zahl und Dauer, nie Zukunft; Tagesdeckel kappt und zeigt an; Plausibilitätsgrenze 25.000 Schritte je Tag für automatische Quellen; Nachfrist 48 Stunden nach Ende für Beiträge mit Zeitpunkt im Zeitraum; Korrektur und Löschung eigener Beiträge bis Ende der Nachfrist als Gegenbuchung, Abzeichen bleiben; Kiosk ohne Foto und ohne Korrektur; bei manueller und automatischer Erfassung derselben Metrik am selben Tag gilt der höhere Wert bis zum Deckel. Jeder Beitrag erzeugt in einer Transaktion Beitrag, Fachereignis, ein pauschales Aktivitätsereignis und Folgejobs; das Metering-Ereignis `challenge.participant_day` entsteht beim ersten Beitrag einer Person am Tag; die sensible Kategorie erzeugt keine Challenge-Metriken.
- **Begründung:** Vergessen darf nicht bestrafen, Manipulation darf sich nicht lohnen, Offline-Nutzung am letzten Tag darf nichts verlieren.
- **Nachweise:** Deckel, Rückdatierung, Doppelübertragung, Nachfrist und Korrektur gemäß [Challenges](challenges.md), Abschnitt 9.

## A-040 – Lebenszyklus, Kadenz und Zuständigkeit

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Zustände Entwurf, Geplant, Laufend, Nachfrist, Beendet, Archiviert; Vorschau vor dem Planen Pflicht; Achsen nach Start unveränderbar, Texte änderbar; vorzeitiges Ende mit Begründung im Protokoll. Programm-Manager legt tenantweit an, gibt Vorlagen frei, setzt Kadenz und markiert Belohnungen als eingelöst; Botschafter legen für eigene Gruppen aus freigegebenen Vorlagen an; Mitglieder legen keine Challenges an, können Vorlagen vorschlagen. Keine Vorlage innerhalb von 12 Monaten wiederholt, Warnung übersteuerbar mit Protokoll. Kickoff-Challenge beim Einrichten vorbelegt.
- **Begründung:** Kadenz braucht eine verantwortliche Rolle; unveränderbare Achsen schützen die Fairness laufender Wertungen.
- **Nachweise:** Achsenänderung nach Start abgelehnt; Wiederholungswarnung und Protokoll.

## A-041 – Vorlagenbibliothek

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Drei Ebenen: Plattformvorlagen des Operators, Partnervorlagen, Tenantvorlagen aus eigenen Challenges. Eine Vorlage ist eine vollständige Achsenkombination mit Texten, Bild, Vorschlagswerten und Metadaten. Vorlagen mit nicht freigeschalteter Quelle werden mit Ersatzmetrik angeboten. 24 Plattformvorlagen zum Start gemäß [Challenges](challenges.md), Abschnitt 5.2. Texte und Bilder sind Redaktionsarbeit des Betreibers.
- **Begründung:** Niemand startet vor einem leeren Formular; Vorlagen sind die wichtigste Einzelmaßnahme gegen leere Programme.
- **Nachweise:** Klonen, Anpassen von zwei Feldern und Starten innerhalb von drei Minuten; Ersatzmetrik ohne Wearable-Modul.

## A-042 – Wertung, Kollektivstände und Ranglisten

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Kollektivstände ereignisgetrieben mit Altersanzeige; Ranglisten als Snapshot alle 15 Minuten und am Ende der Nachfrist, nie live; pro Kopf auf Sollstärke der Gruppe zum Challenge-Start; relative Verbesserung mit sieben Tagen Baseline und Wertung ab Tag acht, ohne Baseline keine Verbesserungswertung; binäre Zielerreichung je Tag; Aufgabenraster und Staffel gemäß Export. Anzeige nach A-022 und A-023: unter fünf oder unter 40 Prozent Kollektivfortschritt statt Rangliste, keine Nullwerte, Meilensteine als genau ein Feed-Ereignis. Arena-Projektion nur Pro-Kopf-Wert auf Tenant-Sollstärke, Größenklasse, Zeitpunkt; unter fünf nur „nimmt teil“.
- **Begründung:** Snapshots verhindern Rückschlüsse aus Einzelbewegungen; Sollstärke-Bezug belohnt Beteiligung statt Nichtaktivierung.
- **Nachweise:** Schwellen, Baseline, Sollstärke-Stichtag und Projektion gemäß [Challenges](challenges.md), Abschnitt 9.

## A-043 – Buddy-System

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Zwei Mitglieder desselben Tenants, Einladung und ausdrückliche Bestätigung, höchstens ein Buddy zugleich, gegenseitige Sicht auf Serie, Tagesziel und gemeinsame Challenges unabhängig von der Sichtbarkeitsstufe, jederzeit einseitig mit sofortiger Wirkung beendbar, für Dritte unsichtbar, wöchentlicher gemeinsamer Anstoß, Ende bei Austritt einer Seite.
- **Begründung:** persönliche Verbindlichkeit wirkt auch dort, wo kein Feed in Gang kommt; die enge Fassung hält die einzige Sichtbarkeitsausnahme klein.
- **Nachweise:** Sicht trotz „Nur für mich“, sofortiges Ende bei Widerruf, Unsichtbarkeit für Botschafter.

## A-044 – Aktivitätsereignisse und Punkte

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Jede Handlung erzeugt genau ein Aktivitätsereignis; Korrekturen erzeugen Gegenereignisse. 10 Punkte je gewerteter Handlung unabhängig von Art und Messwert; höchstens 10 gewertete Handlungen je Tag; Deckel je Art (Check-in 1, Challenge-Beiträge 3, Inhalte 3, gelesene Beiträge 2, Anerkennungen 3, Feed-Beiträge 1, Veranstaltungsbesuch 1, automatische Tageswerte 1 je Metrik). Saldo verfällt nie, nie negativ, nicht einlösbar. Automatische Tageswerte aus dem Vault ergeben höchstens eine Handlung je Metrik und Tag und zählen nicht als „aktives Mitglied“.
- **Begründung:** Aktivitätsgleichheit als Rechenregel statt Absichtserklärung; Deckel gegen Ausreißer und Manipulation.
- **Nachweise:** Deckel gesamt und je Art; Gegenereignis reduziert Saldo bis null; automatischer Tageswert ohne Abrechnungswirkung.

## A-045 – Serie und Abwesenheit

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Serie als zusammenhängende Tage mit gewerteter Handlung in Tenant-Zeitzone; Serienschutz einmal je Kalendermonat, automatisch; Serienbruch senkt nie Stufe, Abzeichen oder Rang; Neuberechnung bei Korrektur. Abwesenheit „von–bis“ ohne Grund, rückwirkend bis drei Tage, höchstens acht Wochen je Eintrag; pausiert Serie, unterdrückt alle Benachrichtigungskategorien außer Konto und Sicherheit sowie Plattform (A-060); für niemanden sichtbar außer als pausierte Serie für den Buddy.
- **Begründung:** Krankenstand oder Urlaub dürfen keine Serie zerstören und keine Gesundheitsinformation hinterlassen.
- **Nachweise:** Pause ohne Bruch; kein Grund gespeichert; Signale unterdrückt.

## A-046 – Abzeichen

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Plattformkatalog des Operators mit rund 30 Abzeichen in fünf Kategorien (Einstieg, Dranbleiben, Gemeinsam, Vielfalt, Saison); keine Tenant- oder Partnerabzeichen. Regeln nur über Handlungen, Tage und Kollektivereignisse, nie über Messwerte. Verleihung ereignisgetrieben, je Person einmal, nie entzogen. Sichtbar: verdiente plus genau ein nächstes je Kategorie. Erstes Abzeichen im Beitritt. Feed-Ereignisse nach Sichtbarkeitsstufe; Sammelereignisse ab fünf Personen.
- **Begründung:** kein grauer Friedhof; verdiente Anerkennung bleibt; einheitliche Gestaltung über alle Tenants.
- **Nachweise:** höchstens fünf offene Kacheln; Abzeichen bleibt nach Korrektur; Sammelereignis erst ab fünf.

## A-047 – Stufen

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Fünf Stufen bei 0, 300, 1.000, 3.000 und 10.000 kumulierten Punkten; Namen Neu dabei, Dabei, Dranbleiber, Vorbild, Urgestein, je Tenant umbenennbar; Stufen schalten nichts frei und sinken nie.
- **Begründung:** Beständigkeit statt Leistung; keine Inhalte hinter Verhaltensbedingungen.
- **Nachweise:** Stufe bleibt bei Saldo unter Schwelle; keine Freischaltung an Stufen gebunden.

## A-048 – Check-in, persönliche Anzeigen und Signale

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Täglicher Check-in mit drei Kacheln (Bewegt, Pause gemacht, Erholt), Mehrfachauswahl, ein Tap, kein Rating, auch am Kiosk. Persönliches Tagesziel 1 bis 3 Handlungen. Tagesring, Wochenstreifen, Monatsraster, Punkteverlauf erst ab 14 Tagen Daten, Serien-Chip; alles nur für die Person, Buddy sieht Serie und Tagesring. Wochenrückblick privat, Jahresrückblick privat und teilbar, beide ohne Vergleiche. Inaktivitätssignale nach 7, 30 und 60 Tagen als interne Ereignisse an Benachrichtigungen, nie sichtbar, nie ausgewertet, nicht während Abwesenheit. Aufbewahrung nach A-024 mit Monatsaggregaten.
- **Begründung:** Der Kern erzeugt eigenen Fortschritt ohne Modul; Anzeigen ohne Nullwerte; Inaktivität bleibt unsichtbar.
- **Nachweise:** Check-in am Kiosk; Punkteverlauf erst ab 14 Tagen; Signale unterdrückt bei Abwesenheit; Aggregate nach 24 Monaten.

## A-049 – Feed-Modell

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Ereignis-Stream aus vier Quellen mit Priorität System-Ereignisse, kuratierte Inhalte, Tenant-Beiträge, Mitglieder-Beiträge; fünf Kartentypen mit festem Layout; Startseite mit Kopfkarte, Check-in-Karte, höchstens zwei angehefteten Tenant-Beiträgen für höchstens sieben Tage und chronologischem Stream ohne Interaktionsgewichtung; Geltungsbereich je Eintrag (Tenant, Gruppen, Arena); Mitglieder-Beiträge höchstens im Kreis der eigenen Sichtbarkeitsstufe, mit „Nur für mich“ kein Beitrag; Verdichtung gleichartiger System-Ereignisse, Sammelkarten ab fünf Personen, höchstens acht System-Karten und zwei Inhaltskarten je Tag; Abfrage mit ETag alle 60 Sekunden, Feed nur online; Einträge 12 Monate im Stream.
- **Begründung:** Der Feed ist die Oberfläche aller Module und darf nie leer sein; chronologische Ordnung ohne Beliebtheitsgewichtung verhindert Vergleichsspiralen.
- **Nachweise:** kein leerer Feed bei neuem Tenant; Reichweite nach Sichtbarkeitsstufe rückwirkend; Sammelkarte ab fünf; ETag ohne Nutzdaten.

## A-050 – Beiträge, Kommentare und Anerkennung

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Tenant-Beiträge durch Programm-Manager, Redakteur und Botschafter (eigene Gruppen) mit Text bis 2.000 Zeichen, bis zu 4 Bildern oder einem Dokument, Termin oder Link, Zeitsteuerung, Ablauf, Anheftung, Tonalitätsvorschlägen. Mitglieder-Beiträge tenantweit einschaltbar (Voreinstellung ein), Text bis 1.000 Zeichen, bis zu 4 Bilder, optional verknüpfte eigene Handlung mit wählbarer Messwertanzeige, höchstens 5 je Tag. Kommentare eine Ebene tief bis 500 Zeichen, tenantweit und je Beitrag abschaltbar, nur mit mindestens „Mein Team“; keine Kommentare auf Inhaltsobjekten. Anerkennung ohne Zähler, sichtbar dass und von wem nach Sichtbarkeit; Geben ist Handlung mit Deckel 3; Erhalten erzeugt nur eine tägliche Zusammenfassung.
- **Begründung:** Kundeninteraktion findet hier statt; Kommentare mit Werkzeugen statt Verzicht; keine Beliebtheitsmetriken.
- **Nachweise:** Reichweite, Kommentarschalter, Anerkennung ohne Zahl, Ratenbegrenzung.

## A-051 – Moderation und Meldungen

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Melden mit Grund, Melder bleibt gegenüber Betroffenen unbekannt; Moderation durch Programm-Manager tenantweit, Botschafter in eigenen Gruppen, Redakteur im Firmenkanal; Warteschlange mit belassen, ausblenden, entfernen; automatische Zurückhaltung bei Sperrlistentreffer und automatische Ausblendung ab drei Meldungen; plattformweite Sperrliste plus tenant-eigene Ergänzungen; drei Entfernungen in 30 Tagen sperren das Posten für 30 Tage, nie die Mitgliedschaft; alle Entscheidungen im Prüfprotokoll; Moderation ändert nie die Sichtbarkeitsstufe.
- **Begründung:** Ein offener Feed braucht Schutz gegen Missbrauch ohne Werkzeuge gegen Personen.
- **Nachweise:** automatische Ausblendung, Protokoll, Melderanonymität, Postsperre ohne Mitgliedschaftsverlust.

## A-052 – Inhaltsmodell

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Neun Typen: Video, Übung, Artikel, Kurzfakt, Rezept, Audio, Quiz, Dokument, Sammlung, jeweils mit festgelegter Struktur und Abschlussregel als Handlung. Sechs Kategorien: Bewegung, Ergonomie, Wissen, Regeneration, Ernährung, Firma. Pflichtmetadaten einschließlich Lizenz, Rechteinhaber, Verantwortlichem, Sprache, Version. Drei Ebenen: Plattform, Partner, Tenant; Tenant schaltet Sammlungen frei oder blendet aus; Tenantinhalte setzen die freigeschaltete Kategorie voraus. Texte je Sprache mit Fallback und Kennzeichnung. Keine Kalorien, Gewichtsziele oder Ernährungs-Scores.
- **Begründung:** feste Typen halten Feed-Karten, Filter und Barrierefreiheit konsistent; Lizenzfelder verhindern Rechteprobleme.
- **Nachweise:** jeder Typ mit korrekter Abschlusshandlung; nicht freigeschaltete Kategorie unsichtbar.

## A-053 – Redaktion

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Zustände Entwurf, Prüfung (Vier-Augen optional je Tenant), Geplant, Veröffentlicht, Zurückgezogen, Archiviert; Versionierung je Veröffentlichung; Ablaufdatum. Redaktionsplan als Kalender mit Serienformaten, Vorschlägen zu laufenden Challenges, Warnung bei leeren Wochen, abonnierbaren Plattform-Serien. Regeln: Nutzertitel Pflicht, Dauer aus Mediendatei, Duplikatprüfung, Lizenz Pflicht mit automatischem Rückzug, Bildsprache und Kontraindikationshinweis als Prüfpunkte.
- **Begründung:** Kuratierung ist Produktqualität; ein Plan verhindert leere Wochen und passt Inhalte zu Challenges.
- **Nachweise:** abgelaufene Lizenz zieht zurück; Version bleibt; Serie erzeugt Feed-Karten im Rhythmus.

## A-054 – Medienverarbeitung

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Upload über signierte URLs mit Limits (Video 2 GB, Bild 20 MB, Audio 200 MB, Dokument 50 MB); Typprüfung, Virenprüfung, Metadaten-Entfernung; ffmpeg im Worker für HLS in 360p, 720p, 1080p plus MP4-Fallback, Poster, Kapitel, Hoch- und Querformat; Bilder in AVIF, WebP, JPEG in vier Größen; Audio AAC; Untertitel WebVTT, Pflicht für Plattformvideos; Auslieferung über signierte URLs, CDN nur für Plattform- und Partnermedien ohne Personenbezug; Offline-Speicherung von Videos nur auf Wunsch mit 30 Tagen Aufbewahrung; Veröffentlichung erst nach Abschluss aller Ableitungen. Keine Einbettung externer Plattformen, keine Live-Streams.
- **Begründung:** Selbstauslieferung erfüllt A-026; adaptive Stufen bedienen Mittelklassegeräte im Mobilfunk.
- **Nachweise:** Ableitungen ohne Metadaten; Veröffentlichung blockiert bis Verarbeitung fertig; Tenantmedien nie über CDN.

## A-055 – Entdecken und persönliche Funktionen

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Eine Bibliothek mit Suche, Kategoriechips und Filtern; Volltextsuche in PostgreSQL mit deutscher Wortstammbildung und Ähnlichkeitssuche; Schnelleinstieg Bewegung über Situation und Körperregion zu drei Vorschlägen; Sammlungen, Serien, Fortsetzen, Favoriten, Verlauf, Wochenplan, Quiz mit Bestehen ab 70 %; Anerkennung ohne Zähler, keine Kommentare, Melden an Redaktion; keine Beliebtheitsranglisten; persönliche Daten nur für die Person, im Export, bei Austritt gelöscht; Barrierefreiheitsprüfpunkte in der Freigabe.
- **Begründung:** eine Bibliothek statt sieben Tabs; Suche ohne weitere Komponente; Situationsfilter vor Fachsprache.
- **Nachweise:** Suche mit Wortstamm und Tippfehler; Quiz-Handlung einmal je Quiz; Verlauf nur für die Person.

## A-056 – Inhaltsauswertung

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Person sieht eigenen Verlauf; Redaktion und Programm-Manager sehen je Inhalt Aufrufe, Abschlüsse, Favoriten, Anerkennungen als Zahlen und je Kategorie Nutzung je Woche, nur ab fünf Personen und ohne Personenbezug; Tenant-Admin Nutzung je Kategorie als Zahl; Einsichtsrolle nur aktive Kategorien; Operator tenantübergreifende Aggregate je Plattforminhalt ohne Tenant-Vergleich. Abspielminuten und Abschlüsse zugleich Metering-Ereignisse.
- **Begründung:** Redaktion braucht Wirkungsdaten; niemand braucht Personenlisten.
- **Nachweise:** Aggregate erst ab fünf; keine Personenliste über API oder Export.

## A-057 – KI-Authoring in der Redaktion mit eigenen Zugangsdaten

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Optionale KI-Schreibhilfe für Redakteure und Programm-Manager, je Tenant ein- oder ausschaltbar (Voreinstellung aus), ausschließlich mit vom Tenant beigestellten Zugangsdaten. Tenant-Admin konfiguriert Anbieterstil (OpenAI-kompatible Chat-Completions-API oder Anthropic-Messages-API), Endpunkt-URL, API-Schlüssel in OpenBao, Modellbezeichnung und optionales Monatslimit; Testaufruf vor Aktivierung Pflicht. Funktionen: Entwurf, Umformulierung in Tonalität, Kürzen, Zusammenfassen, Titel, Übersetzung, Textalternativen, Untertitelkorrektur; Ergebnisse sind Vorschläge im Editor und durchlaufen den normalen Lebenszyklus. Übertragen werden nur Inhaltstext, gewählte Kontexttexte und Tonalität; nie Mitglieder-, Aktivitäts-, Aggregat- oder Metering-Daten. Aufrufe serverseitig über den Worker; keine Speicherung von Prompts oder Antworten außerhalb des Entwurfs; Protokoll nur mit Anzahl, Zeitpunkt und Funktion.
- **Begründung:** Redaktionsarbeit ist der teuerste Teil des Programms; eigene Zugangsdaten machen den Anbieter zum Auftragsverarbeiter des Tenants statt des Betreibers und halten die Funktion optional.
- **Folgen:** Kein Plattform-Schlüssel des Betreibers; kein KI-Coach für Mitglieder; A-021 unberührt. Details in [Feed und Inhalte](feed-und-inhalte.md), Abschnitt 8.1.
- **Nachweise:** ohne Konfiguration keine Oberfläche; Anfrageinhalt ohne Personendaten; Schlüssel nur in OpenBao; Limit pausiert; keine Veröffentlichung ohne Freigabe.

## A-058 – Benachrichtigungskanäle und Tenant-Schalter

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Fünf Kanäle: In-App als immer aktive Grundlage mit Benachrichtigungszentrum, Web Push, E-Mail, Aushang als PDF, Kalender als ICS. Kein SMS, keine Chat-Integrationen, keine nativen Push-Dienste außerhalb des Web-Standards. Tenant schaltet je Kanal und Kategorie ein oder aus; Ausschalten pausiert sofort und erhält Abonnements und Einstellungen; Einschalten wirkt sofort ohne neue Berechtigung; Schalter im Prüfprotokoll. Der Kiosk zeigt keine persönlichen Benachrichtigungen.
- **Begründung:** Optionalität je Tenant ohne Verlust der Reichweite beim Wiedereinschalten; Feed bleibt Ereignis-Stream, Benachrichtigungen sind an Personen gerichtet.
- **Nachweise:** Pausieren ohne Zustellung, Wiedereinschalten ohne Prompt, Protokolleintrag.

## A-059 – Web Push

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Push-API und Notifications-API im Angular-Service-Worker mit VAPID-Schlüsselpaar der Plattform in OpenBao und geplanter Rotation; kein Fremd-SDK. Nutzlast verschlüsselt mit ausschließlich Referenz, Kategorie und Ziel; Auflösung über die API mit Sitzung, ohne Sitzung neutrale Anzeige mit Produktnamen. TTL, Dringlichkeit und Sammelschlüssel je Kategorie und Bezug. Abonnement erst nach App-eigenem Erklärungsschritt und Button, auf iOS nach Installation; automatisches Abonnieren durch den Service Worker und Erneuerung bei `pushsubscriptionchange`; Prüfung bei jedem App-Start; 404 oder 410 löschen, fünf Fehler pausieren das Abonnement. Geräteliste je Person; Löschung bei Abmeldung aus allen Sitzungen und Austritt; Kiosk abonniert nie.
- **Begründung:** Standardkonformer Push ohne Drittanbieter erfüllt A-026; Erklärungsschritt vor dem Browser-Prompt sichert die Zustimmungsquote.
- **Nachweise:** Prompt erst nach Button, Erneuerung ohne Nutzeraktion, Nutzlast ohne Inhalt, Löschung bei 410.

## A-060 – Kategorien, Regelwerk und Zusammenspiel mit Beiträgen

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Zwölf Kategorien mit Standardwerten je Kanal, darunter Veranstaltungen für M9, gemäß [Benachrichtigungen](benachrichtigungen.md), Abschnitt 4.1; Konto und Sicherheit sowie Plattform nicht abschaltbar. Regeln: Sichtbarkeitsprüfung für Empfänger und genannte Personen; höchstens drei Push je Person und Tag außer Erinnerungen und Konto; drei Ankündigungen je Woche tenantweit, eine je Woche und Gruppe für Botschafter; ein Inhalte-Push je Tag; Ruhezeit tenantweit 20:00 bis 07:00, persönlich nur erweiterbar, Zurückhaltung und Verdichtung um 07:00; Verdichtung gleicher Bezüge in 30 Minuten; tägliche Zusammenfassung für Kommentare und Anerkennungen; Abwesenheit unterdrückt alles außer Konto und Plattform; Idempotenz je Ereignis, Person und Kanal; Texte in Tonalität und Sprache ohne Rückstand, Vergleich oder Nichtteilnahme. Tenant-Beiträge lösen Push nur mit Markierung „Ankündigung“ aus; Mitglieder-Beiträge nie an Dritte; Autoren sehen die erwartete Reichweite je Kanal ab fünf.
- **Begründung:** Benachrichtigungen müssen Feed und Beiträge tragen, nicht übertönen; Kontingente und Ruhezeiten sind Teil des Modells.
- **Nachweise:** Kontingente, Ruhezeit, Verdichtung, Abwesenheit, Sichtbarkeit ohne Namen, Ankündigungsmarkierung.

## A-061 – Persönliche Einstellungen und Erinnerungen

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Je Kategorie Push ein/aus und E-Mail sofort, täglich, wöchentlich oder aus; In-App immer aktiv; Geräteliste; persönliche Ruhezeit nur erweiterbar; bis zu vier Erinnerungsfenster je Tag mit Wochentagen, optionalem Text und Kanal Push oder Kalender, ohne Zahlen zum eigenen Stand; Sprache; alles je Person und Tenant, im Export, bei Austritt gelöscht. Onboarding schlägt ein Zeitfenster vor und setzt nichts ohne Zustimmung.
- **Begründung:** Erinnerungen wirken nur, wenn die Person sie selbst gesetzt hat.
- **Nachweise:** Erinnerung zur gewählten Zeit trotz Ruhezeit; Onboarding ohne stillschweigende Aktivierung.

## A-062 – E-Mail, Aushang und Kalender

- **Status:** angenommen, 20.09.2026.
- **Umfang:** E-Mail sofort oder als tägliche und wöchentliche Zusammenfassung außerhalb der Ruhezeit; Ein-Klick-Abmeldung je Kategorie außer Konto; kein Tracking; Transport nach A-032; drei Unzustellbarkeiten pausieren E-Mail für die Person. Wöchentlicher Aushang-Zettel je Tenant oder Gruppe und Ankündigungs-Aushang aus Tenant-Beitrag als A4-PDF mit Tokensatz, ohne Personenbezug, ohne Quote unter fünf, erzeugt vom Worker, Vorlage nicht frei gestaltbar. ICS-Export für Erinnerungen und Termine ohne Personendaten Dritter.
- **Begründung:** Belegschaften ohne Gerät erreichen die Plattform nur am Aushang; E-Mail ohne Tracking bleibt betriebsratsfähig.
- **Nachweise:** Aushang ohne Personendaten; Abmeldung sofort wirksam; kein Pixel.

## A-063 – Zustellpipeline, Status und Auswertung

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Fachereignis → Regelwerk im Worker → In-App-Eintrag immer plus Kanaljobs mit Idempotenzschlüssel → Zustellstatus ohne Inhalt 30 Tage → Lesestatus geräteübergreifend; Einträge 90 Tage; Zentrum online. Auswertung für Programm-Manager und Tenant-Admin je Kategorie und Woche (gesendet, zugestellt, geöffnet, Anteil mit aktivem Push und installierter App) nur ab fünf Personen; Einsichtsrolle sieht Kanäle, Kategorien, Schalter und Kontingente; Operator Fehlerquoten plattformweit; keine Personenlisten. Push-Dienste der Browserhersteller als Empfänger technisch notwendiger Daten im Auftragsverarbeitungsvertrag; Endpunkte werden wie Zugangsdaten behandelt; signierte, zeitlich begrenzte Einstellungslinks; Löschung bei Austritt.
- **Begründung:** eine Pipeline für alle Kanäle mit garantierter Einmaligkeit; Wirkungsdaten ohne Personenbezug.
- **Nachweise:** wiederholter Job ohne Doppelzustellung; Aggregate erst ab fünf; Löschung bei Austritt.

## A-064 – Modulkatalog und Kern

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Kern immer aktiv: Identität und Zugang mit allen Anmeldewegen und Kiosk, Organisation, Profil und Fortschritt, Feed mit Tenant- und Mitglieder-Beiträgen, alle Benachrichtigungskanäle, Marke, Datenschutz und Nachweis, Rollout-Werkzeuge, PWA, Redaktion für Tenant-Beiträge und Tenant-Inhalte durch Programm-Manager und Redakteur mit optionalem KI-Authoring, Verwaltung. Die Rolle Redakteur ist Kern; M10 fügt Firmenkanal-Rechte je Gruppe, Dokumente, Termine und Zielgruppen hinzu. Zwölf Module: M1 Challenges, M2 Arena (setzt M1 voraus), M3 Bewegung, M4 Ergonomie, M5 Wissen, M6 Regeneration, M7 Ernährung, M8 Wearables (buchbar nur mit vom Operator freigegebenem Hersteller), M9 Workshops und Events (setzt M1 voraus), M10 Firmenkanal, M11 Mehrsprachigkeit, M12 Verzeichnis und Netzwerk ohne Personenimport. Navigation aus Modulen mit höchstens fünf Bereichen. Bündel sind Vertriebsvorschläge, keine Entitlements. Katalog mit Datenschutzhinweis je Modul gemäß [Entitlements](entitlements.md), Abschnitt 2.
- **Begründung:** Der Kern enthält alles, was Rollout und Vertrauen tragen; Module sind Einheiten aus Funktion, Sichtbarkeit und Preis.
- **Nachweise:** Navigation je Modulkombination; kein Hinweis auf inaktive Module; M12 ohne Personenimport.

## A-065 – Entitlement-Modell

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Je Tenant und Modul höchstens ein aktiver Datensatz mit Zustand (Testphase, aktiv, auslaufend, inaktiv), aktiv_ab, aktiv_bis, test_bis, Quelle, Grenzwerten und append-only Historie. Testphase höchstens einmal je Tenant und Modul, Ereignisse erfasst und nicht bewertet, danach automatisch aktiv. Rahmen entlang Operator → Partner → Tenant mit erlaubten Modulen, Testphasen-Erlaubnis und -dauer (Voreinstellung 30 Tage) und Voreinstellungen bei Tenant-Anlage. Entfernen aus dem Rahmen lässt bestehende Entitlements zum übernächsten Monatsende auslaufen. Entitlements prüfen nie Zahlungen.
- **Begründung:** Trennung von Zugang und Verbrauch; Vererbung ohne Umbau; keine abrupten Programmabbrüche durch Rahmenänderungen.
- **Nachweise:** zweite Testphase abgelehnt; Rahmenentzug mit Auslauf und Benachrichtigung; Historie vollständig.

## A-066 – Buchung, Testphase und Kündigung

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Tenant-Admin bucht, testet (wenn erlaubt) und kündigt im Rahmen; Partner für eigene Tenants; Operator für alle und erzwungene Deaktivierung mit Grund und sieben Tagen Vorlauf. Buchungsablauf mit Vorschau, Datenschutzhinweis, Preis, Kostenauswirkung vor dem Klick, Bündelvorschlag statt Ablehnung bei Abhängigkeit, frischer Anmeldung, sofortiger Wirkung, keiner automatischen Ankündigung. Kündigung zum Monatsende; abhängige Module enden zum selben Termin; laufende Challenges enden zum Termin; Arena mit laufender Arena; Wearable-Verbindungen getrennt; danach 90 Tage Export, Verdichtung, Abzeichen bleiben.
- **Begründung:** Buchung ohne Überraschung, Kündigung ohne Datenverlust.
- **Nachweise:** Bündelvorschlag; Kündigungskaskade; Rücknahme vor Termin; erzwungene Deaktivierung im Protokoll.

## A-067 – Durchsetzung

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Navigation nur aus aktiven Entitlements; API-Prüfung im Autorisierungsquerschnitt mit Antwort „nicht gefunden“; Lesepfade inaktiver Module leer außer Tenant-Export für 90 Tage; keine neuen Feed-Karten und Benachrichtigungen; Jobs übersprungen und protokolliert; Challenge-Achsen und Inhaltskategorien nur mit Modul; Auswertung je Request mit 60 Sekunden Cache und sofortiger Invalidierung; Historie als Nachweis. Frontend-Darstellung ist keine Autorisierung.
- **Begründung:** Ein unsichtbares Modul darf auch über API und Daten nicht erkennbar oder nutzbar sein.
- **Nachweise:** Modulendpunkt ohne Entitlement liefert „nicht gefunden“; Buchung wirkt beim nächsten Request.

## A-068 – Funktionsfreigaben und Grenzwerte

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Funktionsfreigaben des Operators je Tenant, befristet auf höchstens 90 Tage, verlängerbar, protokolliert, nicht buchbar, nicht sichtbar, ohne Bewertung, für die Einsichtsrolle sichtbar. Grenzwerte als Betriebsschutz am Entitlement-Datensatz: Medienspeicher 50 GB, 50 Video-Uploads je Monat, 500 Dokumente, 5 Sprachen, 20 Kiosk-Geräte, KI-Aufrufe nach Tenant-Vorgabe oder 1.000; Überschreitung blockiert mit Hinweis; Operator ändert je Tenant; keine Preisstufen.
- **Begründung:** Vorabfreigaben ohne Vermischung mit dem Katalog; Schutz des Betriebs ohne Preislogik.
- **Nachweise:** Freigabe läuft aus; Grenzwert blockiert ohne Metering-Wirkung.

## A-069 – Metering-Ledger

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Append-only Ledger, je Periode partitioniert, mit Tenant, Modul, Metrik, anonymem Bezug, Menge mit vier Nachkommastellen, Quelle, fachlichem Zeitpunkt, Buchungsperiode, Idempotenzschlüssel aus Fachereignis und Metrik, Nachlauf-Kennzeichnung und Storno-Verweis; keine Klarnamen, keine Personen-IDs. Versiegelung am dritten Kalendertag des Folgemonats um 03:00 Tenant-Zeit; Nachläufer in die offene Periode; Testphasen-Ereignisse erfasst und nicht bewertet; Korrekturen als Gegenbuchung; Aufbewahrung sieben Jahre. Periodensalz je Tenant und Periode in OpenBao, Slots als schlüsselabhängiger Hash, Salz sieben Tage nach Versiegelung vernichtet.
- **Begründung:** Was nicht von Anfang an erfasst wird, lässt sich nicht rekonstruieren; Slots ohne Salz sind nicht rückrechenbar.
- **Nachweise:** Doppelübertragung ein Ereignis; Slot je Periode neu; keine Rückrechnung nach Salzvernichtung; versiegelte Periode unverändert.

## A-070 – Metrikkatalog und Emission

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Katalog gemäß [Metering und Abrechnung](metering-und-abrechnung.md), Abschnitt 3.1, mit Standardmetrik `member.active_month`; `wearable.tenant_month` nie je Verbindung; `ai.call` nur als Nachweis. Jede Domäne emittiert in derselben Transaktion wie die fachliche Änderung mit Idempotenzschlüssel, unabhängig vom Entitlement-Zustand; Korrekturen als Gegenbuchung; kein Ereignis mit Anzeigenamen, Personen-IDs, Gruppen oder persönlichen Messwerten. Neue Metriken sind Konfiguration plus Emissionsstelle.
- **Begründung:** Jede Entität emittiert von Beginn an, damit spätere Preisregeln auf vollständige Historie treffen.
- **Nachweise:** jede Domäne emittiert; Gegenbuchung bei Korrektur; kein Personenbezug im Ledger.

## A-071 – Aktives Mitglied

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Aktiv ist eine Person mit mindestens einer gewerteten Handlung im Monat mit Quelle selbst oder Plattform gemäß A-044. Nicht: Öffnen der App, Anmeldung, empfangene Benachrichtigungen, automatische Wearable-Tageswerte, Kommentare, Systemereignisse. Fortschritt emittiert je Person und Monat genau ein Ereignis mit Slot bei der ersten gewerteten Handlung; `member.active_day` analog je Tag. Definition im Vertrag, in der Verwaltung und an jeder Rechnungsposition.
- **Begründung:** Der Kunde zahlt für Beteiligung; Öffnen darf keinen Umsatz erzeugen, sonst hätte der Betreiber ein Interesse an Erinnerungen statt an Beteiligung.
- **Nachweise:** zehn Handlungen ergeben ein Ereignis; Öffnen, Push, automatischer Wert und Kommentar ergeben keines.

## A-072 – Preisregeln, Pläne und Rahmen

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Preisregel mit Geltung, Modul, Metrik, Modell, Parametern, Währung EUR, Periode, Gültigkeit und Priorität; zehn Modelle: flat, per_unit, tiered, volume, package, step, one_time, revenue_share, min_max, credit; min_max und credit als Modifikatoren, revenue_share auf die Ebene darüber. Preispläne je Tenant, versioniert, Änderungen nur mit Gültigkeit ab dem nächsten Monatsersten oder später; vergangene Perioden unveränderlich; Listenpreisplan des Operators als Vorlage; alle Beträge sind Konfiguration. Preisrahmen je Partner mit erlaubten Modellen, Betragsgrenzen, Rabatten und Provisionssatz; Rahmenverletzungen werden abgelehnt, Rahmenänderungen wirken ab dem nächsten Monatsersten mit Auslauf betroffener Pläne zum übernächsten Monatsende. Mengen und Einzelpreise mit vier Nachkommastellen, Rundung kaufmännisch auf zwei je Position; Proratierung tagesgenau einschließlich Flatrates; Testtage nicht bewertet.
- **Begründung:** Maximal variable Preise brauchen kombinierbare Modelle und einen Rahmen für Partner; Rundung je Position hält jede Zeile nachrechenbar.
- **Nachweise:** Beispielplan mit sechs Modellen nachrechenbar; Rahmenverletzung abgelehnt; Proratierung korrekt.

## A-073 – Kostenvorschau und Verbrauchsdetail

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Laufender Monat je Regel, Prognose als Schätzung, Simulation vor Buchung, Verbrauchsdetail ohne Ledger-Einzelzeilen: Mengen ohne Personenbezug je Metrik und Tag, personennahe Metriken (aktive Mitglieder, Teilnehmertage) nur als Monatssumme, Stufenwarnungen nach unten aktiv und nach oben zwingend, Testphasen-Anzeige, Klartextdefinitionen, Rechnungsarchiv sieben Jahre. Tagesaggregate je Tenant, Metrik und Tag als einzige Datenquelle für Tenant-Oberflächen und -Exporte, stündlich und bei Buchung aktualisiert. Programm-Manager, Botschafter und Einsichtsrolle sehen keine Kostenvorschau, Rechnungen oder Verbrauchsdetails; der Programm-Manager sieht Angebotspreise im Veranstaltungskatalog; Einsichtsrolle sieht verwendete Metriken ohne Beträge; Partner sehen Mengen und Beträge ihrer Pläne und ihre Provision.
- **Begründung:** Echtzeit-Transparenz macht variable Preise zum Vorteil; Aggregate schließen die Hintertür zur Auswertung.
- **Nachweise:** kein Einzelzeilenzugriff über Oberfläche, API oder Export; Simulation vor Buchung; Warnung bei Stufensprung.

## A-074 – Rechnungslauf, Steuern, Zahlung und Mahnwesen

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Versiegeln, Bewerten, Positionen mit Regel und Mengennachweis, Steuer je Position nach Steuerregel des Tenant-Landes (Steuersatz, Reverse Charge mit UID, Drittland; Konfiguration des Betreibers), Rechnungsentwurf mit fortlaufender Nummer je Rechtsträger und Jahr, Freigabe durch Operator-Admin mit optionaler automatischer Freigabe je Tenant, Versand als PDF, CSV und XML nach EN 16931, Zahlungseingang gebucht durch den Operator. Korrekturen nach Versand nur als Gutschrift oder Nachbelastung. Zahlungsweg Überweisung mit 14 Tagen Ziel; kein Zahlungsdienstleister, keine Karte, keine Lastschrift. Mahnwesen: Erinnerung Tag 7, Mahnung Tag 21, Sperrvorschlag Tag 45; Sperre nur durch Operator-Admin; während der Sperre Flatrates weiter, keine nutzungsabhängigen Mengen. Sonderfälle gemäß Export.
- **Begründung:** vertriebsgeführtes B2B ohne weiteren Unterauftragsverarbeiter; keine automatische Sperre gegen Belegschaften wegen Buchhaltungsfehlern.
- **Nachweise:** Rechnung in drei Formaten mit fortlaufender Nummer; Korrektur nur als Gutschrift; Mahnlauf ohne automatische Sperre.

## A-075 – Partnerabrechnung

- **Status:** angenommen, 20.09.2026.
- **Umfang:** Je Partner Direktabrechnung (Rechnungen an Tenants, monatliche Provisionsgutschrift nach Zahlungseingang) oder Sammelabrechnung (eine Rechnung an den Partner mit Positionen je Tenant, keine Tenant-Rechnungen, keine Provision). Partner sehen je Tenant Mengen und Beträge ihrer Pläne, nie Details unterhalb der Tagesaggregate. Modellwechsel ab dem nächsten Monatsersten; bei Partnerauflösung Umstellung der Tenants auf Direktabrechnung mit dem Operator.
- **Begründung:** Reseller fakturieren selbst, Konzern-Holdings wollen eine Rechnung; beides ohne Einblick in Mitgliederdaten.
- **Nachweise:** beide Modelle erzeugen korrekte Rechnungen und Gutschriften; Partner ohne Zugriff unterhalb der Aggregate.

## A-076 – Tokenmodell und Design-System

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Drei Ebenen ref → sys → Komponente; Komponenten nur mit `--ch-*`-Rollen, Rohfarben sind Lint-Fehler. Farbrollen mit Startwerten für hell und dunkel gemäß [Marke und Theme](marke-und-theme.md), Abschnitt 2.2; `team`, `success`, `warning`, `error` und Diagrammfarben plattformweit fest. Inter und Inter Tight selbst ausgeliefert, Typografie-Skala, 4-px-Raster, Radien, Erhöhung über Flächenton, Bewegung 120/200/320 ms mit reduzierter Bewegung, ein Erfolgsmoment ohne Konfetti, Layoutgrenzen 640 und 1024 px, kleines Komponenteninventar, Material Symbols Rounded, Bildsprache mit echten Arbeitsumgebungen. Farbe trägt nie allein Information; höchstens zwei Akzente je Screen; keine Verläufe.
- **Begründung:** Rollen statt Werte sind Voraussetzung für Tenant-Theming und Dunkelmodus ohne Sonderfälle; ein kleines Inventar begrenzt die Stellen, an denen Theming brechen kann.
- **Nachweise:** Rohfarbe scheitert am Lint; Referenzscreen in beiden Modi und drei Marken. **Technisch nachgewiesen** am 26.09.2026 (`durchstich/abnahme/stufe-5.md`).

## A-077 – Tenant-Theme-Kontrakt und Ableitung

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Einstellbar: Produktname, Logo hell und dunkel (SVG bereinigt oder PNG ab 512 px), Saatfarbe, optionaler Akzent mit Mindestabstand, Anrede, Tonalität, Startbild, Willkommenstext, Bezeichnungen für Punkte, Serie, Stufen, Sprachen mit Modul; die Bezeichnungen der Gruppendimensionen gehören zu Organisation (A-034). Nicht einstellbar: Schriften, Abstände, Radien, Formen, semantische und Teamfarben, Diagrammfarben, Navigation, Datenschutzregeln, Systemtexte, Erfolgsmoment, Icon-Stil. Versioniertes JSON-Dokument mit Schemaversion und Backend-Validierung. Ableitung nach Material-3-Verfahren im HCT-Farbraum für hell und dunkel, `progress` mit erzwungenem Farbtonabstand und nie in Blau, WCAG-2.2-AA-Prüfung aller Rollenpaare, Saatfarbe immer angenommen und nur dort ersetzt, wo sie durchfällt, Ersetzungsliste mit Begründung, Standard-Theme bei ungültiger Eingabe. Tokensatz versioniert, Laufzeitwechsel ohne Neuladen, Dokumente mit Version zum Erzeugungszeitpunkt, zwölf Monate Historie. Verwaltung mit Live-Vorschau in beiden Layouts und Modi, Kontrastbericht, Veröffentlichung als sensible Aktion.
- **Begründung:** Die Firma ist der Absender und darf ihre Farbe nie verlieren; Lesbarkeit und Layoutstabilität bleiben Plattformeigenschaft.
- **Nachweise:** orange Saatfarbe ergibt kontrastierende Fortschrittsfarbe; alle Paare bestehen; ungültiges Theme fällt auf Standard zurück. **Technisch nachgewiesen** am 26.09.2026 (`durchstich/abnahme/stufe-5.md`); Verfahren in A-110. Logo-Upload, Startbild und Live-Vorschau in beiden Layouts folgen mit der Medienverarbeitung und der Verwaltung.

## A-078 – Plattformmarke, Standard-Theme und Co-Branding

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Operator pflegt Plattformname, Wortmarke, Bildzeichen (aufsteigender Bogen aus drei Segmenten in `primary` und `progress`) und Standard-Theme; Namenswechsel ohne Codeänderung; Markenrecherche Betreibersache. Zwei Geltungsbereiche: Tenant für alle Bereiche innerhalb der Firma einschließlich Verwaltung und Kiosk; Plattform für Arena, Partner-Konsole, Operator-Konsole und Anmeldeseite vor Tenant-Zuordnung, mit Tenant-Logo als gleichrangigem Teilnehmerkennzeichen. Sichtbarer Wechsel über eigene Kopfzeile, Rückkehr beim Verlassen, kein Doppel-Logo, Overlays folgen. `system | light | dark` je Person, beide Modi gleichwertig gestaltet.
- **Begründung:** White-Label innen und Plattformmarke dort, wo sie Wert stiftet, den der Tenant allein nicht erzeugen kann.
- **Nachweise:** Wechsel Firma ↔ Arena mit Overlay-Schluss und unveränderter Sitzung.

## A-079 – PWA-Manifest je Tenant

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Dynamisches Web-App-Manifest je Tenant unter tenant-spezifischem Pfad auf derselben Origin: Name und Kurzname aus dem Produktnamen, Farben aus dem Tokensatz, `start_url`, `scope` und `id` je Tenant, Icons in allen Größen einschließlich maskierbarer Varianten aus dem Tenant-Logo oder ersatzweise aus dem Bildzeichen in Tenant-Farbe. Kiosk mit eigenem Manifest je Gerät im Vollbild. Anmeldeseite vor Zuordnung in Plattformmarke; nach Zuordnung Wechsel von Theme und Manifest-Verweis. Service Worker und Cookie-Sitzung unverändert.
- **Begründung:** Jede Firma soll auf dem Startbildschirm unter ihrem Namen erscheinen, ohne eine zweite Origin und ohne Verlust der gemeinsamen Sitzung.
- **Nachweise:** installierte App zeigt Tenant-Name und -Symbol; zwei Tenants auf einem Gerät unterscheidbar; Kiosk im Vollbild. Nachweisstand: Manifest je Tenant mit eigener `id`, `start_url` und `scope`, Name, Farben und Icons (PNG 192, 512, maskierbar; SVG) aus dem Tokensatz, Kiosk-Manifest mit `display: fullscreen`, Manifestverweis nach Zuordnung, Isolation gegen fremde Tenant-Pfade technisch nachgewiesen am 26.09.2026 (`durchstich/abnahme/stufe-5.md`); die Installation auf einem Gerät mit zwei Tenants ist eine manuelle Prüfung auf Staging und steht aus.

## A-080 – Tonalität, Sprache und Textkatalog

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Drei Tonalitätsstufen (sachlich, freundlich, motivierend) wirken auf alle generierten Texte. Operator pflegt je Textschlüssel drei Varianten je Sprache mit Platzhaltern; Anrede Du und Sie je Variante ausgearbeitet; Tenants wählen Stufe und Anrede und ersetzen Bezeichnungen, schreiben aber keine Systemtexte; Fallback auf Tenant-Standardsprache, dann Deutsch; Katalog versioniert, nie rückwirkend auf gespeicherte Einträge. Sprachregeln: nie Schuld, nie Vergleich, nie Nichtteilnahme; Ausrufezeichen nur begrenzt; Sperrliste mit Gesundheitsdaten, Tracking, Monitoring, Auswertung, Überwachung, Krankenstand, Fehlzeiten, Leistung, Ranking als Oberflächenbegriffe; Nutzersprache in Titeln; Landesformat für Zahlen.
- **Begründung:** Tonalität ist Markeneigenschaft des Tenants, die Sprachregeln sind Vertrauenseigenschaft der Plattform.
- **Nachweise:** Tonalitätswechsel wirkt sofort auf neue Texte; Sperrbegriff im Katalog abgelehnt. Nachweisstand: für Systemtexte der Oberfläche und die Sperrliste des Katalogs technisch nachgewiesen am 26.09.2026 (`durchstich/abnahme/stufe-5.md`); Benachrichtigungen, Aushang und Feed-Einträge folgen mit Stufe 6.

## A-081 – Barrierefreiheit und Großflächenmodus

- **Status:** angenommen, 21.09.2026.
- **Umfang:** WCAG 2.2 AA vollständig; Kontrast 4,5:1 und 3:1 in jedem Theme durch die Ableitung; Bedienflächen 48 px mit 8 px Abstand; Großflächenmodus als persönliche Einstellung im Onboarding mit 56 px, zwei Textstufen größer, verstärkten Kontrasten und reduzierter Dichte, am Kiosk immer aktiv; vollständige Tastaturbedienung mit sichtbarem Fokusring; Screenreader-Texte für jeden Fortschritt, Live-Regionen, Textalternativen, Untertitel und Transkripte; reduzierte Bewegung; Prüfpunkte im Referenzscreen und in visuellen Tests; kein Tenant-Theme kann sie unterlaufen.
- **Begründung:** Die Zielgruppe umfasst alle Beschäftigten, einschließlich Produktion mit Handschuhen und älterer Nutzer.
- **Nachweise:** gemessene Flächen 56 px im Großflächenmodus; Kontrast in allen Themes; Tastatur- und Screenreader-Durchlauf des Referenzscreens. Nachweisstand: gemessene Flächen 48 und 56 px, Text zwei Stufen größer, Kontrast in allen Themes durch die Ableitung, Tastaturdurchlauf mit Sprungmarke, Fokusring und Dialog, reduzierte Bewegung technisch nachgewiesen am 26.09.2026 (`durchstich/abnahme/stufe-5.md`); Textentsprechungen und Live-Regionen sind umgesetzt, der Durchlauf mit echter Hilfstechnik steht aus.

## A-082 – Workshops und Events: Angebotsmodell

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Eigene Domäne für Modul M9 mit Anbietern (Eigenleistung des Betreibers, externe Anbieter mit Provision, tenant-interne), Angeboten mit Format, Kapazität, Preismodell und Stornobedingungen, Veranstaltungen je Tenant mit Geltungsbereich, Stationen für Gesundheitstage, Anmeldungen mit Ticket-Token und Gutscheinen. Katalog auf drei Ebenen (Plattform, Partner, intern); Angebote ohne verfügbaren Anbieter in der Region werden nicht angezeigt. Formate Präsenz, Online im Werkzeug des Anbieters mit Link nur für Angemeldete ab 60 Minuten vorher, Hybrid, Gesundheitstag mit Stationen, interne Veranstaltungen ohne Kosten durch Programm-Manager oder Botschafter. Kein Anbieter-Login; Kommunikation per E-Mail, Zustandspflege durch Operator und Organisator; Anbieterabrechnung außerhalb der Plattform mit monatlicher Übersicht für den Operator.
- **Begründung:** Das Modul liefert die einzige nicht kopierbare Belohnung und den höchsten Deckungsbeitrag; ein Anbieter-Login wäre ein weiterer Organisationstyp ohne Bedarf zum Start.
- **Nachweise:** Gesundheitstag mit Stationen; Online-Link erst 60 Minuten vorher; keine Anbieterrolle in der Plattform.

## A-083 – Buchung durch den Tenant

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Ablauf Auswahl mit Angebotspreis und Stornobedingungen durch Programm-Manager oder Tenant-Admin, Anfrage an Anbieter und Operator, Terminbestätigung durch Operator-Admin, für Partneranbieter Partner-Admin, für interne Anbieter den Organisator, mit sieben Tagen Haltefrist, verbindliche Buchung durch Tenant-Admin als sensible Aktion, Veröffentlichung durch den Organisator, Check-in, Abschlussmeldung innerhalb von sieben Tagen mit automatischer Durchführung nach 14 Tagen. Organisator ist Programm-Manager oder benannte Person mit Rolle Botschafter oder Redakteur. Storno bis 14 Tage kostenfrei, danach Stornosatz aus dem Angebot (Voreinstellung 50 %, innerhalb 48 Stunden 100 %); Absage wegen Mindestteilnehmerzahl kostenfrei nach Entscheidung des Organisators; Absage durch Anbieter kostenfrei. Anmeldezahlen in der Verwaltung erst ab fünf, darunter keine Zahl; der Organisator sieht die Anmeldeliste seiner Veranstaltung. Kostenvorschau, Kosten und Rechnungspositionen nur für den Tenant-Admin; der Programm-Manager sieht Angebotspreise.
- **Begründung:** verbindliche Buchung mit Kosten vor dem Klick; Stornoregeln transparent; keine Teilnehmerzahlen unter fünf außerhalb der Organisation.
- **Nachweise:** Buchung nur mit frischer Anmeldung; Stornobetrag und kostenfreie Absage korrekt im Metering.

## A-084 – Anmeldung der Mitglieder

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Kalender im Bereich Firma; Anmeldung mit einem Tap, je Station bei Gesundheitstagen; Warteliste in Reihenfolge mit 24 Stunden Bestätigungsfrist beim Nachrücken; Abmeldefrist 24 Stunden; Erinnerungen 24 Stunden und 1 Stunde vorher; ICS; Kiosk-Anmeldung. Ticket-QR je Anmeldung, Scan zeigt nur gültig oder ungültig; Kiosk-Kennung als Ticketersatz; Check-in erzeugt Handlung „Veranstaltung besucht“ mit Deckel eins je Tag, Selbstbestätigung bis 48 Stunden danach. Klarname nur mit Freigabe je Anmeldung; Anmeldeliste nur für den Organisator mit Anzeigenamen und Anzahl der Check-ins, ohne Check-in-Status je Person, nicht exportierbar; Anbieter nur Anzahl; Anmeldedaten 30 Tage nach Veranstaltung gelöscht; Anmeldezahlen erst ab fünf, darunter keine Zahl.
- **Begründung:** pseudonym bis zur Tür; Teilnahme an einem Gesundheitsworkshop ist keine Auswertungsgrundlage.
- **Nachweise:** Scan ohne Namen; Liste nicht exportierbar und nach 30 Tagen gelöscht; Handlung ohne Messwert.

## A-085 – Verdiente Belohnung

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Workshop-Gutschein als Belohnungsobjekt im Challenge-Block Belohnung: Angebot oder interne Veranstaltung, Bedingung (100 % oder Meilenstein), Geltungsbereich, Kostenmodell „vom Tenant zugesagt“ oder „im Preisplan enthalten“ innerhalb von Preisplan und Partnerrahmen; der Programm-Manager verknüpft, der Tenant-Admin gibt die Kostenwirkung als sensible Aktion mit Kostenvorschau vor dem Challenge-Start frei, ohne Freigabe startet die Challenge ohne Gutschein. Freischaltung durch Challenge-Ereignis mit Feed-Karte, Benachrichtigung und Aushang-Vorlage; Gültigkeit sechs Monate mit Erinnerung 30 Tage vor Ablauf; Einlösung als Buchung mit Vorbelegung. Offen für alle im Geltungsbereich, keine Vorränge, keine Auslosung nach Beiträgen, keine individuellen Gutscheine.
- **Begründung:** Belohnungen gelten immer allen, sonst entsteht ein Nachteil für Nichtteilnehmende (A-021).
- **Nachweise:** Freischaltung bei Bedingung; Nichtteilnehmende können sich anmelden; beide Kostenmodelle im Metering korrekt; Ablauf mit Erinnerung.

## A-086 – Rückmeldung, Auswertung, Benachrichtigungen und Metering für Veranstaltungen

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Anonyme Rückmeldung mit drei Skalenfragen, Aggregat ab fünf für Organisator und Operator, Freitext nur an den Operator mit Löschung nach 12 Monaten; keine Trainerbewertung in der App. Auswertung nach Rollen ohne Nichtteilnehmerlisten. Benachrichtigungskategorie Veranstaltungen. Metriken `event.booking` als gebuchte Plätze, `event.delivered`, `event.cancellation`, `workshop.credit_redeemed`; nie Teilnehmer, Check-ins oder Anmeldungen als Metrik; Provisionen über `revenue_share`.
- **Begründung:** Qualitätssicherung ohne Personenbezug; Rechnungen ohne Teilnehmerzahlen.
- **Nachweise:** Rückmeldung anonym und ab fünf; Freitext nur beim Operator; Rechnung zeigt Plätze und Termine.

## A-087 – Arena-Dienst und Isolation

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Eigener Dienst im selben Stack mit eigener Codebasis, eigenem Image, eigenem Compose-Projekt, eigener PostgreSQL-Datenbank und eigenen Zugangsdaten; nur intern erreichbar, ausschließlich vom Monolithen; Dienstzugangsdaten je Richtung in OpenBao. Nur der Monolith spricht mit dem Arena-Dienst: Definitionen, Beitritte, Projektionen und Löschaufträge per idempotenten Jobs hinein; Stände und Ereignisse über die interne API heraus, vom Monolithen je Tenant gecached. Der Dienst kennt Tenants nur als Kennung mit Name, Logo, Größenklasse, Region und Branche und keine Personen; Einzelwerte tragen ein zufälliges Arena-Token je Person und Arena. Betreiber ist Verantwortlicher.
- **Begründung:** Ein versehentlicher Join über Tenants hinweg wäre der teuerste denkbare Fehler; Isolation in der Infrastruktur statt im Code-Review.
- **Nachweise:** kein gegenseitiger Datenbankzugriff; keine öffentliche Route; keine Personen-IDs in der Arena-Datenbank.

## A-088 – Arena-Modell und Lebenszyklus

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Arenen definiert der Operator: Zeitraum vier bis zwölf Wochen, Metrik, Normalisierung immer pro Kopf auf Sollstärke zum Start, Formen Firmenrangliste, gemeinsames Sammelziel, Ligaduell; Größenklassen bis 50, 51 bis 250, 251 bis 1.000, über 1.000, optional Region oder Branche; Ligen erst ab vier Tenants je Klasse, sonst offene Arena; Start nur mit mindestens vier Tenants, sonst Verschiebung bis vier Wochen oder Absage. Zustände Entwurf, Anmeldung offen, Laufend, Nachfrist 48 Stunden, Beendet, Archiviert nach Zeitraum plus 12 Monaten mit Löschung der Tokens. Stündliche Snapshots, nie live. Keine Preise; Anerkennung als Feed-Karte und Abzeichen „Firmenziel“ für alle im Tenant. Region und Branche als optionale Tenant-Stammdaten.
- **Begründung:** Eine Liga mit einem Teilnehmer ist schlimmer als keine Liga; Pro-Kopf auf Sollstärke belohnt Beteiligung.
- **Nachweise:** Arena mit drei Tenants startet nicht; Ligen ab vier je Klasse; Sollstärke-Änderung ohne Wirkung auf laufende Arena.

## A-089 – Teilnahme, Projektion und Anzeige

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Beitritt je Arena durch Tenant-Admin als sensible Aktion mit Abschnitt „Was verlässt unsere Firma?“ und Wahl der Einzelränge; Beitritt erzeugt eine gespiegelte Challenge im Tenant mit Sichtbarkeit Arena und automatischem Beitritt mit Widerspruch. Projektion stündlich durch Challenges: Pro-Kopf-Wert auf eine Nachkommastelle, Größenklasse, Zeitpunkt, Status wertend ab fünf Beitragenden sonst „nimmt teil“, Einzelwerte nur bei doppelter Zustimmung mit Arena-Token, Anzeigename und Wert; nie Summen, Teilnehmerzahlen, Quoten oder Gruppenwerte. Anzeige in Plattform-Schale mit eigener Kopfzeile, eigene Firma hervorgehoben, Einzelrangliste nur mit Operator-Erlaubnis und ab fünf zustimmenden Personen; Arena-Ereignisse als Feed-Karten und Benachrichtigungen im Tenant. Persönliche Arena-Zustimmung standardmäßig aus, beim ersten Öffnen angeboten, jederzeit widerrufbar mit Wirkung bei der nächsten Projektion. Metering `arena.entry` und `arena.size_class`.
- **Begründung:** Summenwert plus Pro-Kopf-Wert ergäbe die Teilnehmerzahl und damit die Beteiligungsquote; deshalb verlässt nur ein Wert den Tenant.
- **Nachweise:** Tenant unter fünf ohne Wert; Einzelwert nur bei doppelter Zustimmung; Widerruf wirkt; Plattform-Schale ohne Sitzungswechsel.

## A-090 – Wearable-Vault: Dienst, Isolation und Subjektmodell

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Eigener Dienst im selben Stack mit eigenem Image, Compose-Projekt und Netz, eigener PostgreSQL-Datenbank mit eigenen Zugangsdaten, Rohdaten feldverschlüsselt mit Vault-eigenem Schlüssel in eigenem OpenBao-Namensraum. Eingehend nur OAuth-Callback und Webhooks der Hersteller mit Signaturprüfung sowie interne Aufrufe des Monolithen; ausgehend nur Hersteller-Endpunkte über Allowlist mit Alarm bei Verstoß. Der Vault kennt keine Personen: opaker Subjektschlüssel je Verbindung, Zuordnung nur im Monolithen feldverschlüsselt. Logs ohne Nutzdaten; kein Support-Zugriff auf Vault-Inhalte; Backup mit Nachlöschung.
- **Begründung:** Die Auflagen der Herstellerprogramme verlangen eine Grenze, die in der Infrastruktur steht; ein kompromittierter Vault enthält Rohdaten ohne Personen, ein kompromittierter Monolith Personen ohne Rohdaten.
- **Nachweise:** keine Personen-IDs im Vault; Allowlist-Verstoß alarmiert; Rohdaten ohne Schlüssel unlesbar; Support erreicht den Vault nicht.

## A-091 – Hersteller, Verbindung, Datenumfang und Ableitung

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Adaptermodell mit bestätigten Startadaptern Garmin, Fitbit, Polar, Withings; Freischaltung je Hersteller durch den Operator erst nach Programmzugang und Datenschutzerklärung mit KI-Aussage; M8 nur mit freigeschaltetem Hersteller. Eine Verbindung je Person; OAuth 2.0 im Systembrowser mit minimalen Scopes; Backfill sieben Tage gedeckelt und nur in laufenden Challenge-Zeiträumen; Webhooks plus täglicher Abruf um 03:00 und manueller Anstoß höchstens alle 15 Minuten. Bezogen werden nur Tagesübersichten (Schritte, aktive Minuten, Distanz) und Schlafdauer; nie Herzfrequenz, Positionen, Aktivitätsdetails, Körperwerte. Export aus dem Vault je Tag: Schritte auf 100 gerundet und auf 25.000 gedeckelt, aktive Minuten auf 5 gerundet und auf 300 gedeckelt, Distanz auf 0,5 km gerundet und auf 30 km gedeckelt, Schlafziel ab sieben Stunden nur als ja oder nein, Datenstand; Tagesabschluss 03:00 mit 48 Stunden Nachfrist; Rohdaten nach 90 Tagen gelöscht. Verwendung in Challenges als Erfassungsart automatisch und in Fortschritt als höchstens eine Handlung je Metrik und Tag; nicht aktives Mitglied. Apple Health und Health Connect nicht anbindbar; eine native Brücken-App ist nicht eingeplant und wäre eine eigene Registerentscheidung.
- **Begründung:** Quantisierte Tagessummen ermöglichen Schritte- und Minuten-Challenges ohne Zeitreihen; minimale Scopes erleichtern den Programmantrag.
- **Nachweise:** Scopes ohne Herzfrequenz und Position; Export nur mit fünf Feldern; Schlaf nur binär; Backfill begrenzt; doppelter Webhook ohne Doppelwirkung.

## A-092 – Transparenz, Rechte und Löschung im Vault

- **Status:** angenommen, 21.09.2026.
- **Umfang:** „Ich → Geräte“ mit Hersteller, Zustand, letzter Übertragung, bezogenen Metriken, Synchronisieren, Trennen, Rohdaten-Export; keine Geräteverwaltung am Kiosk. Selbstexport enthält abgeleitete Werte; Rohdaten-Export der letzten 90 Tage als Datei über signierten, 24 Stunden gültigen Verweis, danach im Vault gelöscht. Trennen löscht Rohdaten binnen 30 Tagen und widerruft Tokens; Austritt löscht sofort Tokens, Rohdaten, Subjektschlüssel und Zuordnung; Tenant-Kündigung löscht mit der Tenant-Löschung; Herstellerwiderruf wird erkannt. Keine Rolle sieht Verbindungen; Einsichtsrolle sieht nur aktive Hersteller; Metering nur `wearable.tenant_month`. Programmantrag erst nach Datenschutzerklärung, Scopes und Isolationsnachweis; Herstellerentzug deaktiviert den Hersteller mit Benachrichtigung; jährliche Prüfung der Herstellerbedingungen als Registerentscheidung.
- **Begründung:** Transparenz und Trennbarkeit erzeugen Vertrauen und reduzieren Support; Verbindungen dürfen für den Arbeitgeber unsichtbar bleiben.
- **Nachweise:** Trennen, Austritt, Herstellerwiderruf und Exportablauf gemäß [Wearable-Vault](wearable-vault.md), Abschnitt 6.

## A-093 – Beitritt einer Person

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Zwölf Schritte vom QR-Code bis zur Push-Erklärung gemäß [Onboarding und Rollout](onboarding-und-rollout.md), Abschnitt 2: Code, Tenant-Vorschau, Anzeigename mit Vorschlag, Sprache bei M11, Gruppe je Dimension, Sichtbarkeit ohne Voreinstellung, Zugang sichern, Großflächenmodus, erste Handlung (Kickoff-Beitrag oder Check-in, mit M3 Bewegungspause als Angebot), erstes Abzeichen, Installation, Push. Zielwert höchstens 90 Sekunden bis zum Start der ersten Handlung. Kiosk-Variante mit Kennung, PIN und Übertragung per QR. Fehlende Schritte später als verwerfbare Feed-Karten höchstens einmal je Woche.
- **Begründung:** Beitritt ohne Firmen-E-Mail entscheidet, ob die Hälfte der Zielgruppe teilnehmen kann.
- **Nachweise:** Beitritt unter 90 Sekunden auf Mittelklassegerät; Sichtbarkeit ohne Voreinstellung; kein Nullwert nach dem Beitritt.

## A-094 – Tenant-Einrichtung und Rollout-Choreografie

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Geführter Programmstart mit Checkliste nach Einlösung des ersten Tenant-Admin-Rollencodes; Pflichtschritte Stammdaten, Gruppen und Sollstärken, Marke, Anmeldewege, Module, Programm-Manager, Kickoff-Challenge, Beitrittscodes, Aushang, Vorschau, Starttermin; Botschafter empfohlen je 50 Personen mit 10-Minuten-Onboarding und drei Beiträgen vor Tag 0. Choreografie: Vorbereitung, Botschafter eine Woche vorher, synchroner Start Tag 0, zwei Wochen Ereignisse, Auswertung Tag 15 ohne Gesundheitsbericht, monatlicher Rhythmus ab Woche 3. Rollout erst nach erledigten Pflichtschritten freigebbar.
- **Begründung:** Ein Admin vor leerer Verwaltung startet nichts; ein leerer Feed am Tag 1 ist schlimmer als kein Feed.
- **Nachweise:** Rollout ohne Pflichtschritte nicht freigebbar; Feed am Tag 0 mit Botschafter-Beiträgen.

## A-095 – Rollout-Werkzeuge und Kennzahlen

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Aushang-PDF je Code, wöchentlicher Aushang-Zettel, Ankündigungstexte in drei Tonalitäten, Checkliste, Kiosk-Einrichtung, Vorschau als Person, Beiblatt für den Betriebsrat aus den Nachweisdokumenten. Kennzahlen als Produktziele je Tenant, Aggregate ab fünf, Bezug Sollstärke als Schätzgröße: Aktivierung 60 % in 14 Tagen, ohne E-Mail 30 %, Retention 40 % nach vier und 25 % nach zwölf Wochen, Challenge-Teilnahme 50 %, Installation mit Push 50 %, 90 Sekunden; nie sichtbarkeitssteuernd. Zwölf verbotene Muster aus Entwurf und Umsetzung ausgeschlossen.
- **Begründung:** Nutzungskennzahlen statt Gesundheitskennzahlen; die Muster stammen aus konkreten Befunden bei Wettbewerbern.
- **Nachweise:** Rollout-Fortschritt nur ab fünf und als Schätzgröße gekennzeichnet; kein verbotenes Muster im Referenzscreen.

## A-096 – Verwaltung des Tenants

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Eine Verwaltung mit Bereichen je Rolle gemäß [Verwaltung und Konsolen](verwaltung-und-konsolen.md), Abschnitt 2: Übersicht ohne Gesundheitskennzahlen, Programmstart, Mitglieder und Gruppen, Challenges, Inhalte, Feed, Veranstaltungen, Benachrichtigungen, Module und Kosten, Marke und Sprache, Zugang, Datenschutz nur lesend, Prüfprotokoll, Einsicht. Gemeinsame Regeln: Rechtematrix, frische Anmeldung für sensible Aktionen, Prüfprotokoll, keine Einzelwerte, Aggregate ab fünf, Vorschau vor Start, Kosten vor dem Klick, keine ausgegrauten Module, desktop-first und responsiv, „Meine Aufgaben“ in der Mitglieder-App für Botschafter und Organisatoren. Getrennter Feature-Einstieg, nie im ersten Ladepaket.
- **Begründung:** Firmenvertreter und Mitglied sind verschiedene Personen mit verschiedenen Jobs; eine Verwaltung mit Rollenbereichen statt vieler Oberflächen.
- **Nachweise:** kein Verwaltungscode im ersten Ladepaket; jede „–“-Zelle unsichtbar und abgelehnt; Botschafter-Aufgaben in der Mitglieder-App.

## A-097 – Partner-Konsole

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Bereiche Tenants, Rahmen, Vorlagen und Inhalte, Abrechnung, Prüfprotokoll; Plattform-Schale; nie Mitgliederlisten, Inhalte, Challenges, Quoten, Gruppen oder Sollstärken der Tenants.
- **Begründung:** Partner brauchen Anlage und Rahmen, nicht Einblick (A-037).
- **Nachweise:** Zugriffsversuch auf Tenant-Mitglieder oder -Quoten abgelehnt.

## A-098 – Operator-Konsole

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Bereiche Organisationen, Module und Preise, Abrechnung, Arena, Vorlagen und Inhalte, Textkatalog und Marke, Rechtstexte und Betreiberdaten, Anbieter, Hersteller, Anmeldeanbieter, E-Mail, Support mit Vorgangsnummer, Betrieb; eigener Feature-Einstieg unter derselben Origin in Plattform-Schale; Operator-Rollen mit Passkey oder externem Anbieter.
- **Begründung:** eine Konsole für Betreiben, ohne eigene Anwendung und ohne Zugriff jenseits von A-025.
- **Nachweise:** Support-Zugriff nur mit Vorgangsnummer und im Protokoll; kein Zugriff auf Einzelwerte oder Vault.

## A-099 – M3 Bewegung

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Übungen und Kurzvideos 2 bis 15 Minuten mit Körperregionen, Situation einschließlich im Sitzen, Dauer, Intensität, Hilfsmitteln und Kontraindikationshinweis; Schnelleinstieg über Situation und Körperkarte zu drei Vorschlägen; Wochenplan über Erinnerungsfenster; Sammlungen für Einstieg, Schreibtisch, Produktion, Schicht, Rücken, Nacken und Schultern; Tenant-Videos; Handlung bei Erledigt oder 90 %.
- **Begründung:** Situationsfilter vor Fachsprache; ohne Gerät und ohne Schreibtisch nutzbar.
- **Nachweise:** drei Vorschläge ohne Fachbegriff; Variante im Sitzen sichtbar.

## A-100 – M4 Ergonomie

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Fünf Arbeitsplatztypen, geführter Check mit Maßangaben in 5 bis 8 Minuten, Ergebnis mit drei Empfehlungen und Kategorien je Schritt, Wiederholung nach drei Monaten über Erinnerungsfenster; Ergebnis feldverschlüsselt, 12 Monate, selbst löschbar; Aggregat für den Arbeitsschutz je Gruppe ab fünf Checks nur als Anzahl und Anteil je Kategorie, ohne Maßwerte; Standardhinweis, dass der Check keine arbeitsmedizinische Beurteilung ist.
- **Begründung:** Arbeitsschutz braucht Anteile, keine Personen; Ergonomie ist persönlich.
- **Nachweise:** Aggregat erst ab fünf ohne Maßwerte; Einzelergebnis nur für die Person.

## A-101 – M5 Wissen

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Artikel mit Lesezeit und Quellen, Kurzfakt des Tages als eine Inhaltskarte, Quiz mit Erklärung und Bestehen ab 70 % mit Handlung einmal je Quiz, Lese-Serie, Autorenprofile der Redaktion; Ergebnisse nur für die Person; keine Quiz-Ranglisten außerhalb einer Challenge; Auswertung ab fünf.
- **Begründung:** Wissen ohne Wissenswettbewerb.
- **Nachweise:** Quiz-Handlung einmal je Quiz; keine Rangliste außerhalb einer Challenge.

## A-102 – M6 Regeneration und Stimmungs-Check-in

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Atemübungen mit Animation und Mustern, Audio 2 bis 15 Minuten, Sammlungen je Schichtmodell. Stimmungs-Check-in mit fünf Symbolen ohne Zahlen und optionalen Stichworten, einmal je Tag, ausschließlich privat, feldverschlüsselt, 12 Monate, selbst löschbar, in keinem Aggregat auch nicht anonym, ohne Sichtbarkeitsstufe, Feed-Karte oder Benachrichtigung, ohne Trendbewertung oder automatisierte Einordnung; statischer Hinweis auf vom Tenant-Admin hinterlegte Hilfsangebote ohne Auslösung durch Werte; erzeugt keine Handlung, kein Aktivitätsereignis und kein Metering-Ereignis; nie Challenge-Metrik. Kein Frühwarnsystem, keine Belastungsauswertung, keine KI-Einordnung.
- **Begründung:** Sobald Stimmungsdaten aggregiert Richtung Arbeitgeber fließen, ist die Vertrauensbasis weg und das Modul tot.
- **Nachweise:** Stimmungs-Check-in in keiner Auswertung, keinem Tenant-Export, keiner Karte; unlesbar ohne Schlüssel.

## A-103 – M7 Ernährung

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Rezepte mit Zutaten, Schritten, Zeit, Portionen und Hinweisen, Zubereitungsvideos, Gewohnheitsvorlagen als Häkchen-Challenges, Trinkerinnerung über Erinnerungsfenster ohne Zähler, Tenant-Rezepte; keine Kalorien, Gewichtsziele, Ernährungs-Scores, Diätpläne oder Nährwertampeln.
- **Begründung:** Ernährung ohne Körperbild-Metriken (A-021).
- **Nachweise:** Rezepte ohne Kalorienangabe; keine Zählfunktion.

## A-104 – Mehrsprachigkeit

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Standardsprache je Tenant ohne Modul; mit M11 bis zu fünf aktive Sprachen aus vollständig übersetzten Oberflächensprachen: Deutsch, Englisch, Türkisch, Bosnisch/Kroatisch/Serbisch als eine Variante, Rumänisch, Ungarisch, Slowakisch, Polnisch. Sprache der Person im Beitritt und Profil; Kiosk mit Sprachwahl je Sitzung. Übersetzung: Oberfläche und Textkatalog durch den Operator, Plattforminhalte redaktionell durch den Betreiber, Tenant-Inhalte durch die Tenant-Redaktion optional mit KI-Authoring über eigene Zugangsdaten; Fallback auf Standardsprache mit Kennzeichnung; Rechtstexte mit deutscher Fassung als verbindlich; Aushang in bis zu drei Sprachen; Sperrliste je Sprache; Übersetzungsstand in der Redaktion; Kündigung wirkt zum Monatsende mit Rückfall auf Standardsprache.
- **Begründung:** Sprache entscheidet in Produktion, Reinigung, Pflege und Bau über Teilnahme oder Ausschluss; unvollständige Sprachen wären ein halbes Produkt.
- **Nachweise:** unvollständige Sprache nicht aktivierbar; sechste Sprache abgelehnt; Fallback gekennzeichnet.

## A-105 – Verzeichnis und Netzwerk

- **Status:** angenommen, 21.09.2026.
- **Umfang:** SCIM-2.0-Empfänger mit Token in OpenBao; aus dem Verzeichnis werden nur Gruppen mit Mitgliederanzahl für Sollstärken mit Stichtag und je Benutzer `externalId` und `active` verwendet; alle anderen Attribute werden verworfen, es entsteht keine Person. Deaktivierungssignal beendet Sitzungen und entfernt die Anbieterverknüpfung; ohne anderen Anmeldeweg automatischer Austritt nach 30 Tagen, Reaktivierung innerhalb der Frist stellt wieder her. IP-Allowlist nur für Verwaltung und Verwaltungsrollen, nie für Mitglieder-App, Kiosk, Beitritt oder Anmeldung; selbstaussperrende Regeln abgelehnt. Prüfprotokoll-Export täglich als Datei und optional als signierter HTTPS-Push.
- **Begründung:** Großkunden verlangen Verzeichnis und Netz; der anonyme Beitritt bleibt unberührt.
- **Nachweise:** SCIM-Benutzer erzeugt keine Person; Deaktivierung mit 30-Tage-Frist; IP-Regel ohne Wirkung auf die Mitglieder-App.

## A-106 – Technischer Durchstich

- **Status:** angenommen, 21.09.2026.
- **Umfang:** Schmalster Pfad durch Zugang, Organisation, Marke, Entitlements mit M1, Challenges, Fortschritt, Feed, Benachrichtigungen, Metering und Datenschutz mit zwei Tenants in Compose; ohne Inhaltsmodule, Arena, Vault, Veranstaltungen, Firmenkanal, Mehrsprachigkeit, Verzeichnis, Partner-Konsole, KI-Authoring, Rechnungsversand, Mahnwesen. Acht Stufen in fester Reihenfolge (Fundament, Isolation, Queue und Idempotenz, Zugang, Vertrag und Oberfläche, Fachpfad, Geld, Onboarding), Stufen 4 und 5 parallel erlaubt; jede Stufe mit grüner CI und Abnahmeprotokoll. Vier Produktwahlen mit Version ins Register. Abschluss: alle Nachweise grün oder manuell abgenommen, Ladebudget gemessen, Neuaufbau innerhalb RTO, Lasttest bestanden, Zusatz „technisch nachgewiesen“ je Entscheidung. Keine Termine; Abweichungen vom Konzept sind Registeränderungen im selben Commit. Ab Stufe 6 ein Branch je Stufe gemäß A-030.
- **Begründung:** Beweise vor Breite; Isolation und Idempotenz zuerst, weil Fehler dort alles Spätere entwerten.
- **Nachweise:** Abnahmeprotokolle im Repository; Register mit Produktwahlen und Zusätzen.

## A-107 – Durchstich-Umgebungen ohne bereitgestellte Hosts

- **Status:** angenommen, 25.09.2026.
- **Umfang:** Bis der Betreiber Produktiv-, Staging- und Beobachtungshost bereitstellt (A-028), ist der GitHub-Actions-Runner die frische Umgebung für Wiederherstellungsübung, Betriebsnachweise in Compose und Lasttest. Es gelten dieselben Compose-Definitionen und Images wie für die Hosts. In diesen Läufen liegt das pgBackRest-Repository auf einem eigenen Volume als Stellvertreter des Offsite-Speichers; OpenBao wird automatisch initialisiert und entsiegelt, die Schlüsselteile liegen im lokalen Volume. Abnahmeprotokolle je Lauf sind CI-Artefakte; die für eine Stufenabnahme referenzierten Läufe liegen unter `durchstich/abnahme/`.
- **Begründung:** Hosting ist Betreibersache (A-028) und keine Vorbedingung; die Nachweise sollen nicht auf Hosts warten. Ein frischer Runner erfüllt „Neuaufbau aus Images, Konfiguration und Backups“ wörtlich und ohne Altbestand.
- **Folgen:** In Produktion bleiben Entsiegelung durch zwei Personen und Offsite-S3 am zweiten Standort unverändert (A-029, A-031). Erste Wiederherstellungsübung und Lasttest werden nach Bereitstellung der Hosts auf echtem Staging wiederholt; die Protokolle vermerken die Umgebung. Betriebsnachweise 9.3 (Alarme innerhalb von 5 Minuten bei Komponentenausfall) und 9.8 (Zertifikatserneuerung) setzen die Hosts voraus und bleiben bis dahin offen.
- **Nachweise:** Protokolle unter `durchstich/abnahme/` nennen Umgebung, Images und CI-Lauf; die Wiederherstellungsübung läuft bei jedem Push auf `master` und schreibt RTO und RPO.

## A-108 – Jobbibliothek: eigene Umsetzung auf `SKIP LOCKED`

- **Status:** angenommen, 25.09.2026; technisch nachgewiesen am 25.09.2026 (`durchstich/abnahme/stufe-3.md`).
- **Umfang:** Die logische Queue aus A-006 ist eine eigene Umsetzung in der Plattforminfrastruktur (`CompanyHero.Platform.Jobs`) ohne zusätzliche Bibliothek: Tabellen `platform.job` und `platform.job_schedule`, Einreihung nur in der laufenden Kontexttransaktion, Beanspruchung mit `FOR UPDATE SKIP LOCKED` und Lease, Bestätigung des Jobs in derselben Transaktion wie seine Wirkung mit Lease-Token aus Worker und Versuchszähler, Wiederholung mit verdoppelter Wartezeit bis zu einem Maximum, Dead-Letter mit protokolliertem Replay im Plattformkontext, Fairness durch begrenzte gleichzeitige Jobs je Tenant und Reihenfolge „je Tenant der älteste Job, zuerst der am längsten nicht bediente Tenant“, zeitgesteuerte Aufgaben mit Zeilensperre, Metriken `companyhero.jobs.*`. Fachereignisse liegen in der Ereignistabelle des veröffentlichenden Moduls; Abonnenten erhalten die Ereigniskennung als Job und lesen über dessen Schnittstelle. Es gibt keinen Broker, keinen Dispatcher-Prozess und kein LISTEN/NOTIFY; Worker fragen im Poll-Intervall ab.
- **Begründung:** Geprüft am 25.09.2026 gegen die Anforderungen aus A-006 (transaktionale Einreihung in der Transaktion des Fachvorgangs, Leases, Retries, Dead-Letter, Tenant-Fairness, Plattformtabelle ohne Nutzdaten): Hangfire mit Hangfire.PostgreSql 1.21.1 reiht über eigene Storage-Verbindung ein, nicht in der Transaktion des Fachvorgangs, und kennt keine Tenant-Fairness; Quartz.NET 4.1.1 ist ein Scheduler mit PostgreSQL-Jobstore, ohne Outbox-Kopplung, Dead-Letter oder Fairness; Wolverine (JasperFx, Releases 2026) bietet Outbox und dauerhafte Zustellung, bringt aber ein Messaging-Framework mit eigenem Modell, eigener Codegenerierung und kommerziellem Supportmodell und keine Fairness je Tenant; MassTransit v9 (SQL-Transport auf PostgreSQL) ist seit 2026 kommerziell lizenziert. Keine Bibliothek erfüllt die Anforderungen ohne Umgehung; die eigene Umsetzung umfasst wenige hundert Zeilen SQL und C#, verwendet nur Npgsql und bleibt hinter der Queue-Schnittstelle austauschbar (A-006 Erweiterungspfad).
- **Folgen:** keine zusätzliche Abhängigkeit; Versionen bleiben die von Npgsql und EF Core (`durchstich/versionen.md`). Erfolgreiche Jobs bleiben zur Nachvollziehbarkeit in der Tabelle; ein Aufräumlauf als zeitgesteuerte Aufgabe folgt mit dem Fachpfad. Die Wartezeit auf neue Jobs ist das Poll-Intervall (Voreinstellung 500 ms); LISTEN/NOTIFY wird erst bei gemessenem Bedarf ergänzt.
- **Nachweise:** Integrationstests `JobQueueTests` und `ContributionIdempotencyTests` (`durchstich/abnahme/stufe-3.md`).

## A-109 – OpenAPI-Clientgenerator: openapi-typescript mit typisiertem Transport über Angular HttpClient

- **Status:** angenommen, 26.09.2026; technisch nachgewiesen am 26.09.2026 (`durchstich/abnahme/stufe-5.md`).
- **Umfang:** Der Vertrag (A-009) entsteht als OpenAPI 3.1 aus der Endpunktregistrierung der API (`Microsoft.AspNetCore.OpenApi` 10.0.12, Schematransformer: nicht-nullbare Eigenschaften sind Pflichtfelder, `JsonNumberHandling.Strict`) und wird mit `dotnet run --project src/backend/CompanyHero.Api -- --export-openapi <datei>` beziehungsweise `npm run api:export` nach `src/backend/CompanyHero.Api/Contracts/openapi.json` exportiert und eingecheckt. Der TypeScript-Client wird offline aus dieser Datei mit **openapi-typescript 7.13.0** (`npm run api:generate`) nach `src/frontend/src/libs/api-client/generated/api.ts` erzeugt: reine Typen für `paths`, `operations` und `components`, Aufzählungen als Zeichenkettenunionen, Strings bleiben Strings, `null` bleibt unterscheidbar von fehlend. Der Transport ist ein generischer, von den generierten Pfadtypen typisierter Aufruf über Angular `HttpClient` (`libs/api-client/api-client.ts`, K11); er kennt keine Endpunkte und dupliziert keine DTOs. Der generierte Ordner wird nie manuell geändert; `scripts/check-generated-client.mjs` (CI) regeneriert und vergleicht, `OpenApiContractTests` (CI) prüft, dass Export und eingecheckte Datei übereinstimmen und dass der Vertrag K13 folgt.
- **Begründung:** Geprüft am 26.09.2026 gegen A-009 und K13 (Dezimal- und ID-Strings, strict-Typen, keine Nachbearbeitung, offline reproduzierbar): openapi-typescript erzeugt Typen ohne Laufzeitanteil und ohne Umwandlung von Datums- oder Dezimalwerten; NSwag (typescript-angular) wandelt `date-time` standardmäßig in `Date`, erzeugt umfangreiche Klassen mit eigenem Laufzeitcode und bräuchte Nachkonfiguration gegen stille Präzisionsverluste; ng-openapi-gen 1.1.0 erzeugt Angular-Dienste je Endpunkt (mehr generierter Code im Ladebudget) und unterstützt OpenAPI 3.1 nicht durchgängig; @hey-api/openapi-ts 0.99.0 bringt ein eigenes Laufzeitpaket mit. Der Transport über `HttpClient` hält Cookie-Sitzung, CSRF-Schutz und Interceptoren der Stufe 4 in einer Schicht (A-007, K11). openapi-typescript deklariert TypeScript `^5` als Peer; der Workspace fixiert TypeScript 6.0 (Angular 22), die Peer-Auflösung ist in `package.json` (`overrides`) auf die Workspace-Version gesetzt und der Generator läuft in CI mit dieser Version.
- **Folgen:** Nach jeder Vertragsänderung: `npm run api:export`, `npm run api:generate`, Verbraucher anpassen; Ablauf in der README. Vertragsproben (echte Antworten aus Testcontainer-PostgreSQL) liegen unter `src/frontend/e2e/fixtures/api/` und werden von `ContractSamplesTests` gegen die laufende API und von `contract.spec.ts` gegen den generierten Client geprüft. Versionen in `durchstich/versionen.md`.
- **Nachweise:** `OpenApiContractTests`, `ContractSamplesTests`, `contract.spec.ts`, `api-client.spec.ts`, `challenges.api.spec.ts`; Referenzscreen mit echten Vertragsproben (`durchstich/abnahme/stufe-5.md`).

## A-110 – Theme-Ableitung: eigene Portierung des Material-Verfahrens im HCT-Farbraum

- **Status:** angenommen, 26.09.2026; technisch nachgewiesen am 26.09.2026 (`durchstich/abnahme/stufe-5.md`).
- **Umfang:** Die Ableitung (A-013, A-077) ist eine eigene, abhängigkeitsfreie Portierung des veröffentlichten Verfahrens aus **material-color-utilities** (Google, Apache-2.0; Stand des TypeScript-Pakets `@material/material-color-utilities` 0.4.0, Repository-Stand 5b3618b vom 21.08.2026) in `CompanyHero.Modules.Branding/Domain/Color`: CAM16 mit Betrachtungsbedingungen, HCT-Solver, Tonpaletten mit Schlüsselfarbe, Kontrastfunktionen. Darauf setzt die Fachregel `ThemeDerivation` (Verfahrenskennung `hct-m3/1`): Tonpalette der Saatfarbe; `progress` mit erzwungenem Farbtonabstand von 40° innerhalb eines warmen Bandes (HCT 25° bis 95°), nie Blau; Akzent nur mit Mindestabstand; neutrale Flächen, Team-, Erfolgs-, Warn-, Fehler- und Diagrammfarben plattformweit aus der Konfiguration des Operators; WCAG-2.2-Prüfung aller Rollenpaare (4,5:1 Text, 3:1 Bedienelemente und Fortschrittsbahnen); die Saatfarbe wird immer angenommen und nur dort durch die nächstliegende zulässige Ableitung derselben Farbe (gleicher Farbton, gleiches Chroma, nächster Ton) ersetzt, wo sie durchfällt, mit Grund in der Ersetzungsliste. Ergebnis ist ein Tokensatz je Modus für alle `sys.*`-Rollen aus Marke 2.2 zuzüglich der Textrollen `on-success`, `on-warning`, `on-error`, `error-container`, `on-error-container` und der optionalen Akzentrollen, versioniert je Tenant.
- **Begründung:** Geprüft am 26.09.2026: das einzige .NET-Paket `MaterialColorUtilities` (albi005) liegt bei 0.3.0 vom April 2023 ohne weitere Releases, zielt auf .NET 6 und deckt die Kontrastprüfung mit Ersetzungsliste nach A-077 nicht ab; eine Portierung der drei Kernbausteine umfasst rund 700 Zeilen, ist deterministisch (keine Zufalls- oder Plattformabhängigkeit) und wird mit den veröffentlichten Referenzwerten des Verfahrens (CAM16-Werte für Rot, Grün, Blau, Weiß, Schwarz; Tonpalette von Blau; Kontrastfunktionen) auf drei Nachkommastellen exakt geprüft. Die Fachregel gehört ohnehin dem Backend (A-013) und bleibt bei einem späteren Paketwechsel unverändert.
- **Folgen:** keine zusätzliche Abhängigkeit; der Standard-Tokensatz der Plattform ist das Ergebnis derselben Ableitung aus der Saatfarbe `#2A45C9` und den Startwerten aus Marke 2.2 (Ersetzungen bei `progress`, `on-progress` und `warning` gegen die Flächen stehen im Kontrastbericht). Die Werte der Mockups (`design/mockups/referenz-tokens.json`) sind Gestaltungsannahmen; die Ableitung ist maßgeblich.
- **Nachweise:** `HctReferenceTests`, `ThemeDerivationTests`, `BrandingThemeTests`; Referenzscreen mit drei Marken (`durchstich/abnahme/stufe-5.md`).

## A-111 – OIDC-Middleware und WebAuthn: ASP.NET Core OpenIdConnect mit Schemata je Anbieter zur Laufzeit, Fido2NetLib

- **Status:** angenommen, 26.09.2026; technisch nachgewiesen am 26.09.2026 (`durchstich/abnahme/stufe-4.md`).
- **Umfang:** Externe Anbieter (A-007, A-015) laufen über **Microsoft.AspNetCore.Authentication.OpenIdConnect 10.0.12**: Authorization Code Flow mit PKCE, Scope nur `openid`, keine gespeicherten Provider-Tokens; die Middleware prüft Signatur, Issuer, Audience, State und Nonce. Es gibt kein statisches Schema je Anbieter: ein Schema-Provider des Moduls Identity (`DynamicOidcSchemeProvider`, `OidcSchemeRegistry`) registriert je plattformweitem Anbieter aus der Konfiguration (`Identity:Providers:<key>`) und je tenant-eigenem Anbieter aus der Datenbank (Client-Secret mit Data Protection verschlüsselt, Discovery-Dokument vor Aktivierung geprüft) ein Schema `oidc:<key>` mit Callback `/api/auth/oidc/<key>/callback`. Nach der Prüfung übergibt `OnTokenValidated` nur Issuer und Subject an den Anmeldeablauf des Moduls (Hash im plattformweiten Identitätsindex, Sitzung als Cookie, sonst geschützte Übergabe in den Beitritt); Claims werden danach verworfen. Passkeys (A-015, A-016) laufen über **Fido2 (Fido2NetLib) 4.1.0**: Registrierung mit Resident Key, Attestation `none`, Anmeldung mit leerer Credential-Liste (Discoverable Credentials), Prüfung von Origin, Challenge, User-Handle und Signaturzähler; Zeremoniezustand liegt Data-Protection-geschützt beim Client, gespeichert werden Credential-ID, öffentlicher Schlüssel, Zähler, AAGUID und Gerätename. Die Cookie-Sitzung selbst ist kein Bibliotheksschema: ein eigener `AuthenticationHandler` (`ch-session`) prüft das Sitzungs- beziehungsweise Gerätegeheimnis gegen PostgreSQL und das CSRF-Doppelcookie; Data-Protection-Schlüssel liegen in `platform.data_protection_key` (`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` 10.0.12), optional mit Zertifikat verschlüsselt.
- **Begründung:** Geprüft am 26.09.2026 gegen A-007 (etablierte Middleware für Signatur, Issuer, Audience, State, Nonce; Callback und Codeaustausch im Backend; keine Tokens im Browser), A-015 (tenant-eigene Anbieter im Self-Service mit Discovery-Validierung, Anbieter zur Laufzeit zu- und abschaltbar) und A-026 (keine Fremdhosts, keine Telemetrie, EU-Betrieb): die OpenIdConnect-Middleware ist Teil von ASP.NET Core 10 (kein weiteres Paket, dieselbe Wartung wie das Framework) und erlaubt über `IAuthenticationSchemeProvider` und `IOptionsMonitor` die Registrierung je Anbieter zur Laufzeit; Duende.IdentityModel 8.1.0 liefert nur Protokollbausteine (Discovery, Token-Endpunkte) ohne Redirect-Middleware und hätte den Ablauf selbst nachgebaut; OpenIddict.Client 7.7.1 ist ein vollständiger Client-Stack mit eigenem Speicher- und Konfigurationsmodell und mehr Oberfläche als für den Login nötig. Für WebAuthn ist Fido2NetLib die einzige aktiv gepflegte, framework-unabhängige .NET-Bibliothek mit vollständiger Attestations- und Assertionsprüfung; die Passkey-Unterstützung von ASP.NET Core Identity in .NET 10 ist an den Identity-Benutzerspeicher (`IdentityUser`, `UserManager`) gebunden, den CompanyHero nicht verwendet, weil Person, Rollen und Mitgliedschaft in eigenen Modulen liegen (A-012, A-014). Beide Bibliotheken laufen ohne externe Dienste.
- **Folgen:** Plattformanbieter werden über Konfiguration (Geheimnisse in OpenBao) gepflegt, tenant-eigene Anbieter über `POST /api/access/providers` mit frischer Anmeldung; Redirect-Ziele sind ausschließlich Pfade der Plattform-Origin (`Identity:PublicOrigin`), die zugleich Relying-Party-ID der Passkeys ist. Versionen in `durchstich/versionen.md`. Ein Wechsel der Bibliotheken bleibt hinter `IExternalLoginFlow` und `IPasskeyService` lokal.
- **Nachweise:** `OidcLoginTests`, `AccessJoinTests`, `RoleCodeTests`, `SessionTests`, `AccessRulesTests`; Playwright mit virtuellem Authenticator (`durchstich/abnahme/stufe-4.md`).

## Noch zu entscheidende Produktwahlen

Diese Punkte sind keine offenen Architekturfragen, sondern Produktauswahlen innerhalb der beschlossenen Architektur. Sie wurden im technischen Durchstich festgelegt:

| Punkt | Wo entschieden |
|---|---|
| Jobbibliothek oder eigene Umsetzung auf `SKIP LOCKED` | entschieden: A-108 |
| OpenAPI-Clientgenerator | entschieden: A-109 |
| Bibliothek für die Theme-Ableitung | entschieden: A-110 |
| Bibliotheken für OIDC-Middleware und WebAuthn | entschieden: A-111 |

Hosting und Plattformdomain stellt der Betreiber bereit; sie sind keine Entscheidungen dieses Konzepts.
