#!/usr/bin/env bash
# Einrichtung von Garage (A-029) vom Deploy-Host aus: Layout für einen Knoten, Bucket für Medien, Zugangsschlüssel
# für die Anwendung. Idempotent. Das Garage-Image enthält keine Shell, daher laufen die Befehle über compose exec.
# Der Zugangsschlüssel wird in OpenBao unter companyhero/app/storage abgelegt, nie in Dateien oder Logs.
set -euo pipefail

COMPOSE=(docker compose -f "$(cd "$(dirname "$0")/../compose/plattform" && pwd)/compose.yaml")
BUCKET="${CH_GARAGE_BUCKET:-companyhero-media}"
KEY_NAME="${CH_GARAGE_KEY_NAME:-companyhero-app}"
CAPACITY="${CH_GARAGE_CAPACITY:-50G}"

g() { "${COMPOSE[@]}" exec -T garage /garage -c /etc/garage.toml "$@"; }

for _ in $(seq 1 30); do g status > /dev/null 2>&1 && break; sleep 2; done

NODE_ID="$(g node id -q 2>/dev/null | cut -d@ -f1 | tr -d '\r')"
if ! g layout show 2>/dev/null | grep -q "${NODE_ID:0:16}"; then
  g layout assign -z dc1 -c "$CAPACITY" "$NODE_ID" > /dev/null
  VERSION="$(g layout show | sed -n 's/.*version \([0-9]*\).*/\1/p' | tail -1 | tr -d '\r')"
  g layout apply --version "$(( ${VERSION:-0} + 1 ))" > /dev/null
  echo "Garage: Layout zugewiesen"
fi

g bucket info "$BUCKET" > /dev/null 2>&1 || { g bucket create "$BUCKET" > /dev/null; echo "Garage: Bucket $BUCKET angelegt"; }
g key info "$KEY_NAME" > /dev/null 2>&1 || { g key create "$KEY_NAME" > /dev/null; echo "Garage: Schlüssel $KEY_NAME angelegt"; }
g bucket allow --read --write --owner "$BUCKET" --key "$KEY_NAME" > /dev/null

KEY_INFO="$(g key info --show-secret "$KEY_NAME" | tr -d '\r')"
ACCESS_KEY="$(printf '%s\n' "$KEY_INFO" | sed -n 's/^Key ID: *//p' | head -1)"
SECRET_KEY="$(printf '%s\n' "$KEY_INFO" | sed -n 's/^Secret key: *//p' | head -1)"

ROOT_TOKEN_FILE="${CH_OPENBAO_ROOT_TOKEN_FILE:-}"
if [ -z "$ROOT_TOKEN_FILE" ]; then
  # Lokal/CI: Root-Token aus der automatischen Initialisierung
  ROOT_TOKEN="$("${COMPOSE[@]}" exec -T openbao-init sh -c 'sed -n "s/.*\"root_token\": \"\([^\"]*\)\".*/\1/p" /openbao/init/init.json' 2>/dev/null || true)"
else
  ROOT_TOKEN="$(cat "$ROOT_TOKEN_FILE")"
fi

if [ -n "${ROOT_TOKEN:-}" ]; then
  "${COMPOSE[@]}" exec -T -e BAO_ADDR=http://openbao:8200 -e BAO_TOKEN="$ROOT_TOKEN" openbao \
    bao kv put -mount=companyhero app/storage \
      Storage__Endpoint=http://garage:3900 Storage__Region=garage Storage__Bucket="$BUCKET" \
      Storage__AccessKey="$ACCESS_KEY" Storage__SecretKey="$SECRET_KEY" > /dev/null
  echo "Garage: Zugangsschlüssel in OpenBao unter app/storage abgelegt"
else
  echo "Garage: kein OpenBao-Token verfügbar; Zugangsschlüssel nicht hinterlegt" >&2
  exit 1
fi
