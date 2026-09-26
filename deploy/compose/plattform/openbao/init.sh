#!/bin/sh
# Einrichtung von OpenBao für die Plattform (A-029, Betrieb 3.1). Idempotent.
#
# Produktion:  Initialisierung und Entsiegelung erfolgen manuell durch zwei benannte Personen; dieses Skript
#              wartet auf den entsiegelten Zustand und richtet dann mit dem Root-Token aus CH_OPENBAO_ROOT_TOKEN_FILE ein.
# Lokal/CI:    CH_OPENBAO_AUTO_INIT=true initialisiert mit zwei Schlüsselteilen, legt sie unter /openbao/init ab und entsiegelt.
#              Diese Ablage ist ausdrücklich nicht für Produktion gedacht.
#
# Ergebnis: KV-v2 unter "companyhero", Transit "companyhero-transit", Policy und Token für API/Worker unter
# /run/companyhero/openbao-app-token, Datenbankgeheimnis app/database aus dem Passwort der Laufzeitrolle.
set -eu

export BAO_ADDR="${BAO_ADDR:-http://openbao:8200}"
INIT_FILE=/openbao/init/init.json
RUNTIME_DIR=/run/companyhero

log() { printf '{"Timestamp":"%s","Category":"openbao-init","Message":"%s"}\n' "$(date -u +%FT%TZ)" "$*"; }

health_code() {
  # 200 aktiv und entsiegelt, 429 Standby, 501 nicht initialisiert, 503 versiegelt
  wget -q -S -O /dev/null "$BAO_ADDR/v1/sys/health" 2>&1 | sed -n 's/.*HTTP\/[0-9.]* \([0-9]*\).*/\1/p' | head -1
}

wait_for_api() {
  i=0
  while [ -z "$(health_code)" ]; do
    i=$((i + 1)); [ "$i" -gt 60 ] && { log "OpenBao antwortet nicht"; exit 1; }
    sleep 2
  done
}

wait_for_active() {
  i=0
  until [ "$(health_code)" = "200" ]; do
    i=$((i + 1)); [ "$i" -gt "${1:-60}" ] && return 1
    sleep 2
  done
}

is_initialized() { bao status -format=json 2>/dev/null | grep -q '"initialized": true'; }
is_sealed() { bao status -format=json 2>/dev/null | grep -q '"sealed": true'; }

wait_for_api

if ! is_initialized; then
  if [ "${CH_OPENBAO_AUTO_INIT:-false}" != "true" ]; then
    log "OpenBao ist nicht initialisiert. Produktion: bao operator init durch zwei Personen; danach dieses Skript erneut."
    exit 1
  fi
  log "Initialisiere OpenBao (nur lokal/CI)"
  mkdir -p /openbao/init
  bao operator init -key-shares=2 -key-threshold=2 -format=json > "$INIT_FILE"
  chmod 0600 "$INIT_FILE"
fi

if is_sealed; then
  if [ -f "$INIT_FILE" ]; then
    log "Entsiegele mit lokal abgelegten Schlüsselteilen (nur lokal/CI)"
    for key in $(sed -n '/"unseal_keys_b64"/,/\]/p' "$INIT_FILE" | grep -o '"[A-Za-z0-9+/=]\{20,\}"' | tr -d '"'); do
      bao operator unseal "$key" > /dev/null
    done
  else
    i=0
    while is_sealed; do
      i=$((i + 1)); [ "$i" -gt 900 ] && { log "OpenBao bleibt versiegelt"; exit 1; }
      [ $((i % 30)) -eq 1 ] && log "Warte auf manuelle Entsiegelung durch zwei Personen"
      sleep 2
    done
  fi
fi

# Nach der Entsiegelung braucht der Raft-Knoten einen Moment bis zum aktiven Zustand.
wait_for_active 60 || { log "OpenBao wird nach der Entsiegelung nicht aktiv"; bao status || true; exit 1; }

if [ -n "${CH_OPENBAO_ROOT_TOKEN_FILE:-}" ] && [ -s "$CH_OPENBAO_ROOT_TOKEN_FILE" ]; then
  BAO_TOKEN="$(cat "$CH_OPENBAO_ROOT_TOKEN_FILE")"
elif [ -f "$INIT_FILE" ]; then
  BAO_TOKEN="$(sed -n 's/.*"root_token": "\([^"]*\)".*/\1/p' "$INIT_FILE")"
else
  log "Kein Root-Token für die Einrichtung verfügbar"
  exit 1
fi
export BAO_TOKEN

MOUNTS="$(bao secrets list -format=json)"
echo "$MOUNTS" | grep -q '"companyhero/"' || bao secrets enable -path=companyhero -version=2 kv > /dev/null
echo "$MOUNTS" | grep -q '"companyhero-transit/"' || bao secrets enable -path=companyhero-transit transit > /dev/null

bao policy write companyhero-app - > /dev/null <<'EOF'
# API und Worker: Anwendungsgeheimnisse lesen, Tenant-Datenschlüssel verwenden, nie exportieren.
path "companyhero/data/app/*"                   { capabilities = ["read"] }
path "companyhero-transit/encrypt/tenant-*"     { capabilities = ["update"] }
path "companyhero-transit/decrypt/tenant-*"     { capabilities = ["update"] }
path "companyhero-transit/keys/tenant-*"        { capabilities = ["create", "read", "update"] }
path "companyhero-transit/keys/tenant-*/rotate" { capabilities = ["update"] }
EOF

APP_PASSWORD="$(cat /run/secrets/db_app_password)"
DB_HOST="${CH_DB_HOST:-postgres}"
DB_NAME="${CH_DB_NAME:-companyhero}"
bao kv put -mount=companyhero app/database \
  "ConnectionStrings__Default=Host=${DB_HOST};Database=${DB_NAME};Username=ch_app;Password=${APP_PASSWORD};Maximum Pool Size=50" > /dev/null

# Benachrichtigungen (A-059, A-032): VAPID-Schlüssel und SMTP-Zugang als Anwendungsgeheimnis; Werte kommen aus Docker-Secrets und .env.
VAPID_PEM="$(cat /run/secrets/vapid_private_key 2>/dev/null || true)"
SMTP_PASSWORD="$(cat /run/secrets/smtp_password 2>/dev/null || true)"
bao kv put -mount=companyhero app/notifications \
  "Notifications__Vapid__PrivateKeyPem=${VAPID_PEM}" \
  "Notifications__Vapid__Subject=${CH_VAPID_SUBJECT:-mailto:betrieb@localhost}" \
  "Notifications__Smtp__Host=${CH_SMTP_HOST:-}" \
  "Notifications__Smtp__Port=${CH_SMTP_PORT:-587}" \
  "Notifications__Smtp__Username=${CH_SMTP_USERNAME:-}" \
  "Notifications__Smtp__Password=${SMTP_PASSWORD}" \
  "Notifications__Smtp__From=${CH_SMTP_FROM:-companyhero@localhost}" \
  "Notifications__Smtp__FromName=${CH_SMTP_FROM_NAME:-CompanyHero}" > /dev/null

mkdir -p "$RUNTIME_DIR"
bao token create -policy=companyhero-app -period=720h -orphan -format=json \
  | sed -n 's/.*"client_token": "\([^"]*\)".*/\1/p' > "$RUNTIME_DIR/openbao-app-token.tmp"
[ -s "$RUNTIME_DIR/openbao-app-token.tmp" ] || { log "Token konnte nicht ausgestellt werden"; exit 1; }
mv "$RUNTIME_DIR/openbao-app-token.tmp" "$RUNTIME_DIR/openbao-app-token"
chmod 0644 "$RUNTIME_DIR/openbao-app-token"

log "OpenBao eingerichtet: KV companyhero (app/database, app/notifications), Transit companyhero-transit, Token für companyhero-app ausgestellt"
