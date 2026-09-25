#!/bin/sh
# Ein Image, zwei Prozesse (A-012, Backend 3.1): CH_PROCESS wählt den Einstiegspunkt.
set -eu
case "${CH_PROCESS:-api}" in
  api)    exec dotnet /app/api/CompanyHero.Api.dll "$@" ;;
  worker) exec dotnet /app/worker/CompanyHero.Worker.dll "$@" ;;
  *) echo "CH_PROCESS muss api oder worker sein, ist: ${CH_PROCESS}" >&2; exit 64 ;;
esac
