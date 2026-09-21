# CompanyHero – Verzeichnis und Netzwerk

**Stand:** 21.09.2026  
**Status:** Beschlossen gemäß A-105 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung.  
**Art:** Modul M12 als Erweiterung der Domänen Identität und Zugang sowie Organisation und Mandanten.  
**Bezug:** [Zugang und Identität](zugang-und-identitaet.md), [Organisation und Mandanten](organisation-und-mandanten.md), [Datenschutz und Nachweis](datenschutz-und-nachweis.md).

Externe Anmeldeanbieter und Anbieterzwang gehören zum Kern. Das Modul Verzeichnis und Netzwerk ergänzt, was Großkunden ab etwa 500 Beschäftigten zusätzlich verlangen: Gruppenstruktur und Sollstärken aus dem Verzeichnis, ein Deaktivierungssignal beim Ausscheiden, Netzwerkregeln für die Verwaltung und einen Protokollexport. Es importiert keine Personen.

## 1. Grundsätze

- **Kein Personenimport.** Der Beitritt bleibt anonym über Codes. Namen, E-Mails und Mitgliederlisten aus dem Verzeichnis werden beim Empfang verworfen und nie gespeichert.
- **Nur zwei Informationen aus dem Verzeichnis:** Gruppen mit Mitgliederanzahl für Sollstärken und ein Deaktivierungssignal je Identität.
- **Netzwerkregeln nur für die Verwaltung.** Mitglieder-App und Kiosk sind nie von IP-Regeln betroffen.
- **Protokollexport ohne Personenbezug auf Mitglieder**, wie das Prüfprotokoll selbst.

## 2. Verzeichnisanbindung (A-105)

### 2.1 Anbindung

| Element | Festlegung |
|---|---|
| Protokoll | SCIM 2.0 als Empfänger; der Tenant konfiguriert seinen Verzeichnisdienst (etwa Entra ID) mit Endpunkt und Bearer-Token aus der Verwaltung; Token in OpenBao, jederzeit rotierbar |
| Gruppen | Verzeichnisgruppen werden Gruppen einer Dimension zugeordnet (Mapping in der Verwaltung); die Mitgliederanzahl der Verzeichnisgruppe wird zur Sollstärke mit Stichtag; Mitgliederkennungen werden nach der Zählung verworfen |
| Benutzer | Benutzerressourcen werden angenommen, aber nur `externalId` und `active` verwendet; alle anderen Attribute werden verworfen; es entsteht keine Person |
| Deaktivierungssignal | `active = false` oder Löschung: Ist die `externalId` als Subject einer Anbieterverknüpfung bekannt, werden Sitzungen der Person beendet und die Verknüpfung entfernt. Ohne anderen Anmeldeweg wird die Person nach 30 Tagen ohne Anmeldung automatisch ausgetreten (A-019); mit anderem Weg bleibt die Mitgliedschaft |
| Reaktivierung | `active = true` innerhalb der 30 Tage stellt die Verknüpfung wieder her |
| Protokoll | jede Gruppen- und Sollstärkenänderung und jedes Deaktivierungssignal im Prüfprotokoll ohne Personenbezug |

Was der Tenant sieht: Zuordnung der Gruppen, Zeitpunkt der letzten Synchronisierung, Anzahl verarbeiteter Signale je Tag. Nie: welche Person deaktiviert wurde.

### 2.2 Netzwerkregeln

- Allowlist von IP-Bereichen für den Zugriff auf die Verwaltung und auf Rollen mit Verwaltungsrechten (Tenant-Admin, Programm-Manager, Redakteur, Einsichtsrolle). Außerhalb der Bereiche werden diese Rollen abgelehnt; Mitgliedsfunktionen derselben Person bleiben erreichbar.
- Mitglieder-App, Kiosk, Beitritt und Anmeldung sind nie betroffen.
- Eine Sperrliste, die den Tenant-Admin selbst aussperren würde, wird abgelehnt; die Verwaltung prüft die aktuelle Adresse vor dem Speichern.

### 2.3 Protokollexport

- Täglicher Export des Prüfprotokolls (A-025) als JSON-Datei über einen signierten Verweis in der Verwaltung.
- Optional signierter HTTPS-Push an einen vom Tenant konfigurierten Endpunkt mit Wiederholung und Zustellstatus.
- Inhalt wie das Prüfprotokoll: Klarnamen der Funktionsrollen, keine Mitgliederdaten.

## 3. Schnittmengen

| Schnittmenge | Festlegung |
|---|---|
| Verzeichnis ↔ Zugang | Deaktivierungssignal über Subject; Sitzungen beenden; Anbieterverknüpfung entfernen; 30 Tage bis zum automatischen Austritt |
| Verzeichnis ↔ Organisation | Gruppen-Mapping, Sollstärke mit Stichtag aus Mitgliederanzahl |
| Verzeichnis ↔ Datenschutz | kein Personenimport; Verwerfen aller Attribute außer `externalId` und `active`; Prüfprotokoll |
| Verzeichnis ↔ Betrieb | SCIM-Endpunkt über den Reverse Proxy mit Ratenbegrenzung; Token in OpenBao; Push-Export signiert |
| Verzeichnis ↔ Entitlements und Metering | M12, `directory.sync_month` |

## 4. Nachweise

1. SCIM-Benutzer mit Namen und E-Mail erzeugt keine Person und keinen gespeicherten Namen; Datenbank enthält nur `externalId` als Subject, sofern eine Verknüpfung existiert.
2. Gruppen-Synchronisierung setzt Sollstärken mit Stichtag; Mitgliederkennungen sind nach der Zählung nicht gespeichert.
3. Deaktivierung beendet Sitzungen und entfernt die Verknüpfung; Person mit Passkey bleibt Mitglied; Person ohne anderen Weg ist nach 30 Tagen ausgetreten; Reaktivierung innerhalb der Frist stellt wieder her.
4. IP-Regel sperrt die Verwaltung außerhalb der Bereiche, nicht die Mitglieder-App derselben Person; selbstaussperrende Regel wird abgelehnt.
5. Protokollexport enthält keine Mitgliederdaten; Push-Zustellung mit Wiederholung.
