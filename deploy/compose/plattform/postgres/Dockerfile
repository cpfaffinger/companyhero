# PostgreSQL 18 (A-011) mit pgBackRest (A-031) im selben Image: der Backup-Dienst teilt Datenverzeichnis
# und Socket mit dem Server und läuft als Benutzer postgres, nie als Root.
ARG POSTGRES_VERSION=18
FROM postgres:${POSTGRES_VERSION}

RUN apt-get update \
 && apt-get install -y --no-install-recommends pgbackrest \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /var/log/pgbackrest /var/lib/pgbackrest /var/spool/pgbackrest /etc/pgbackrest/conf.d \
 && chown -R postgres:postgres /var/log/pgbackrest /var/lib/pgbackrest /var/spool/pgbackrest /etc/pgbackrest

COPY --chown=postgres:postgres pgbackrest.conf /etc/pgbackrest/pgbackrest.conf
COPY --chmod=0755 init/ /docker-entrypoint-initdb.d/
COPY --chmod=0755 bin/ /usr/local/bin/
