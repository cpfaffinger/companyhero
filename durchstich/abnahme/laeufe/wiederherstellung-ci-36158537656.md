# Wiederherstellungsübung 2026-09-25

**Nachweis:** Betrieb 9.1 und A-031: Neuaufbau aus Images, Konfiguration und Backups innerhalb der RTO (4 h) mit Datenstand innerhalb der RPO (15 min).
**Umgebung:** frische Compose-Umgebung (Projekt drill-staging) als Staging-Ersatz gemäß A-107; Repository auf Volume `drill-offsite-20260925161220` als Stellvertreter des Offsite-Speichers.
**Zeitpunkt:** 2026-09-25T16:13:19Z UTC; Host: runnervmtr4k5; Images: ghcr.io/cpfaffinger/companyhero-postgres@sha256:31d1870e865180f7006f421ed3874fac840934a708d8b2c8cd6b032fdec19a8d.

| Messgröße | Wert | Grenze | Ergebnis |
|---|---|---|---|
| RTO (Wiederherstellung bis Datenbank bereit) | 7 s | 14400 s | ok |
| RPO (Verlustzeitpunkt minus letztes archiviertes WAL) | 2 s | 900 s | ok |
| Fachzeilen drill.probe | 2 von 2 | vollständig | ok |
| Release-Zeilen platform.release | 1 von 1 | vollständig | ok |

Wiederhergestellte Notizen: nach-basissicherung-1, nach-basissicherung-2

**Ergebnis:** bestanden

## Ablauf

1. Produktion (Projekt drill-prod) mit PostgreSQL 18 und Backup-Dienst gestartet, Schema per Migrations-Container angelegt.
2. Backup-Dienst legte Stanza an und erstellte die erste Basissicherung.
3. Zeilen nach der Basissicherung geschrieben, WAL-Wechsel erzwungen, Archivierung durch `archive_command` (pgBackRest) abgewartet.
4. Produktion gestoppt, Datenvolumes gelöscht; nur das Repository blieb.
5. Staging (Projekt drill-staging) mit leerem Datenverzeichnis: `pgbackrest restore`, Start mit WAL-Wiedergabe bis zum Ende, Promotion.
6. Datenstand geprüft. Löschläufe existieren in Stufe 1 noch nicht; ab ihrer Einführung werden sie nach jeder Wiederherstellung erneut ausgeführt (A-031).

## pgBackRest-Stand vor der Zerstörung

```
stanza: main
    status: ok
    cipher: aes-256-cbc

    db (current)
        wal archive min/max (18): 000000010000000000000001/000000010000000000000004

        full backup: 20260925-161251F
            timestamp start/stop: 2026-09-25 16:12:51+00 / 2026-09-25 16:12:52+00
            wal start/stop: 000000010000000000000004 / 000000010000000000000004
            database size: 30MB, database backup size: 30MB
            repo1: backup size: 3.6MB
```
