# syntax=docker/dockerfile:1.7
# Ein Image für API und Worker (A-012: zwei Prozesse aus derselben Codebasis), ein eigenes Image für den
# Migrations-Container (Betrieb 4) und ein Web-Image mit Caddy und dem Angular-Build (A-032: eine Origin).
# Alle Laufzeit-Container laufen ohne Root (A-028). Versionen: durchstich/versionen.md.

ARG DOTNET_VERSION=10.0
ARG NODE_VERSION=24.15.0
ARG CADDY_VERSION=2.11

# ---------- Backend-Build ----------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS backend-build
ARG RELEASE_VERSION=0.0.0-dev
WORKDIR /src
COPY global.json .editorconfig Directory.Build.props Directory.Packages.props CompanyHero.slnx ./
COPY src/backend/ src/backend/
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore src/backend/CompanyHero.Api/CompanyHero.Api.csproj --locked-mode \
 && dotnet restore src/backend/CompanyHero.Worker/CompanyHero.Worker.csproj --locked-mode \
 && dotnet restore src/backend/CompanyHero.Migrations/CompanyHero.Migrations.csproj --locked-mode
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/backend/CompanyHero.Api/CompanyHero.Api.csproj -c Release --no-restore -o /out/app/api -p:InformationalVersion=${RELEASE_VERSION} \
 && dotnet publish src/backend/CompanyHero.Worker/CompanyHero.Worker.csproj -c Release --no-restore -o /out/app/worker -p:InformationalVersion=${RELEASE_VERSION} \
 && dotnet publish src/backend/CompanyHero.Migrations/CompanyHero.Migrations.csproj -c Release --no-restore -o /out/migrate -p:InformationalVersion=${RELEASE_VERSION}

# ---------- Gemeinsame Laufzeit ----------
# GSSAPI-Bibliothek fehlt im Basis-Image; die .NET-Netzwerkschicht meldet das sonst bei jedem Start auf stderr.
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime-base
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*

# ---------- Anwendungs-Image: API und Worker ----------
FROM runtime-base AS app
ENV ASPNETCORE_URLS=http://+:8080 \
    CH_PROCESS=api
WORKDIR /app
COPY --from=backend-build /out/app/ /app/
COPY --chmod=0755 deploy/docker/entrypoint-app.sh /usr/local/bin/entrypoint-app.sh
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["/usr/local/bin/entrypoint-app.sh"]

# ---------- Migrations-Image ----------
# aspnet-Laufzeit, weil die Plattformbibliothek das ASP.NET-Core-Framework referenziert (Hosting, Health).
FROM runtime-base AS migrate
WORKDIR /app
COPY --from=backend-build /out/migrate/ /app/
COPY --chmod=0755 deploy/docker/entrypoint-migrate.sh /usr/local/bin/entrypoint-migrate.sh
USER $APP_UID
ENTRYPOINT ["/usr/local/bin/entrypoint-migrate.sh"]

# ---------- Frontend-Build ----------
FROM node:${NODE_VERSION}-bookworm-slim AS frontend-build
WORKDIR /web
COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci --no-audit --no-fund
COPY src/frontend/ ./
RUN npm run build

# ---------- Web-Image: Caddy mit App-Shell ----------
FROM caddy:${CADDY_VERSION}-alpine AS web
RUN addgroup -S caddy && adduser -S -G caddy caddy \
 && mkdir -p /data /config /srv/web && chown -R caddy:caddy /data /config /srv/web
# Das Basis-Image setzt Datei-Capabilities auf caddy; mit no-new-privileges wäre exec dann verboten. Ports 80/443 kommen über die Sysctl.
RUN apk add --no-cache libcap && setcap -r /usr/bin/caddy && apk del libcap
COPY deploy/compose/plattform/caddy/Caddyfile /etc/caddy/Caddyfile
COPY --from=frontend-build --chown=caddy:caddy /web/dist/companyhero-frontend/browser/ /srv/web/
USER caddy
EXPOSE 80 443
