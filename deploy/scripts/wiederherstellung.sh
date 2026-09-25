#!/usr/bin/env bash
# Wiederherstellungsübung (Betrieb 9.1, A-031): Neuaufbau der Datenbank auf einer frischen Umgebung ("staging")
# aus Images, Konfiguration und dem pgBackRest-Repository, mit Messung von RTO und RPO.
#
# Ablauf: Produktion (Projekt drill-prod) starten, Schema migrieren, Daten schreiben, Basissicherung abwarten, weitere Daten
# schreiben und archivieren lassen, Produktion mit ihren Datenvolumes zerstören, Staging (Projekt drill-staging) aus dem
# Repository wiederherstellen, Daten prüfen, Protokoll schreiben. Das Repository liegt auf einem eigenen Volume als
# Stellvertreter des Offsite-Speichers; in Produktion ist es der S3-Speicher am zweiten Standort.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PLATTFORM="$ROOT/deploy/compose/plattform"
ENV_FILE="${CH_DRILL_ENV_FILE:-$PLATTFORM/.env}"
OFFSITE_VOLUME="drill-offsite-$(date -u +%Y%m%d%H%M%S)"
REPORT="${CH_DRILL_REPORT:-$ROOT/durchstich/abnahme/wiederherstellung-$(date -u +%Y%m%dT%H%M%SZ).md}"
RTO_LIMIT_SECONDS=$((4 * 3600))
RPO_LIMIT_SECONDS=$((15 * 60))

export CH_BACKUP_REPO_VOLUME="$OFFSITE_VOLUME"
PROD=(docker compose -f "$PLATTFORM/compose.yaml" --env-file "$ENV_FILE" -p drill-prod)
STAGING=(docker compose -f "$PLATTFORM/compose.yaml" --env-file "$ENV_FILE" -p drill-staging)

log() { printf '[übung %s] %s\n' "$(date -u +%FT%TZ)" "$*"; }
psql_prod() { "${PROD[@]}" exec -T postgres psql -U postgres -d companyhero -v ON_ERROR_STOP=1 -tAc "$1"; }
psql_staging() { "${STAGING[@]}" exec -T postgres psql -U postgres -d companyhero -v ON_ERROR_STOP=1 -tAc "$1"; }
now_epoch() { date -u +%s; }

cleanup() {
  log "Aufräumen"
  "${STAGING[@]}" down -v --remove-orphans > /dev/null 2>&1 || true
  "${PROD[@]}" down -v --remove-orphans > /dev/null 2>&1 || true
  docker volume rm -f "$OFFSITE_VOLUME" > /dev/null 2>&1 || true
}
trap 'rc=$?; cleanup; log "Ende mit Exit-Code $rc"; exit $rc' EXIT

log "1/7 Produktion starten (Postgres, Backup-Dienst) und Schema migrieren"
"${PROD[@]}" up -d --wait postgres backup
"${PROD[@]}" run --rm --no-deps migrate > /dev/null

log "2/7 Erste Basissicherung abwarten"
for i in $(seq 1 60); do
  if "${PROD[@]}" exec -T backup ch-backup-env.sh pgbackrest --stanza=main info --output=json 2>/dev/null | grep -q '"backup":\[{'; then break; fi
  [ "$i" -eq 60 ] && { log "Keine Basissicherung nach 5 Minuten"; "${PROD[@]}" logs backup; exit 1; }
  sleep 5
done
BACKUP_INFO="$("${PROD[@]}" exec -T backup ch-backup-env.sh pgbackrest --stanza=main info)"

log "3/7 Daten schreiben: vor und nach der Basissicherung"
psql_prod "create schema if not exists drill; create table if not exists drill.probe (id serial primary key, note text not null, geschrieben_am timestamptz not null default now())" > /dev/null
psql_prod "insert into drill.probe (note) values ('nach-basissicherung-1')" > /dev/null
sleep 2
psql_prod "insert into drill.probe (note) values ('nach-basissicherung-2')" > /dev/null
psql_prod "select pg_switch_wal()" > /dev/null
LAST_WRITE_EPOCH="$(now_epoch)"

log "4/7 WAL-Archivierung abwarten (archive_timeout 300 s, hier erzwungen)"
for i in $(seq 1 60); do
  ARCHIVED="$(psql_prod "select coalesce(extract(epoch from last_archived_time)::bigint, 0) from pg_stat_archiver")"
  if [ "${ARCHIVED:-0}" -ge "$LAST_WRITE_EPOCH" ]; then break; fi
  [ "$i" -eq 60 ] && { log "WAL wurde nicht archiviert"; psql_prod "select * from pg_stat_archiver"; exit 1; }
  sleep 2
done
LAST_ARCHIVED_EPOCH="$ARCHIVED"
EXPECTED_ROWS="$(psql_prod "select count(*) from drill.probe")"
EXPECTED_RELEASES="$(psql_prod "select count(*) from platform.release")"

log "5/7 Produktion zerstören: Container und Datenvolumes weg, Repository bleibt"
LOSS_EPOCH="$(now_epoch)"
"${PROD[@]}" down --remove-orphans > /dev/null
for v in pgdata pg-socket pgbackrest-spool pgbackrest-log; do docker volume rm -f "drill-prod_${v}" > /dev/null; done

log "6/7 Staging aus Images, Konfiguration und Repository aufbauen"
RESTORE_START_EPOCH="$(now_epoch)"
"${STAGING[@]}" run --rm --no-deps --entrypoint ch-backup-env.sh backup \
  bash -c 'mkdir -p /var/lib/postgresql/18/docker && chmod 0700 /var/lib/postgresql/18/docker && pgbackrest --stanza=main --log-level-console=info restore'
"${STAGING[@]}" up -d --wait postgres
for i in $(seq 1 120); do
  if psql_staging "select pg_is_in_recovery()" 2>/dev/null | grep -q '^f$'; then break; fi
  [ "$i" -eq 120 ] && { log "Staging kommt nicht aus der Wiederherstellung"; "${STAGING[@]}" logs postgres; exit 1; }
  sleep 2
done
RESTORE_END_EPOCH="$(now_epoch)"

log "7/7 Datenstand prüfen"
RESTORED_ROWS="$(psql_staging "select count(*) from drill.probe")"
RESTORED_RELEASES="$(psql_staging "select count(*) from platform.release")"
RESTORED_NOTES="$(psql_staging "select string_agg(note, ', ' order by id) from drill.probe")"

RTO=$((RESTORE_END_EPOCH - RESTORE_START_EPOCH))
RPO=$((LOSS_EPOCH - LAST_ARCHIVED_EPOCH))
RESULT="bestanden"
[ "$RESTORED_ROWS" = "$EXPECTED_ROWS" ] || RESULT="nicht bestanden (Zeilen $RESTORED_ROWS statt $EXPECTED_ROWS)"
[ "$RESTORED_RELEASES" = "$EXPECTED_RELEASES" ] || RESULT="nicht bestanden (Release-Zeilen $RESTORED_RELEASES statt $EXPECTED_RELEASES)"
[ "$RTO" -le "$RTO_LIMIT_SECONDS" ] || RESULT="nicht bestanden (RTO ${RTO}s)"
[ "$RPO" -le "$RPO_LIMIT_SECONDS" ] || RESULT="nicht bestanden (RPO ${RPO}s)"

mkdir -p "$(dirname "$REPORT")"
cat > "$REPORT" <<EOF
# Wiederherstellungsübung $(date -u +%Y-%m-%d)

**Nachweis:** Betrieb 9.1 und A-031: Neuaufbau aus Images, Konfiguration und Backups innerhalb der RTO (4 h) mit Datenstand innerhalb der RPO (15 min).
**Umgebung:** frische Compose-Umgebung (Projekt drill-staging) als Staging-Ersatz gemäß A-107; Repository auf Volume \`$OFFSITE_VOLUME\` als Stellvertreter des Offsite-Speichers.
**Zeitpunkt:** $(date -u +%FT%TZ) UTC; Host: $(hostname); Images: ${CH_IMAGE_POSTGRES:-lokal gebaut}.

| Messgröße | Wert | Grenze | Ergebnis |
|---|---|---|---|
| RTO (Wiederherstellung bis Datenbank bereit) | ${RTO} s | ${RTO_LIMIT_SECONDS} s | $([ "$RTO" -le "$RTO_LIMIT_SECONDS" ] && echo ok || echo überschritten) |
| RPO (Verlustzeitpunkt minus letztes archiviertes WAL) | ${RPO} s | ${RPO_LIMIT_SECONDS} s | $([ "$RPO" -le "$RPO_LIMIT_SECONDS" ] && echo ok || echo überschritten) |
| Fachzeilen drill.probe | ${RESTORED_ROWS} von ${EXPECTED_ROWS} | vollständig | $([ "$RESTORED_ROWS" = "$EXPECTED_ROWS" ] && echo ok || echo fehlt) |
| Release-Zeilen platform.release | ${RESTORED_RELEASES} von ${EXPECTED_RELEASES} | vollständig | $([ "$RESTORED_RELEASES" = "$EXPECTED_RELEASES" ] && echo ok || echo fehlt) |

Wiederhergestellte Notizen: ${RESTORED_NOTES}

**Ergebnis:** ${RESULT}

## Ablauf

1. Produktion (Projekt drill-prod) mit PostgreSQL 18 und Backup-Dienst gestartet, Schema per Migrations-Container angelegt.
2. Backup-Dienst legte Stanza an und erstellte die erste Basissicherung.
3. Zeilen nach der Basissicherung geschrieben, WAL-Wechsel erzwungen, Archivierung durch \`archive_command\` (pgBackRest) abgewartet.
4. Produktion gestoppt, Datenvolumes gelöscht; nur das Repository blieb.
5. Staging (Projekt drill-staging) mit leerem Datenverzeichnis: \`pgbackrest restore\`, Start mit WAL-Wiedergabe bis zum Ende, Promotion.
6. Datenstand geprüft. Löschläufe existieren in Stufe 1 noch nicht; ab ihrer Einführung werden sie nach jeder Wiederherstellung erneut ausgeführt (A-031).

## pgBackRest-Stand vor der Zerstörung

\`\`\`
${BACKUP_INFO}
\`\`\`
EOF

log "Protokoll: $REPORT"
log "Ergebnis: $RESULT (RTO ${RTO}s, RPO ${RPO}s)"
[ "$RESULT" = "bestanden" ]
