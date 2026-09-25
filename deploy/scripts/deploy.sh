#!/usr/bin/env bash
# Deployment (Betrieb 4, A-030): Images per Digest ziehen, Migrations-Container ausführen, Anwendung neu starten,
# Gesundheit prüfen, bei Fehler auf den vorherigen Digest zurückrollen. Migrationen sind vorwärtskompatibel,
# deshalb rollt die Datenbank nicht zurück.
#
# Aufruf auf dem Zielhost (per SSH vom Deploy-Job):  deploy.sh <release-version> <app@digest> <migrate@digest> <web@digest> <postgres@digest>
# Der Deploy-Job schreibt die Digests nach deploy/compose/plattform/.env; die letzte funktionierende Zeile bleibt als .env.previous.
set -euo pipefail

PLATTFORM="$(cd "$(dirname "$0")/../compose/plattform" && pwd)"
ENV_FILE="$PLATTFORM/.env"
PREVIOUS="$PLATTFORM/.env.previous"
COMPOSE=(docker compose -f "$PLATTFORM/compose.yaml" --env-file "$ENV_FILE")
HEALTH_URL="${CH_HEALTH_URL:-https://${CH_ORIGIN:-localhost}/api/health/ready}"
HEALTH_ATTEMPTS="${CH_HEALTH_ATTEMPTS:-40}"

RELEASE="$1"; APP="$2"; MIGRATE="$3"; WEB="$4"; POSTGRES="$5"

log() { printf '[deploy %s] %s\n' "$(date -u +%FT%TZ)" "$*"; }

set_image() {
  # Schreibt die Datei in place statt per Umbenennung (sed -i), weil synchronisierte Verzeichnisse Umbenennungen sperren können.
  local key="$1" value="$2" content
  content="$(awk -v k="$key" -v v="$value" 'BEGIN { done = 0 } index($0, k "=") == 1 { print k "=" v; done = 1; next } { print } END { if (!done) print k "=" v }' "$ENV_FILE")"
  printf '%s\n' "$content" > "$ENV_FILE"
}

healthy() {
  local i
  for ((i = 1; i <= HEALTH_ATTEMPTS; i++)); do
    if curl -kfsS --max-time 5 "$HEALTH_URL" > /dev/null 2>&1; then return 0; fi
    sleep 3
  done
  return 1
}

rollback() {
  log "Rollback auf vorherigen Stand"
  cp "$PREVIOUS" "$ENV_FILE"
  "${COMPOSE[@]}" up -d --no-build --no-deps web api worker || true
  if healthy; then log "Rollback erfolgreich; Datenbank unverändert (vorwärtskompatible Migration)"; else log "Rollback nicht gesund; manueller Eingriff nötig"; fi
  exit 1
}

[ -f "$ENV_FILE" ] || { echo ".env fehlt unter $PLATTFORM" >&2; exit 2; }
cp "$ENV_FILE" "$PREVIOUS"

set_image CH_RELEASE_VERSION "$RELEASE"
set_image CH_IMAGE_APP "$APP"
set_image CH_IMAGE_MIGRATE "$MIGRATE"
set_image CH_IMAGE_WEB "$WEB"
set_image CH_IMAGE_POSTGRES "$POSTGRES"
set_image CH_IMAGE_DIGEST "${APP#*@}"

log "Ziehe Images"
# Lokal gebaute Images (Nachweise, Übung) existieren nicht in der Registry; fehlende Pulls sind dort kein Fehler.
"${COMPOSE[@]}" pull --quiet --ignore-pull-failures api worker migrate web postgres || true

log "Starte Datenbank und Geheimnisspeicher"
"${COMPOSE[@]}" up -d --no-build postgres backup garage openbao openbao-init

log "Migrations-Container ($RELEASE)"
if ! "${COMPOSE[@]}" run --rm --no-deps migrate; then
  log "Migration fehlgeschlagen; Anwendung wird nicht gestartet, laufender Stand bleibt unverändert"
  cp "$PREVIOUS" "$ENV_FILE"
  exit 1
fi

log "Starte Anwendung"
# Ein fehlgeschlagener Start ist kein Skriptabbruch: die Gesundheitsprüfung entscheidet über den Rollback.
"${COMPOSE[@]}" up -d --no-build --no-deps api worker web || log "Start meldete einen Fehler; Gesundheitsprüfung folgt"

if healthy; then
  log "Gesund: $RELEASE ist ausgerollt"
  "${COMPOSE[@]}" ps
else
  log "Gesundheitsprüfung fehlgeschlagen"
  rollback
fi
