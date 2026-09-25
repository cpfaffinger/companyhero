#!/bin/bash
# Startet PostgreSQL mit WAL-Archivierung an pgBackRest (A-031). Das Verschlüsselungspasswort des Repositorys
# kommt aus einem Docker-Secret und wird als Umgebungsvariable an Server und Backup-Dienst weitergereicht.
set -euo pipefail

if [ -n "${CH_BACKUP_CIPHER_PASS_FILE:-}" ]; then
  PGBACKREST_REPO1_CIPHER_PASS="$(cat "$CH_BACKUP_CIPHER_PASS_FILE")"
  export PGBACKREST_REPO1_CIPHER_PASS
fi

exec docker-entrypoint.sh postgres \
  -c listen_addresses='*' \
  -c wal_level=replica \
  -c archive_mode=on \
  -c archive_command='pgbackrest --stanza=main archive-push %p' \
  -c archive_timeout=300 \
  -c max_wal_senders=3 \
  -c log_min_duration_statement=-1 \
  -c log_statement=none \
  -c log_connections=off \
  -c log_disconnections=off \
  -c timezone=UTC \
  "$@"
