# Betriebsnachweise in Compose 2026-09-25

**Nachweise:** Betrieb 9.4, 9.5, 9.7; A-029; A-032. **Zeitpunkt:** 2026-09-25T15:58:14Z UTC. **Host:** pulse-pc3. **Umgebung:** gemäß A-107.
**Images:** lokal, lokal, lokal, lokal

| Prüfung | Detail | Ergebnis |
|---|---|---|
| App-Shell unter der Origin | GET / liefert index.html mit ch-root | ok |
| API unter derselben Origin | GET /api/health/ready liefert 200 | ok |
| Version-Endpunkt | GET /api/version liefert JSON mit version | ok |
| HSTS gesetzt | Strict-Transport-Security im Antwortkopf | ok |
| CSP ohne Fremdhosts | Content-Security-Policy mit default-src 'self' | ok |
| HTTP leitet auf HTTPS um | GET http:// antwortet mit 308 | ok |
| Anwendung liest Geheimnis aus OpenBao | openbao-init hat KV, Transit und Token eingerichtet; API mit Datenbank bereit | ok |
| Transit-Engine vorhanden | companyhero-transit/ in der Liste der Secret-Engines | ok |
| KV-Engine vorhanden | companyhero/ (kv v2) in der Liste der Secret-Engines | ok |
| Beobachtung: Collector gesund | health_check-Erweiterung antwortet (Versuch 1) | ok |
| Beobachtung: Grafana gesund | GET /api/health liefert database ok (Versuch 4) | ok |
| Beobachtung: Loki bereit | GET /ready (Versuch 4) | ok |
| Beobachtung: Tempo bereit | GET /ready (Versuch 1) | ok |
| Beobachtung: Prometheus bereit | GET /-/ready (Versuch 1) | ok |
| Nur der Reverse Proxy veröffentlicht Ports | Dienste mit Ports: web (Plattform und Beobachtung zusammen) | ok |
| Von außen nicht erreichbar: PostgreSQL | TCP-Verbindung zu localhost:5432 scheitert | ok |
| Von außen nicht erreichbar: Garage-S3 | TCP-Verbindung zu localhost:3900 scheitert | ok |
| Von außen nicht erreichbar: Garage-Admin | TCP-Verbindung zu localhost:3903 scheitert | ok |
| Von außen nicht erreichbar: OpenBao | TCP-Verbindung zu localhost:8200 scheitert | ok |
| Von außen nicht erreichbar: Grafana | TCP-Verbindung zu localhost:3000 scheitert | ok |
| Von außen nicht erreichbar: Prometheus | TCP-Verbindung zu localhost:9090 scheitert | ok |
| Von außen nicht erreichbar: Loki | TCP-Verbindung zu localhost:3100 scheitert | ok |
| Von außen nicht erreichbar: Tempo | TCP-Verbindung zu localhost:3200 scheitert | ok |
| Von außen nicht erreichbar: Collector | TCP-Verbindung zu localhost:4317 scheitert | ok |
| Logs ohne example.org | Container-Logs von api, worker, web, postgres | ok |
| Logs ohne GEHEIMESQUERYTOKEN | Container-Logs von api, worker, web, postgres | ok |
| Logs ohne GEHEIMERCOOKIEWERT | Container-Logs von api, worker, web, postgres | ok |
| Logs ohne GEHEIMESBEARERTOKEN | Container-Logs von api, worker, web, postgres | ok |
| Caddy-Zugriffslog vorhanden | mindestens ein Eintrag handled request | ok |
| Caddy-Zugriffslog ohne Query, Client-IP und Cookie | keine Felder email=, remote_ip, client_ip, Cookie | ok |
| Fehlerhafte Migration endet mit Fehler | Migrations-Container gegen sabotierte Datenbank, Exit-Code 1 | ok |
| Kein Teilzustand nach fehlerhafter Migration | platform.release der defekten Datenbank unverändert | ok |
| Deploy mit fehlerhafter Migration bricht ab | deploy.sh Exit-Code 1, Anwendung wurde nicht neu gestartet | ok |
| Konfiguration nach Abbruch unverändert | .env zeigt wieder ghcr.io/cpfaffinger/companyhero-migrate:local | ok |
| Produktion unverändert | Version-Endpunkt identisch, platform.release mit 1 Zeilen | ok |
| Deploy eines nicht gesunden Images scheitert | deploy.sh Exit-Code 1 | ok |
| Rollback stellt den vorherigen Digest wieder her | .env zeigt wieder ghcr.io/cpfaffinger/companyhero-app:local | ok |
| Anwendung nach Rollback gesund | GET /api/health/ready liefert 200 (Versuch 1) | ok |
| Datenbank ohne Rollback | platform.release enthält den Lauf 'kaputt' (Migration lief, Anwendung rollte zurück) | ok |

Veröffentlichte Ports laut compose ps: web 8088;web 8443;

**Ergebnis:** bestanden
