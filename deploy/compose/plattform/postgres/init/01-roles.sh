#!/bin/bash
# Datenbankrollen beim ersten Start des PostgreSQL-Containers (Backend 5.1 Nr. 4, Betrieb 4):
#   ch_migrator  Besitzer der Datenbank und aller Schemata; einziger Zugang des Migrations-Containers
#   ch_app       Laufzeitrolle für API und Worker: kein Besitz, kein BYPASSRLS, kein CREATE
# Passwörter kommen aus Docker-Secrets (*_FILE) oder, in Tests, direkt aus Umgebungsvariablen.
set -euo pipefail

read_secret() {
  local var="$1" file_var="$1_FILE"
  if [ -n "${!file_var:-}" ]; then cat "${!file_var}"; else printf '%s' "${!var:?$var oder ${file_var} muss gesetzt sein}"; fi
}

MIGRATOR_PASSWORD="$(read_secret CH_MIGRATOR_PASSWORD)"
APP_PASSWORD="$(read_secret CH_APP_PASSWORD)"
DB_NAME="${CH_DB_NAME:-companyhero}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  -v migrator_password="$MIGRATOR_PASSWORD" -v app_password="$APP_PASSWORD" -v db_name="$DB_NAME" <<-'EOSQL'
  CREATE ROLE ch_migrator LOGIN PASSWORD :'migrator_password' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS NOINHERIT;
  CREATE ROLE ch_app LOGIN PASSWORD :'app_password' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS NOINHERIT;
  CREATE DATABASE :"db_name" OWNER ch_migrator;
  REVOKE ALL ON DATABASE :"db_name" FROM PUBLIC;
  GRANT CONNECT ON DATABASE :"db_name" TO ch_app;
EOSQL

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$DB_NAME" <<-'EOSQL'
  REVOKE ALL ON SCHEMA public FROM PUBLIC;
  GRANT USAGE ON SCHEMA public TO ch_migrator;
  ALTER SCHEMA public OWNER TO ch_migrator;
EOSQL
