#!/bin/bash
# Backup-Dienst (A-031): legt die pgBackRest-Stanza an, prüft die Archivierung und sichert täglich.
# Läuft als Benutzer postgres im selben Image wie der Server und teilt Datenverzeichnis und Socket.
set -euo pipefail

STANZA="${PGBACKREST_STANZA:-main}"
BACKUP_HOUR_UTC="${CH_BACKUP_HOUR_UTC:-02}"

log() { printf '{"Timestamp":"%s","Category":"pgbackrest","Message":"%s"}\n' "$(date -u +%FT%TZ)" "$*"; }

wait_for_server() {
  until pg_isready -h /var/run/postgresql -U postgres -q; do sleep 2; done
}

ensure_stanza() {
  if pgbackrest --stanza="$STANZA" info --output=json | grep -q '"status":{"code":0'; then
    return 0
  fi
  log "Lege Stanza $STANZA an"
  pgbackrest --stanza="$STANZA" stanza-create
  pgbackrest --stanza="$STANZA" check
  log "Stanza $STANZA bereit"
}

wait_for_server
ensure_stanza

# Erste Basissicherung sofort, wenn noch keine existiert (ein Neuaufbau ohne Sicherung ist eine Störung).
if ! pgbackrest --stanza="$STANZA" info --output=json | grep -q '"backup":\[{'; then
  log "Erste Basissicherung"
  pgbackrest --stanza="$STANZA" --type=full backup
fi

if [ "${CH_BACKUP_ONCE:-false}" = "true" ]; then
  exit 0
fi

while true; do
  now_hour=$(date -u +%H)
  if [ "$now_hour" = "$BACKUP_HOUR_UTC" ]; then
    log "Tägliche Sicherung"
    if pgbackrest --stanza="$STANZA" --type=diff backup; then
      pgbackrest --stanza="$STANZA" expire
      log "Sicherung abgeschlossen"
    else
      log "Sicherung fehlgeschlagen"
    fi
    sleep 3600
  fi
  sleep 300
done
