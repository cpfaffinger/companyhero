#!/usr/bin/env bash
# Erzeugt die Bootstrap-Geheimnisse der Compose-Projekte als Dateien (Betrieb 3.1):
# nur lesbar für den Deploy-Benutzer, nie im Repository. Bestehende Dateien bleiben unverändert.
set -euo pipefail

DIR="${1:-$(cd "$(dirname "$0")/../compose/plattform" && pwd)/secrets}"
BEOB="$(cd "$(dirname "$DIR")/../beobachtung" && pwd)/secrets"
mkdir -p "$DIR" "$BEOB"
chmod 0700 "$DIR" "$BEOB"

gen() {
  local dir="$1" name="$2" bytes="${3:-32}"
  if [ ! -s "$dir/$name" ]; then
    openssl rand -hex "$bytes" | tr -d '\n' > "$dir/$name"
    chmod 0600 "$dir/$name"
    echo "erzeugt: $name"
  fi
}

empty() {
  local dir="$1" name="$2"
  [ -e "$dir/$name" ] || { : > "$dir/$name"; chmod 0600 "$dir/$name"; }
}

gen "$DIR" postgres_superuser_password 24
gen "$DIR" db_migrator_password 24
gen "$DIR" db_app_password 24
gen "$DIR" backup_cipher_pass 32
gen "$DIR" garage_rpc_secret 32
gen "$DIR" garage_admin_token 32
# VAPID-Schlüsselpaar der Plattform für Web Push (A-059, Benachrichtigungen 3.1): privater Schlüssel als PEM, der öffentliche
# wird zur Laufzeit abgeleitet; Rotation ist ein geplanter Vorgang (neuen Schlüssel erzeugen, Abonnements erneuern sich beim App-Start).
if [ ! -s "$DIR/vapid_private_key" ]; then
  openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt > "$DIR/vapid_private_key"
  chmod 0600 "$DIR/vapid_private_key"
  echo "erzeugt: vapid_private_key"
fi
# SMTP-Zugangsdaten des Betreibers (A-032): leer, bis der Transport konfiguriert ist; Host, Port und Absender stehen in .env.
empty "$DIR" smtp_password
# Platzhalter für Produktion; das Root-Token entsteht bei der manuellen Initialisierung von OpenBao durch zwei Personen.
empty "$DIR" openbao_root_token

# Beobachtungsprojekt: Grafana-Erstzugang und OAuth-Geheimnis (leer, bis der Anbieter konfiguriert ist)
if [ ! -s "$BEOB/grafana_admin_user" ]; then printf 'admin' > "$BEOB/grafana_admin_user"; chmod 0600 "$BEOB/grafana_admin_user"; fi
gen "$BEOB" grafana_admin_password 24
empty "$BEOB" grafana_oauth_client_secret

echo "Geheimnisse liegen unter $DIR und $BEOB"
