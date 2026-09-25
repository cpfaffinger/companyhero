#!/bin/sh
# Verteilt die Bootstrap-Geheimnisse (Docker-Secrets, auf dem Host nur für den Deploy-Benutzer lesbar, Betrieb 3.1)
# an die Container ohne Root: je Verbraucher ein Verzeichnis im Laufzeitvolume mit passendem Besitzer und Modus 0400.
# Läuft einmalig als Root vor Datenbank, Backup-Dienst und Migrations-Container; idempotent.
#   999  postgres im offiziellen PostgreSQL-Image (Server, Init-Skript, Backup-Dienst)
#   1654 APP_UID der .NET-Laufzeit-Images (Migrations-Container)
set -eu

R=/run/companyhero
S=/run/secrets
PG_UID=999
APP_UID=1654

install -d -m 0755 "$R"
install -d -m 0700 -o "$PG_UID" -g "$PG_UID" "$R/postgres"
for f in postgres_superuser_password db_migrator_password db_app_password backup_cipher_pass; do
  install -m 0400 -o "$PG_UID" -g "$PG_UID" "$S/$f" "$R/postgres/$f"
done

install -d -m 0700 -o "$APP_UID" -g "$APP_UID" "$R/migrate"
install -m 0400 -o "$APP_UID" -g "$APP_UID" "$S/db_migrator_password" "$R/migrate/db_migrator_password"

printf '{"Timestamp":"%s","Category":"secrets-init","Message":"Geheimnisse an postgres (%s) und migrate (%s) verteilt"}\n' "$(date -u +%FT%TZ)" "$PG_UID" "$APP_UID"
