#!/usr/bin/env bash
# Betriebsnachweise gegen die laufenden Compose-Projekte "plattform" und "beobachtung" (Stufe 1):
#   Betrieb 9.4  fehlerhafte Migration bricht vor dem Anwendungsstart ab; Rollback auf vorherigen Stand ohne Datenbank-Rollback
#   Betrieb 9.5  Datenbank, Garage, OpenBao, Grafana, Prometheus, Loki sind von außen nicht erreichbar; nur 80 und 443 veröffentlicht
#   Betrieb 9.7  Logs enthalten keine Personendaten (Stichprobe mit Query, Cookie, Authorization)
#   A-032        eine Origin: App-Shell und API unter derselben Adresse, HSTS und CSP gesetzt
#   A-029        OpenBao liefert das Datenbankgeheimnis; Beobachtungsstack startet mit den eingecheckten Konfigurationen
# Läuft in CI auf dem Runner und lokal (WSL/Linux). Schreibt ein Protokoll nach durchstich/abnahme/laeufe/.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PLATTFORM="$ROOT/deploy/compose/plattform"
BEOBACHTUNG="$ROOT/deploy/compose/beobachtung"
ENV_FILE="$PLATTFORM/.env"
LAUFE="$ROOT/durchstich/abnahme/laeufe"
REPORT="$LAUFE/compose-nachweise-$(date -u +%Y%m%dT%H%M%SZ).md"
HTTPS_PORT="${CH_HTTPS_PORT:-443}"
HTTP_PORT="${CH_HTTP_PORT:-80}"
BASE="https://localhost:${HTTPS_PORT}"
COMPOSE=(docker compose -f "$PLATTFORM/compose.yaml" --env-file "$ENV_FILE" -p nachweise)
BEOB=(docker compose -f "$BEOBACHTUNG/compose.yaml" --env-file "$BEOBACHTUNG/.env" -p nachweise-beob)

mkdir -p "$LAUFE"
FAILED=0
declare -a ROWS=()
log() { printf '[nachweis %s] %s\n' "$(date -u +%FT%TZ)" "$*"; }
pass() { ROWS+=("| $1 | $2 | ok |"); log "ok: $1"; }
fail() { ROWS+=("| $1 | $2 | FEHLER |"); log "FEHLER: $1"; FAILED=1; }
check() { local name="$1" detail="$2"; shift 2; if "$@" > /dev/null 2>&1; then pass "$name" "$detail"; else fail "$name" "$detail"; fi; }
# Wie check, aber mit Wiederholung (Dienste, die nach dem Start noch hochfahren): check_retry <name> <detail> <versuche> <kommando...>
check_retry() {
  local name="$1" detail="$2" attempts="$3" i; shift 3
  for ((i = 1; i <= attempts; i++)); do
    if "$@" > /dev/null 2>&1; then pass "$name" "$detail (Versuch $i)"; return 0; fi
    sleep 5
  done
  fail "$name" "$detail (nach $attempts Versuchen)"
}
pgsql() { "${COMPOSE[@]}" exec -T postgres psql -U postgres -d "$1" -v ON_ERROR_STOP=1 -tAc "$2"; }
http_code() { curl -ksS -o /dev/null -w '%{http_code}' "$1"; }
beob_wget() { "${BEOB[@]}" exec -T grafana wget -q -O - "$1"; }
bao_root() {
  # Führt bao mit dem Root-Token der automatischen Initialisierung aus (nur lokal/CI, A-107).
  "${COMPOSE[@]}" run --rm --no-deps --entrypoint sh openbao-init -c \
    'export BAO_ADDR=http://openbao:8200; export BAO_TOKEN="$(grep -o "\"root_token\": *\"[^\"]*\"" /openbao/init/init.json | cut -d"\"" -f4)"; exec bao "$@"' sh "$@"
}

cleanup() {
  if [ "$FAILED" -ne 0 ]; then
    "${COMPOSE[@]}" ps; "${COMPOSE[@]}" logs --no-color --tail=80 api migrate openbao-init web postgres backup | tail -300
    "${BEOB[@]}" ps; "${BEOB[@]}" logs --no-color --tail=40 | tail -160
  fi
  "${BEOB[@]}" down -v --remove-orphans > /dev/null 2>&1 || true
  "${COMPOSE[@]}" down -v --remove-orphans > /dev/null 2>&1 || true
  docker rmi -f companyhero-app:kaputt companyhero-migrate:defekt > /dev/null 2>&1 || true
  [ -f "$ENV_FILE.nachweise-vorher" ] && mv -f "$ENV_FILE.nachweise-vorher" "$ENV_FILE"
  rm -f "$ENV_FILE.previous"
}
trap 'rc=$?; cleanup; [ "$FAILED" -ne 0 ] && rc=1; log "Ende mit Exit-Code $rc"; exit $rc' EXIT
startfail() { log "Start fehlgeschlagen: $1"; "${COMPOSE[@]}" ps -a; "${COMPOSE[@]}" logs --no-color --tail=40; "${BEOB[@]}" ps -a; "${BEOB[@]}" logs --no-color --tail=40; exit 1; }

bash "$ROOT/deploy/scripts/bootstrap-secrets.sh" > /dev/null
[ -f "$ENV_FILE" ] || cp "$PLATTFORM/.env.example" "$ENV_FILE"
[ -f "$BEOBACHTUNG/.env" ] || cp "$BEOBACHTUNG/.env.example" "$BEOBACHTUNG/.env"
sed -i "s#^CH_OPENBAO_AUTO_INIT=.*#CH_OPENBAO_AUTO_INIT=true#; s#^CH_HTTP_PORT=.*#CH_HTTP_PORT=${HTTP_PORT}#; s#^CH_HTTPS_PORT=.*#CH_HTTPS_PORT=${HTTPS_PORT}#" "$ENV_FILE"
# Die Deploy-Tests verändern .env; der Ausgangsstand wird gesichert und am Ende wiederhergestellt.
cp "$ENV_FILE" "$ENV_FILE.nachweise-vorher"
for var in CH_IMAGE_APP CH_IMAGE_MIGRATE CH_IMAGE_WEB CH_IMAGE_POSTGRES; do
  value="${!var:-$(grep "^${var}=" "$PLATTFORM/.env.example" | cut -d= -f2-)}"
  sed -i "s#^${var}=.*#${var}=${value}#" "$ENV_FILE"
done

log "Plattform starten"
"${COMPOSE[@]}" up -d --no-build --wait --wait-timeout 300 || startfail plattform
"${COMPOSE[@]}" ps
log "Beobachtung starten"
"${BEOB[@]}" up -d --wait --wait-timeout 300 || startfail beobachtung
"${BEOB[@]}" ps

# ---- A-032: eine Origin, App-Shell und API ----
check "App-Shell unter der Origin" "GET / liefert index.html mit ch-root" bash -c "curl -ksS '$BASE/' | grep -q '<ch-root>'"
check "API unter derselben Origin" "GET /api/health/ready liefert 200" test "$(http_code "$BASE/api/health/ready")" = 200
check "Version-Endpunkt" "GET /api/version liefert JSON mit version" bash -c "curl -ksS '$BASE/api/version' | grep -q '\"version\"'"
check "HSTS gesetzt" "Strict-Transport-Security im Antwortkopf" bash -c "curl -ksSI '$BASE/' | grep -qi 'strict-transport-security'"
check "CSP ohne Fremdhosts" "Content-Security-Policy mit default-src 'self'" bash -c "curl -ksSI '$BASE/' | grep -i 'content-security-policy' | grep -q \"default-src 'self'\""
check "HTTP leitet auf HTTPS um" "GET http:// antwortet mit 308" test "$(curl -sS -o /dev/null -w '%{http_code}' "http://localhost:${HTTP_PORT}/")" = 308
check "Anwendung liest Geheimnis aus OpenBao" "openbao-init hat KV, Transit und Token eingerichtet; API mit Datenbank bereit" bash -c "${COMPOSE[*]} logs openbao-init | grep -q 'eingerichtet'"
MOUNTS="$(bao_root secrets list 2>/dev/null || true)"
check "Transit-Engine vorhanden" "companyhero-transit/ in der Liste der Secret-Engines" grep -q "companyhero-transit/" <<< "$MOUNTS"
check "KV-Engine vorhanden" "companyhero/ (kv v2) in der Liste der Secret-Engines" grep -q "companyhero/" <<< "$MOUNTS"
check_retry "Beobachtung: Collector gesund" "health_check-Erweiterung antwortet" 12 beob_wget http://otel-collector:13133/
check_retry "Beobachtung: Grafana gesund" "GET /api/health liefert database ok" 24 bash -c "${BEOB[*]} exec -T grafana wget -q -O - http://localhost:3000/api/health | grep -q '\"database\": *\"ok\"'"
check_retry "Beobachtung: Loki bereit" "GET /ready" 24 bash -c "${BEOB[*]} exec -T grafana wget -q -O - http://loki:3100/ready | grep -q ready"
check_retry "Beobachtung: Tempo bereit" "GET /ready" 24 bash -c "${BEOB[*]} exec -T grafana wget -q -O - http://tempo:3200/ready | grep -q ready"
check_retry "Beobachtung: Prometheus bereit" "GET /-/ready" 12 beob_wget http://prometheus:9090/-/ready

# ---- Betrieb 9.5: nur 80/443 veröffentlicht ----
PUBLISHED="$( { "${COMPOSE[@]}" ps --format json; "${BEOB[@]}" ps --format json; } | python3 -c '
import sys, json
for line in sys.stdin:
    line = line.strip()
    if not line: continue
    rows = json.loads(line)
    rows = rows if isinstance(rows, list) else [rows]
    for r in rows:
        for p in (r.get("Publishers") or []):
            if p.get("PublishedPort"):
                print(r["Service"], p["PublishedPort"])' | sort -u)"
log "Veröffentlichte Ports: $(echo "$PUBLISHED" | tr '\n' ' ')"
check "Nur der Reverse Proxy veröffentlicht Ports" "Dienste mit Ports: web (Plattform und Beobachtung zusammen)" test "$(echo "$PUBLISHED" | awk '{print $1}' | sort -u | xargs)" = "web"
for portname in "5432 PostgreSQL" "3900 Garage-S3" "3903 Garage-Admin" "8200 OpenBao" "3000 Grafana" "9090 Prometheus" "3100 Loki" "3200 Tempo" "4317 Collector"; do
  set -- $portname
  check "Von außen nicht erreichbar: $2" "TCP-Verbindung zu localhost:$1 scheitert" bash -c "! (exec 3<>/dev/tcp/127.0.0.1/$1) 2>/dev/null"
done

# ---- Betrieb 9.7: Logs ohne Personenbezug ----
curl -ksS -o /dev/null -H 'Cookie: ch_session=GEHEIMERCOOKIEWERT' -H 'Authorization: Bearer GEHEIMESBEARERTOKEN' \
  "$BASE/api/version?email=max.muster%40example.org&token=GEHEIMESQUERYTOKEN"
curl -ksS -o /dev/null "$BASE/api/gibt-es-nicht?email=max.muster%40example.org"
curl -ksS -o /dev/null "$BASE/gibt-es-nicht?email=max.muster%40example.org"
sleep 3
LOGS="$("${COMPOSE[@]}" logs --no-color api worker web postgres 2>/dev/null || true)"
for needle in example.org GEHEIMESQUERYTOKEN GEHEIMERCOOKIEWERT GEHEIMESBEARERTOKEN; do
  check "Logs ohne $needle" "Container-Logs von api, worker, web, postgres" bash -c "! grep -q '$needle' <<< \"\$LOGS\""
done
WEBLOGS="$("${COMPOSE[@]}" logs --no-color web 2>/dev/null | grep 'http.log.access' || true)"
check "Caddy-Zugriffslog vorhanden" "mindestens ein Eintrag handled request" test -n "$WEBLOGS"
check "Caddy-Zugriffslog ohne Query, Client-IP und Cookie" "keine Felder email=, remote_ip, client_ip, Cookie" bash -c "! grep -Eq 'email=|\"remote_ip\"|\"client_ip\"|\"Cookie\"|Authorization' <<< \"\$WEBLOGS\""

# ---- Betrieb 9.4: fehlerhafte Migration bricht vor dem Anwendungsstart ab ----
GOOD_APP="$(grep '^CH_IMAGE_APP=' "$ENV_FILE" | cut -d= -f2-)"
GOOD_MIGRATE="$(grep '^CH_IMAGE_MIGRATE=' "$ENV_FILE" | cut -d= -f2-)"
GOOD_WEB="$(grep '^CH_IMAGE_WEB=' "$ENV_FILE" | cut -d= -f2-)"
GOOD_PG="$(grep '^CH_IMAGE_POSTGRES=' "$ENV_FILE" | cut -d= -f2-)"
VERSION_BEFORE="$(curl -ksS "$BASE/api/version")"
RELEASES_BEFORE="$(pgsql companyhero 'select count(*) from platform.release')"

pgsql postgres "create database companyhero_defekt owner ch_migrator" > /dev/null
pgsql companyhero_defekt "create schema platform authorization ch_migrator; create table platform.release (kaputt text); alter table platform.release owner to ch_migrator" > /dev/null
set +e
"${COMPOSE[@]}" run --rm --no-deps -e CH_DB_NAME=companyhero_defekt migrate > "$LAUFE/migrate-defekt.log" 2>&1
MIG_EXIT=$?
set -e
check "Fehlerhafte Migration endet mit Fehler" "Migrations-Container gegen sabotierte Datenbank, Exit-Code $MIG_EXIT" test "$MIG_EXIT" -ne 0
check "Kein Teilzustand nach fehlerhafter Migration" "platform.release der defekten Datenbank unverändert" test "$(pgsql companyhero_defekt "select string_agg(column_name, ',') from information_schema.columns where table_schema='platform' and table_name='release'")" = kaputt

# Ein Release, dessen Migration scheitert, durch den echten Deploy-Ablauf: die Anwendung bleibt auf dem alten Stand.
# Das Testimage trägt die Verbindung zur sabotierten Datenbank fest eingebaut (nur lokal, wird am Ende gelöscht).
MIGRATOR_PW="$(cat "$PLATTFORM/secrets/db_migrator_password")"
docker build -q -t companyhero-migrate:defekt - > /dev/null <<EOF
FROM $GOOD_MIGRATE
ENV ConnectionStrings__Migrator="Host=postgres;Database=companyhero_defekt;Username=ch_migrator;Password=$MIGRATOR_PW"
EOF
set +e
CH_HEALTH_URL="$BASE/api/health/ready" CH_HEALTH_ATTEMPTS=8 COMPOSE_PROJECT_NAME=nachweise \
  bash "$ROOT/deploy/scripts/deploy.sh" "defekt" "$GOOD_APP" "companyhero-migrate:defekt" "$GOOD_WEB" "$GOOD_PG" > "$LAUFE/deploy-defekt.log" 2>&1
DEFEKT_EXIT=$?
set -e
check "Deploy mit fehlerhafter Migration bricht ab" "deploy.sh Exit-Code $DEFEKT_EXIT, Anwendung wurde nicht neu gestartet" test "$DEFEKT_EXIT" -ne 0
check "Konfiguration nach Abbruch unverändert" ".env zeigt wieder $GOOD_MIGRATE" bash -c "grep -q '^CH_IMAGE_MIGRATE=$GOOD_MIGRATE\$' '$ENV_FILE'"
check "Produktion unverändert" "Version-Endpunkt identisch, platform.release mit $RELEASES_BEFORE Zeilen" bash -c "[ \"\$(curl -ksS '$BASE/api/version')\" = '$VERSION_BEFORE' ] && [ \"\$(${COMPOSE[*]} exec -T postgres psql -U postgres -d companyhero -tAc 'select count(*) from platform.release')\" = '$RELEASES_BEFORE' ]"

# ---- Betrieb 9.4: Rollback auf vorherigen Digest ohne Datenbank-Rollback ----
# Ein "neues" Anwendungs-Image, das nie gesund wird: derselbe Inhalt, aber ein Einstiegspunkt, der sofort endet.
docker build -q -t companyhero-app:kaputt - > /dev/null <<EOF
FROM $GOOD_APP
ENTRYPOINT ["/bin/sh", "-c", "echo kaputt; exit 64"]
EOF
set +e
CH_HEALTH_URL="$BASE/api/health/ready" CH_HEALTH_ATTEMPTS=8 COMPOSE_PROJECT_NAME=nachweise \
  bash "$ROOT/deploy/scripts/deploy.sh" "kaputt" "companyhero-app:kaputt" "$GOOD_MIGRATE" "$GOOD_WEB" "$GOOD_PG" > "$LAUFE/deploy-rollback.log" 2>&1
DEPLOY_EXIT=$?
set -e
check "Deploy eines nicht gesunden Images scheitert" "deploy.sh Exit-Code $DEPLOY_EXIT" test "$DEPLOY_EXIT" -ne 0
check "Rollback stellt den vorherigen Digest wieder her" ".env zeigt wieder $GOOD_APP" bash -c "grep -q '^CH_IMAGE_APP=$GOOD_APP\$' '$ENV_FILE'"
check_retry "Anwendung nach Rollback gesund" "GET /api/health/ready liefert 200" 20 bash -c "[ \"\$(curl -ksS -o /dev/null -w '%{http_code}' '$BASE/api/health/ready')\" = 200 ]"
check "Datenbank ohne Rollback" "platform.release enthält den Lauf 'kaputt' (Migration lief, Anwendung rollte zurück)" test "$(pgsql companyhero "select count(*) from platform.release where version='kaputt'")" = 1

{
  echo "# Betriebsnachweise in Compose $(date -u +%Y-%m-%d)"
  echo
  echo "**Nachweise:** Betrieb 9.4, 9.5, 9.7; A-029; A-032. **Zeitpunkt:** $(date -u +%FT%TZ) UTC. **Host:** $(hostname). **Umgebung:** gemäß A-107."
  echo "**Images:** ${CH_IMAGE_APP:-lokal}, ${CH_IMAGE_MIGRATE:-lokal}, ${CH_IMAGE_WEB:-lokal}, ${CH_IMAGE_POSTGRES:-lokal}"
  echo
  echo "| Prüfung | Detail | Ergebnis |"
  echo "|---|---|---|"
  printf '%s\n' "${ROWS[@]}"
  echo
  echo "Veröffentlichte Ports laut compose ps: $(echo "$PUBLISHED" | tr '\n' ';')"
  echo
  echo "**Ergebnis:** $([ "$FAILED" -eq 0 ] && echo bestanden || echo 'nicht bestanden')"
} > "$REPORT"
log "Protokoll: $REPORT"
exit "$FAILED"
