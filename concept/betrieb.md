# CompanyHero – Betrieb

**Stand:** 20.09.2026  
**Status:** Beschlossen gemäß A-027 bis A-032 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung.  
**Bezug:** [Backend](architektur-backend.md), [Datenschutz und Nachweis](datenschutz-und-nachweis.md).

## 1. Betriebsziele (A-027)

| Ziel | Wert | Bedeutung |
|---|---|---|
| Verfügbarkeit | 99,5 % je Kalendermonat, angekündigte Wartungsfenster ausgenommen | bis zu 3,6 Stunden ungeplanter Ausfall je Monat |
| Wiederherstellungspunkt (RPO) | 15 Minuten | WAL-Archivierung der Datenbank in kurzen Abständen |
| Wiederherstellungszeit (RTO) | 4 Stunden | Neuaufbau auf einem frischen Host aus Images, Konfiguration und Backups |
| Planungslast | 100 Tenants, 25.000 Personen, 300 API-Anfragen/s in der Spitze, 50 Beiträge/s | Grundlage für Lasttests im Durchstich, kein gemessener Wert |
| Wartungsfenster | wöchentlich, nachts, höchstens 30 Minuten, in der App angekündigt | Betriebssystem- und Image-Updates |

Die Ziele sind Planungsziele des Betreibers. Sie werden gemessen, bevor sie einem Tenant zugesagt werden. Ein Vertrag mit höherer Zusage oder gemessene Last über der Planungslast löst den Übergang nach Abschnitt 2.3 aus.

## 2. Hosting und Orchestrierung (A-028)

### 2.1 Standort und Bereitstellung

Das Hosting stellt der Betreiber selbst bereit. Es ist keine Entscheidung dieses Konzepts, sondern eine Vorgabe an die bereitgestellte Umgebung: Linux-Hosts in der EU, S3-kompatibler Objektspeicher an einem zweiten Standort für Offsite-Kopien, Zwei-Faktor an allen Verwaltungskonten. Ein beteiligter Infrastrukturanbieter wird in der Unterauftragsverarbeiterliste geführt.

### 2.2 Einstieg: ein Produktivhost mit Docker Compose

| Host | Zweck | Ausstattung zum Start |
|---|---|---|
| Produktion | alle Laufzeitkomponenten | 8 vCPU, 32 GB RAM, NVMe, tägliche Host-Snapshots |
| Staging | gleiche Compose-Definition mit synthetischen Daten | 4 vCPU, 16 GB RAM |
| Beobachtung | Erreichbarkeitsprüfung von außen, Empfang von Alarmen | kleinster Host, anderer Standort |

Compose-Projekte: `plattform` (Caddy, API, Worker, PostgreSQL, Garage, OpenBao), `beobachtung` (Collector, Prometheus, Loki, Tempo, Grafana), `arena` und `vault` als eigene Projekte mit eigenen Netzen und Volumes, sobald diese Dienste gebaut werden.

### 2.3 Übergang zu mehreren Hosts

Trigger: Vertrag mit Verfügbarkeitszusage über 99,5 %, gemessene Last über der Planungslast oder Speicherbedarf über die Kapazität eines Hosts. Zielplattform ist dann Kubernetes in der k3s-Ausprägung mit replizierter PostgreSQL; Compose-Definitionen werden so gehalten, dass Dienste, Volumes und Konfiguration eins zu eins übertragbar sind. Vor dem Trigger wird keine Orchestrierung eingeführt.

### 2.4 Netz und Zugriff

- Öffentlich erreichbar sind ausschließlich Port 443 und 80 des Reverse Proxy. Datenbank, Objektspeicher, Geheimnisspeicher und Beobachtung liegen in internen Docker-Netzen.
- Host-Zugang nur per SSH mit Schlüssel und Zwei-Faktor beim Anbieterkonto; kein Passwort-Login, kein Root-Login. Zugriff haben benannte Personen; Schlüsselrotation bei Personalwechsel.
- Host-Firewall verweigert alles außer 22, 80, 443; 22 nur von festgelegten Adressen.
- Container laufen ohne Root, mit schreibgeschütztem Dateisystem, wo möglich, und ohne Docker-Socket.
- Server laufen in UTC; die fachliche Standardzeitzone der Tenants ist Europe/Vienna und je Tenant einstellbar.

### 2.5 Umgebungen

| Umgebung | Daten | Zweck |
|---|---|---|
| Lokal | Compose auf dem Entwicklungsrechner, Testcontainer | Entwicklung und schnelle Tests |
| Staging | synthetische Daten, nie Produktionsdaten | Abnahme von Releases, Lasttests, Wiederherstellungsübungen |
| Produktion | echte Daten | Betrieb |

Alle drei verwenden dieselben Images und dieselbe Compose-Definition mit umgebungsspezifischer Konfiguration.

## 3. Plattformkomponenten (A-029)

| Aufgabe | Produkt | Lizenz | Begründung |
|---|---|---|---|
| Reverse Proxy und TLS | Caddy | Apache-2.0 | automatische Zertifikate über ACME, HSTS, HTTP/3, kleine Konfiguration |
| Datenbank | PostgreSQL 18 | PostgreSQL | A-011 |
| Objektspeicher | Garage | AGPL-3.0 | S3-API mit signierten URLs, ein Binary, für Einzel- und Kleincluster ausgelegt |
| Geheimnisspeicher | OpenBao | MPL-2.0 | Transit-Engine für Tenant-Datenschlüssel, KV für Anwendungsgeheimnisse, Vault-kompatible API |
| Datenbank-Backup | pgBackRest | MIT | Basissicherungen und WAL-Archivierung mit Verschlüsselung in S3-kompatiblen Speicher |
| Telemetrie-Eingang | OpenTelemetry Collector | Apache-2.0 | ein Eingang für Logs, Metriken und Traces |
| Metriken | Prometheus | Apache-2.0 | Standard für Container-Metriken |
| Logs | Loki | AGPL-3.0 | strukturierte Logs ohne Volltextindex |
| Traces | Tempo | AGPL-3.0 | Traces ohne eigene Datenbank |
| Dashboards und Alarme | Grafana | AGPL-3.0 | eine Oberfläche für alle drei Signale |
| Erreichbarkeit von außen | Uptime Kuma | MIT | HTTP-Prüfung vom Beobachtungshost, sendet keine Personendaten |

AGPL-Komponenten werden selbst betrieben und nicht verändert weitergegeben; daraus entstehen keine Offenlegungspflichten für CompanyHero-Code. Alle Produkte laufen in Containern mit versionierten Images.

### 3.1 Geheimnisse

- OpenBao hält Tenant-Datenschlüssel (Transit, Schlüssel verlassen OpenBao nie), Anwendungsgeheimnisse, Anbieter-Client-Secrets und Data-Protection-Schlüssel.
- Bootstrap-Geheimnisse (OpenBao-Zugang der Anwendung, Migrationszugang der Datenbank) liegen als Docker-Secrets auf dem Host mit Dateirechten nur für den Deploy-Benutzer.
- OpenBao-Entsiegelung mit geteilten Schlüsseln bei zwei benannten Personen; kein automatisches Entsiegeln über Anbieterdienste.
- Rotation: Anwendungsgeheimnisse jährlich, Anbieter-Secrets nach Anbieterrichtlinie, Tenant-Datenschlüssel jährlich mit Neuverschlüsselung im Hintergrund.

### 3.2 Beobachtung

- Anwendung und Worker exportieren über OpenTelemetry an den Collector; keine direkten Exporte an Fremdsysteme.
- Logs ohne Personenbezug mit pseudonymen Kennungen; Aufbewahrung 30 Tage. Metriken 90 Tage. Traces 7 Tage.
- Pflicht-Dashboards: Anfragen und Fehlerquoten je Endpunktgruppe, Datenbank-Verbindungen und -Latenz, Job-Queue (Länge, Alter des ältesten Jobs, Fehlerquote, Dead-Letter), Speicherbelegung, Zertifikatsablauf, Backup-Status.
- Alarme per E-Mail an eine Bereitschaftsadresse: Erreichbarkeit, Fehlerquote über 2 % für 5 Minuten, Job-Alter über 15 Minuten, fehlgeschlagenes Backup, Speicher über 80 %, Zertifikat unter 14 Tagen.
- Grafana ist nur intern erreichbar und verlangt Passkey oder externen Anbieter.

## 4. Lieferkette (A-030)

| Schritt | Festlegung |
|---|---|
| Codehosting | GitHub, privates Repository; Arbeit direkt auf `master`, kein Branch-Schutz, keine Pull-Request-Pflicht |
| CI | GitHub Actions bei jedem Push auf `master`: Build, Compiler- und Templateprüfung, Fachtests, API- und Datenbanktests in Testcontainern, Architekturtests, Lint-Grenzen aus den Integrationsregeln, Ladebudget, Abhängigkeits- und Image-Scan |
| Abhängigkeiten | Dependabot für NuGet, npm und Basis-Images; wöchentliche Sammel-Updates; Sicherheitsupdates sofort |
| Image-Scan | Trivy in CI; kritische Funde blockieren das Release |
| Registry | GitHub Container Registry, Images mit Version und Digest, wöchentlicher Neubau für Basis-Image-Updates |
| Deployment | Deploy-Job verbindet per SSH mit dem Zielhost, zieht Images per Digest, führt den Migrations-Container aus, startet Compose neu und prüft Gesundheitsendpunkte |
| Rollback | vorheriger Digest; Migrationen sind vorwärtskompatibel, damit ein Rollback der Anwendung ohne Datenbank-Rollback möglich ist |
| Staging vor Produktion | jedes Release läuft zuerst auf Staging mit Smoke-Tests; Produktion nur per ausdrücklicher Freigabe |
| Reproduzierbarkeit | Lockfiles, festgelegtes SDK, festgelegte Node-Version, Build im Container |

Agentische Entwicklung committet ebenfalls direkt auf `master` und durchläuft dieselbe CI. Ein Deployment setzt einen grünen CI-Lauf des betreffenden Commits voraus; ein roter Lauf auf `master` wird als Nächstes repariert.

## 5. Backups und Wiederherstellung (A-031)

| Bestand | Verfahren | Häufigkeit | Ziel |
|---|---|---|---|
| PostgreSQL | pgBackRest: Basissicherung und WAL-Archivierung, verschlüsselt | Basis täglich, WAL alle 5 Minuten | Offsite-Objektspeicher, zweiter Standort |
| Garage (Medien) | verschlüsselte inkrementelle Kopie der Buckets | täglich | Offsite-Objektspeicher |
| OpenBao | Raft-Snapshot, verschlüsselt | täglich und nach jeder Schlüsselrotation | Offsite-Objektspeicher; Entsiegelungsschlüssel offline |
| Konfiguration | Compose-Dateien und Umgebungskonfiguration ohne Geheimnisse im Repository | bei Änderung | GitHub |
| Host | Snapshot des Anbieters | täglich | Anbieter |

- Aufbewahrung höchstens 35 Tage für alle Bestände (A-024). Löschläufe werden nach jeder Wiederherstellung erneut ausgeführt.
- Backup-Schlüssel liegen in OpenBao und zusätzlich offline; ein Backup ohne den Schlüssel ist wertlos, ein Schlüssel ohne Backup ebenso.
- Wiederherstellungsübung vierteljährlich auf Staging: vollständiger Neuaufbau aus Images, Konfiguration und Backups innerhalb der RTO, mit Protokoll. Erste Übung im Durchstich.
- Backup-Erfolg ist ein überwachter Alarm; ein ausbleibendes Backup gilt als Störung.

## 6. Domains, E-Mail und Push (A-032)

### 6.1 Domains und TLS

- **Eine Plattform-Origin** für Mitglieder-App, Verwaltung, Kiosk und API. Operator-Konsole unter derselben Origin mit eigenem Feature-Einstieg. Keine tenant-eigenen Domains; die Firmenmarke wirkt innerhalb der App. Staging hat eine eigene Origin.
- Die Plattformdomain ist Konfiguration (Origin-Einstellung, Absenderdomain) und keine Vorbedingung; der Betreiber legt sie fest, wenn sie gebraucht wird. Bis dahin laufen Entwicklung und Staging unter Arbeitsdomains.
- **TLS** über Caddy mit ACME-Zertifikaten einer öffentlichen Zertifizierungsstelle; HSTS mit Preload-fähigen Einstellungen.

### 6.2 E-Mail-Versand

E-Mail ist ein konfigurierbarer Versandkanal der Domäne Benachrichtigungen mit zwei Ebenen:

| Ebene | Wer konfiguriert | Wirkung |
|---|---|---|
| Plattform | Operator | Standardversand für alle Tenants ohne eigene Konfiguration sowie für plattformweite Nachrichten |
| Tenant | Tenant-Admin | eigener Versand unter eigener Absenderadresse und -domain; ersetzt den Plattformversand für diesen Tenant |

Unterstützte Transporte, auf beiden Ebenen gleich:

| Transport | Einstellungen |
|---|---|
| SMTP | Host, Port, Verschlüsselung (keine, STARTTLS, implizites TLS), Authentifizierung (keine, Benutzer und Passwort), Absenderadresse und -name, Antwortadresse |
| Amazon SES | Region, Zugangsschlüssel, verifizierte Absenderidentität |
| SendGrid | API-Schlüssel, verifizierter Absender |
| Mailgun | API-Schlüssel, Domain, Region (EU oder US) |

Regeln:

- Zugangsdaten liegen in OpenBao, nie in der Datenbank im Klartext und nie in Logs.
- Jede Konfiguration wird vor Aktivierung mit einer Testnachricht an eine vom Konfigurierenden bestätigte Adresse geprüft; erst danach ist sie aktiv.
- Öffnungs- und Klickverfolgung der Anbieter ist deaktiviert; Nachrichten enthalten keine Personendaten außer der Anrede mit Anzeigenamen und dem Link und keine Tracking-Elemente.
- SPF, DKIM und DMARC sind für jede Absenderdomain erforderlich. Für Anbieter-Transporte zeigt die Verwaltung die einzutragenden DNS-Einträge und prüft sie; für SMTP liegt die Verantwortung beim Betreiber der Absenderdomain.
- Bei Tenant-Konfiguration gilt: Schlägt der Versand fehl, wird der Tenant-Admin benachrichtigt. Ob in diesem Fall auf den Plattformversand zurückgefallen wird, stellt der Tenant-Admin ein; Voreinstellung ist Rückfall aktiv, weil Magic-Links und Rollencodes zugestellt werden müssen.
- Datenschutz: Ein auf Plattformebene konfigurierter Anbieter ist Unterauftragsverarbeiter des Betreibers und steht in der Unterauftragsverarbeiterliste. Ein vom Tenant konfigurierter Transport ist ein Auftragsverarbeiter des Tenants; die Verwaltung weist darauf hin.
- Zustellstatus (angenommen, zurückgewiesen, unzustellbar) wird je Nachricht ohne Inhalt gespeichert, 30 Tage aufbewahrt und dem Tenant-Admin aggregiert angezeigt.

### 6.3 Push und Status

- **Web Push** über VAPID an die Push-Dienste der Browserhersteller. Nutzlasten sind verschlüsselt und enthalten nur eine Referenz; die Push-Dienste sehen keinen Inhalt.
- **Kein Status-Portal** zum Start; Störungen werden in der App und per E-Mail an Tenant-Admins kommuniziert.

## 7. Betriebsprozesse

- **Bereitschaft:** eine Bereitschaftsadresse, werktags Reaktion innerhalb von 4 Stunden, Störungen an Wochenenden am nächsten Werktag; passend zu 99,5 %.
- **Änderungen:** jede Produktionsänderung über die Lieferkette; manuelle Eingriffe auf dem Host werden protokolliert und binnen einer Woche in Code überführt.
- **Störungen:** kurze Nachbetrachtung mit Ursache, Wirkung, Maßnahme; Maßnahmen als Aufgaben im Repository.
- **Updates:** Host-Betriebssystem wöchentlich im Wartungsfenster; Images wöchentlich neu gebaut; Sicherheitsupdates außerplanmäßig.
- **Zugriffsprüfung:** vierteljährlich Liste der Personen mit Host-, GitHub-, Anbieter- und OpenBao-Zugang.

## 8. Kosten

Zielkorridor zum Start: zwei Hosts, ein Beobachtungshost, Offsite-Speicher und Mailversand im niedrigen dreistelligen Eurobereich je Monat. Die konkrete Zahl folgt aus der vom Betreiber bereitgestellten Infrastruktur.

## 9. Nachweise

1. Vollständiger Neuaufbau der Produktion auf Staging aus Images, Konfiguration und Backups innerhalb von 4 Stunden; Datenstand höchstens 15 Minuten alt.
2. Lasttest mit Planungslast auf Staging: Fehlerquote unter 0,1 %, 95. Perzentil der API-Latenz unter 300 ms, Job-Alter unter 60 Sekunden.
3. Ausfall eines Workers, der API und der Datenbank einzeln: Alarme innerhalb von 5 Minuten, Wiederanlauf ohne Datenverlust.
4. Deploy mit fehlerhafter Migration: Abbruch vor dem Anwendungsstart, Produktion unverändert. Rollback auf vorherigen Digest ohne Datenbank-Rollback.
5. Zugriff von außen auf Datenbank, Garage, OpenBao und Grafana: nicht erreichbar.
6. Gelöschte Daten sind nach 35 Tagen in keinem Backup mehr vorhanden.
7. Logs, Traces und Alarme enthalten keine Personendaten; Stichprobe je Release.
8. Zertifikatserneuerung ohne Eingriff; Ablaufalarm bei künstlich verkürzter Laufzeit.
9. E-Mail über SMTP ohne TLS, SMTP mit STARTTLS und Authentifizierung, Amazon SES, SendGrid und Mailgun: Testnachricht, SPF/DKIM/DMARC-Prüfung, deaktiviertes Tracking, Rückfall auf Plattformversand bei Tenant-Fehler, Zugangsdaten nur in OpenBao.
