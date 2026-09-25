#!/bin/bash
# Gemeinsamer Einstieg für Backup-Dienst und Wiederherstellung: lädt das Repository-Passwort aus dem Secret.
set -euo pipefail
if [ -n "${CH_BACKUP_CIPHER_PASS_FILE:-}" ]; then
  PGBACKREST_REPO1_CIPHER_PASS="$(cat "$CH_BACKUP_CIPHER_PASS_FILE")"
  export PGBACKREST_REPO1_CIPHER_PASS
fi
exec "$@"
