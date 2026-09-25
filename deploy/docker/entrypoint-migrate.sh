#!/bin/bash
# Migrations-Container (Betrieb 4): eigener Datenbankzugang als Docker-Secret, läuft vor API und Worker.
set -euo pipefail

if [ -z "${ConnectionStrings__Migrator:-}" ]; then
  HOST="${CH_DB_HOST:-postgres}"
  NAME="${CH_DB_NAME:-companyhero}"
  PASSWORD="$(cat "${CH_MIGRATOR_PASSWORD_FILE:-/run/secrets/db_migrator_password}")"
  export ConnectionStrings__Migrator="Host=${HOST};Database=${NAME};Username=ch_migrator;Password=${PASSWORD}"
fi

exec dotnet /app/CompanyHero.Migrations.dll "$@"
