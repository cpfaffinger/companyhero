# CompanyHero – Zugang und Identität

**Stand:** 20.09.2026  
**Status:** Beschlossen gemäß A-005, A-007, A-008 und A-014 bis A-019 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung.  
**Domäne:** Identität und Zugang mit Schnittmenge zu Organisation und Mandanten gemäß [Domänenkarte](domaenen-und-schnittmengen.md).  
**Bezug:** [Backend](architektur-backend.md), [Integrationsregeln](architektur-integrationsregeln.md).

## 1. Grundsätze

- Jede Person tritt über einen Beitrittscode einem Tenant bei. Kein Klarname, keine E-Mail, kein Geburtsdatum und keine Körpermaße sind Voraussetzung.
- Eine Person gehört zu genau einem Tenant. Eine Login-Identität ist mit höchstens einer aktiven Person verknüpft. Firmenwechsel bedeutet Austritt und neuer Beitritt.
- Anmeldung und Mitgliedschaft sind getrennt. Eine geprüfte Identität ohne Person im Tenant hat keinen Zugang; Rollen kommen aus dem Organisationsmodul, nie aus Anbieterclaims.
- Mitglieder und Gesundheitsbotschafter handeln unter frei gewähltem Anzeigenamen. Funktionsrollen handeln unter Klarnamen.
- Kein Zugangsweg erzeugt eine Liste von Mitgliedern auf einem Gemeinschaftsgerät oder gegenüber einer Rolle, die sie nicht sehen darf.
- Alle Codes, Links und Kennungen sind zufällig, nicht sequenziell und in einem Alphabet ohne verwechselbare Zeichen.

## 2. Beitritt und Rollenvergabe (A-014)

### 2.1 Beitrittscodes

| Eigenschaft | Festlegung |
|---|---|
| Form | acht Zeichen aus einem Alphabet ohne verwechselbare Zeichen; QR-Code enthält `https://<Origin>/join/<Code>` |
| Ersteller | Tenant-Admin, Programm-Manager |
| Mehrere Codes | erlaubt, etwa je Standort oder Schicht mit vorbelegter Gruppe |
| Gültigkeit | Standard 180 Tage, verlängerbar; optionales Nutzungslimit; jederzeit widerrufbar |
| Serverprüfung | Der Code bestimmt den Tenant serverseitig; die Antwort enthält nur Tenant-Name, Logo und Anrede |
| Schutz | Ratenbegrenzung je Quelle und Bot-Schutz auf der Beitrittsroute; ungültige Codes liefern keine Information über existierende Codes |

### 2.2 Ablauf des Beitritts

1. Code eingeben oder QR scannen.
2. Tenant-Vorschau bestätigen.
3. Anzeigenamen wählen; ein Vorschlag ist vorbelegt. Anzeigenamen sind je Tenant eindeutig, änderbar durch die Person selbst.
4. Je Gruppendimension eine Gruppe wählen, sofern der Tenant Gruppen pflegt; Vorbelegung aus dem Code möglich.
5. Sichtbarkeitsstufe ausdrücklich wählen: Nur für mich, Mein Team oder Ganze Firma, ohne Voreinstellung (A-022).
6. Zugang sichern, abhängig vom Gerät:
   - **Eigenes Gerät:** Passkey einrichten oder E-Mail für Magic-Link hinterlegen oder mit externem Anbieter verknüpfen. Ohne E-Mail wird zusätzlich ein Wiederherstellungscode angezeigt.
   - **Kiosk:** Kiosk-Kennung wird angezeigt, PIN wird gewählt.
7. Erste Handlung und Installationshinweis gehören zum Onboarding-Thema.

Der Beitritt erzeugt die Person, die Mitgliedschaft im Tenant, die gewählten Gruppenzugehörigkeiten, die Kiosk-Kennung und die gewählten Anmeldewege in einer Transaktion.

### 2.3 Rollencodes

Rollen werden über personalisierte Einmalcodes vergeben. Der Aussteller übergibt den Code außerhalb der Plattform oder lässt ihn für Funktionsrollen per E-Mail an eine von ihm eingegebene Adresse senden; die Adresse wird nach Einlösung oder Ablauf gelöscht. Die Plattform speichert keine Zuordnung zwischen Aussteller-Wissen und Anzeigename. Die einzige Ausnahme von „keine gespeicherte Zuordnung von Klarname zu Anzeigename“ ist die freiwillige Klarname-Freigabe je Veranstaltungsanmeldung, die nach 30 Tagen gelöscht wird (A-084).

| Rolle | Aussteller | Anzeigename | Pflichtangaben beim Einlösen |
|---|---|---|---|
| Gesundheitsbotschafter | Tenant-Admin, Programm-Manager | frei | keine; der Code trägt die Gruppen, für die der Botschafter zuständig ist |
| Redakteur, Einsichtsrolle | Tenant-Admin | Klarname | Klarname, E-Mail oder externer Anbieter |
| Programm-Manager | Tenant-Admin | Klarname | Klarname, E-Mail oder externer Anbieter, Passkey oder externer Anbieter |
| Tenant-Admin | Operator-Admin, Partner-Admin für eigene Tenants, bestehender Tenant-Admin | Klarname | Klarname, E-Mail oder externer Anbieter, Passkey oder externer Anbieter |
| Partner-Admin | Operator-Admin | Klarname | wie Tenant-Admin |
| Operator-Admin, Operator-Support | Operator-Admin | Klarname | wie Tenant-Admin |

Rollencodes sind einmalig, 14 Tage gültig, widerrufbar und an genau eine Rolle und eine Organisation gebunden; Botschaftercodes zusätzlich an Gruppen. Das Einlösen wird protokolliert. Eine bestehende Person kann einen Rollencode einlösen; die Rolle wird ihrer Person hinzugefügt. Für Funktionsrollen wird beim Einlösen der Anzeigename auf den Klarnamen gesetzt.

## 3. Anmeldewege (A-015)

### 3.1 Wege für Mitglieder

| Weg | Ablauf | Gespeichert |
|---|---|---|
| Externer Anbieter | OIDC Authorization Code Flow mit PKCE; Callback und Codeaustausch im Backend | Hash aus Issuer und Subject, Verweis auf die Person |
| Magic-Link | einmaliger Link per E-Mail, 15 Minuten gültig, nicht an das anfordernde Gerät gebunden | ausdrücklich hinterlegte E-Mail |
| Passkey | WebAuthn; mehrere Passkeys je Person erlaubt | Credential-ID, öffentlicher Schlüssel, Gerätename |
| Wiederherstellungscode | 12 Zeichen in Dreiergruppen, einmalig; nach Verwendung wird sofort ein neuer erzeugt und angezeigt | Hash |
| Kiosk | Kiosk-Kennung und PIN ausschließlich in einer Kiosk-Gerätesitzung | siehe Abschnitt 6 |

Ein erfolgreicher Login über Wiederherstellungscode führt unmittelbar zur Einrichtung eines neuen Passkeys oder einer E-Mail.

### 3.2 Externe Anbieter

- **Plattformweit registriert:** Microsoft (Arbeits-, Schul- und persönliche Konten) und Google. Der Operator pflegt die Registrierungen; Redirect-Ziele sind auf die Plattform-Origin festgelegt.
- **Tenant-eigene Anbieter:** Der Tenant-Admin konfiguriert eigene OpenID-Connect-Anbieter im Self-Service über Issuer, Client-ID und Client-Secret. Das Backend validiert den Anbieter über dessen Discovery-Dokument, bevor er aktiv wird. Ein Tenant darf Microsoft auf seinen eigenen Entra-Mandanten einschränken.
- **Zuordnung beim Login:** Der Anbieter ist beim Login noch nicht einem Tenant zugeordnet. Der plattformweite Identitätsindex ordnet den Hash aus Issuer und Subject genau einer Person zu. Fehlt eine Zuordnung, führt der Weg in den Beitritt mit Code oder in die Verknüpfung mit einer bestehenden, gerade angemeldeten Person.
- **Anbieter ohne OpenID Connect** werden nicht generisch unterstützt.

### 3.3 Privilegierte Rollen

Tenant-Admin, Partner-Admin, Programm-Manager und alle Operator-Rollen melden sich mit Passkey oder externem Anbieter an. Magic-Link allein ist für diese Rollen kein Anmeldeweg. Redakteur und Einsichtsrolle dürfen Magic-Link verwenden.

Sensible Aktionen verlangen eine frische Anmeldung, die nicht älter als 15 Minuten ist: Ausstellen von Rollencodes, Konfiguration externer Anbieter, Registrierung und Widerruf von Kiosk-Geräten, Modulbuchung, Änderungen an Marke und Rechnungsdaten.

### 3.4 Festlegung der Wege durch den Tenant

Der Tenant bestimmt, welche Anmeldewege seinen Mitgliedern offenstehen.

- Der Tenant-Admin aktiviert oder deaktiviert je Weg: externe Anbieter einzeln, Magic-Link, Passkey mit Wiederherstellungscode, Kiosk. Mindestens ein Weg außer dem Kiosk bleibt aktiv.
- Voreinstellung bei Anlage eines Tenants: alle Wege aktiv.
- Der Tenant kann für Mitglieder einen oder mehrere externe Anbieter erzwingen. Dann ist der Beitritt nur über diesen Anbieter möglich; der Beitritt am Kiosk entfällt. Der Kiosk bleibt für Personen nutzbar, die über den Anbieter beigetreten sind und in der App eine PIN gesetzt haben.
- Für privilegierte Rollen darf der Tenant zusätzlich einen externen Anbieter erzwingen; Passkey bleibt für diese Rollen immer als Weg erlaubt, damit ein Anbieterausfall die Verwaltung nicht aussperrt.
- Die Verwaltung zeigt bei jeder Änderung, wie viele Personen danach keinen aktiven Weg mehr hätten, ohne Personen zu nennen.
- Wird ein Weg deaktiviert, den Personen als einzigen nutzen, bleibt er für diese Personen 30 Tage nutzbar; die App fordert in dieser Zeit zur Einrichtung eines erlaubten Weges auf. Danach ist der Weg gesperrt. Betroffene ohne eingerichteten erlaubten Weg treten neu bei, sobald ihnen ein erlaubter Weg zur Verfügung steht.
- Änderungen an den Wegen sind sensible Aktionen gemäß 3.3 und werden protokolliert; die Einsichtsrolle sieht die aktive Konfiguration.

## 4. Kontomodell und Wiederherstellung (A-016)

- **Person** ist tenantbezogen und trägt Anzeigename, Rollen, Gruppen, Sichtbarkeit und Kiosk-Kennung.
- **Login-Identitäten** hängen an der Person: beliebig viele Passkeys, höchstens eine E-Mail, beliebig viele Anbieterverknüpfungen. Jede Identität ist plattformweit höchstens einer aktiven Person zugeordnet.
- **Verknüpfen** eines weiteren Weges ist nur aus einer bestehenden Sitzung heraus möglich und erfordert das vollständige Durchlaufen des neuen Weges. Es gibt keine automatische Verknüpfung aufgrund gleicher E-Mail oder gleichen Namens.
- **Trennen** eines Weges ist in der Profilansicht möglich, solange mindestens ein Weg verbleibt. Für Personen ohne E-Mail und ohne Anbieter bleibt der Wiederherstellungscode immer aktiv.
- **Geräteverlust:** Anmeldung auf einem anderen Gerät über einen beliebigen Weg, dann „Abmeldung aus allen Sitzungen“ und Entfernen des verlorenen Passkeys.
- **Verlust aller Wege:** Es gibt keine Wiederherstellung durch Tenant-Admin oder Operator, weil sie die Person nicht identifizieren können und keine Zuordnung erzeugen sollen. Die Person tritt mit einem Beitrittscode neu bei und erhält eine neue Person. Die verwaiste Person wird nach 24 Monaten ohne Anmeldung und Handlung automatisch ausgetreten (A-024); nach einem Deaktivierungssignal aus dem Verzeichnis gilt die kürzere Frist von 30 Tagen (A-105).
- **Claims** externer Anbieter werden nach dem Anmeldevorgang verworfen. Der Anzeigename wird nie aus Claims befüllt; die E-Mail wird nur gespeichert, wenn die Person sie ausdrücklich für den Magic-Link hinterlegt.

## 5. Sitzungen (A-017)

| Sitzung | Verlängerung bei Aktivität | Absolutes Ende | Bemerkung |
|---|---|---|---|
| Mitglied, persönliches Gerät | 30 Tage nach letzter Aktivität | 180 Tage | lang genug für Offline-Phasen und Synchronisierung |
| Privilegierte Rolle | 8 Stunden nach letzter Aktivität | 24 Stunden | frische Anmeldung für sensible Aktionen |
| Kiosk-Gerätesitzung | Gerätegeheimnis rotiert alle 30 Tage automatisch | keines, nur Widerruf | gewährt keine Personenrechte |
| Kiosk-Personensitzung | 60 Sekunden ohne Eingabe, je Tenant einstellbar zwischen 30 und 120 Sekunden | 10 Minuten | endet spätestens mit der Gerätesitzung |

Sitzungen liegen in PostgreSQL. Widerruf wirkt beim nächsten Request. „Abmeldung aus allen Sitzungen“ beendet alle Sitzungen der Person einschließlich Kiosk-Personensitzungen. Rollenentzug, Austritt und Widerruf eines Anbieters beenden betroffene Sitzungen sofort. Bei mehreren API-Instanzen gelten dieselben Sitzungen über den gemeinsamen Speicher und die gemeinsamen Data-Protection-Schlüssel.

Ein Wechsel der Sichtbarkeitsstufe, ein Rollencode oder ein Markenwechsel ändern die Sitzung nicht.

## 6. Kiosk (A-018)

### 6.1 Geräteregistrierung

1. Der Tenant-Admin legt in der Verwaltung ein Kiosk-Gerät mit Namen und optionaler Standardgruppe an. Die Verwaltung zeigt einen einmaligen Registrierungscode, 15 Minuten gültig.
2. Am Gerät wird `https://<Origin>/kiosk` geöffnet und der Code eingegeben. Das Backend stellt das Gerätegeheimnis als Secure-/HttpOnly-Cookie aus; die Gerätesitzung beginnt.
3. Das Gerätegeheimnis rotiert alle 30 Tage automatisch. Der Tenant-Admin kann jedes Gerät sofort widerrufen; die Verwaltung zeigt je Gerät letzte Aktivität und Anzahl der Anmeldungen, nie Personen.

Ein registriertes Gerät zeigt ohne Personensitzung nur den Kollektivstand des Tenants, die Beitrittsmöglichkeit und die Anmeldemaske.

### 6.2 Persönliche Anmeldung

- Jede Person erhält beim Beitritt eine **Kiosk-Kennung**: sechs Ziffern, zufällig, je Tenant eindeutig. Sie wird beim Beitritt angezeigt und ist jederzeit in der eigenen App sichtbar, dort auch als druckbarer QR-Code.
- Die **PIN** hat vier Ziffern, wird von der Person gewählt und in der App oder am Kiosk nach Anmeldung geändert. Triviale Folgen und Wiederholungen werden abgelehnt.
- Anmeldung am Kiosk: Kennung eingeben oder persönlichen QR-Code scannen, dann PIN. Es gibt keine Namensliste und keine Namenssuche am Gerät.
- Drosselung: nach fünf Fehlversuchen für eine Kennung 15 Minuten Sperre dieser Kennung am Gerät; nach 20 Fehlversuchen an einem Gerät innerhalb von 15 Minuten 15 Minuten Sperre des Geräts. Fehlversuche werden im Sicherheitsprotokoll pseudonymisiert erfasst (12 Monate, A-024).
- Vergessene PIN: Änderung in der eigenen App. Personen ohne eigenes Gerät setzen die PIN am Kiosk mit ihrem Wiederherstellungscode neu, der beim Kiosk-Beitritt zusammen mit der Kennung angezeigt wird.

### 6.3 Was am Kiosk möglich ist

Check-in, Beitrag zu laufenden Challenges, Kollektivstand, Anmeldung zu Veranstaltungen (A-084), PIN-Änderung, Übertragung auf ein eigenes Gerät. Nicht möglich: Historie, Sichtbarkeitseinstellungen, Feed schreiben, Profiländerungen, Verwaltung. Alle Beiträge folgen A-005 mit serverseitig reservierter Vorgangskennung.

### 6.4 Übertragung auf ein eigenes Gerät

Aus der angemeldeten Kiosk-Personensitzung zeigt „Auf mein Handy übertragen“ einen QR-Code mit einem einmaligen Link, fünf Minuten gültig. Das Öffnen auf dem eigenen Gerät eröffnet dort eine Mitgliedssitzung und führt sofort zur Einrichtung von Passkey oder E-Mail. Kennung und PIN sind außerhalb einer Kiosk-Gerätesitzung kein Anmeldeweg.

### 6.5 Sitzungsende

Der Countdown ist sichtbar; jede Eingabe setzt ihn zurück. Beim Ende der Personensitzung werden Sitzungsspeicher und Anzeige geleert; unbestätigte Eingaben gehen verloren, worauf die Oberfläche vor dem Ablauf hinweist. Der Widerruf des Geräts beendet alle Personensitzungen auf diesem Gerät.

## 7. Offline-Freigabe (A-019)

- Die Offline-Freigabe entsteht mit jeder erfolgreichen Serververbindung aus einer Mitgliedssitzung und gilt **7 Tage** ab dem letzten Serverkontakt. Sie ist kein Token und enthält kein Geheimnis; sie ist ein lokal gespeicherter Ablaufzeitpunkt, den die Oberfläche auswertet.
- Nach Ablauf sperrt die Oberfläche den persönlichen Bereich bis zur nächsten Online-Anmeldung. Wartende Beiträge bleiben erhalten und werden nach erneuter Anmeldung derselben Person im selben Tenant synchronisiert.
- Ist die Serversitzung abgelaufen, aber die Offline-Freigabe noch gültig, kann die Person offline weiter erfassen; die Synchronisierung verlangt eine erneute Online-Anmeldung.
- Geräteverlust: „Abmeldung aus allen Sitzungen“ von einem anderen Gerät beendet die Serversitzung; die Offline-Freigabe auf dem verlorenen Gerät läuft spätestens nach 7 Tagen aus. Lokale Daten auf dem verlorenen Gerät sind bis dahin für jemanden mit Zugriff auf das Browserprofil lesbar; das ist die in A-008 benannte Grenze.
- Der Kiosk besitzt keine Offline-Freigabe.

## 8. Austritt (A-019)

Der Austritt ist ein Button in der Profilansicht mit einer Bestätigung. Aus Sicht des Zugangs bewirkt er sofort: Ende aller Sitzungen, Löschung aller Login-Identitäten, Wiederherstellungscodes, Passkeys, der Kiosk-Kennung und der PIN, Entfernen der Person aus dem plattformweiten Identitätsindex. Eine spätere Anmeldung mit derselben externen Identität hat keine Zuordnung mehr und führt in den Beitritt; ein neuer Beitritt erzeugt eine neue Person ohne Verbindung zur alten. Was mit Beiträgen, Aggregaten und Protokollen geschieht, steht in [Datenschutz und Nachweis](datenschutz-und-nachweis.md), Abschnitt 5.2.

Rollenentzug ist kein Austritt: Die Person bleibt Mitglied, verliert die Rolle und ihre laufenden privilegierten Sitzungen.

## 9. Schnittmengen dieses Themas

| Schnittmenge | Festlegung |
|---|---|
| Identität ↔ Organisation | Der Beitritt erzeugt Person, Mitgliedschaft und Gruppen in einer Transaktion; Rollen liegen bei Organisation und werden über Rollencodes aus Identität vergeben |
| Identität ↔ Datenschutz | Sichtbarkeitswahl im Beitritt gehört Datenschutz; Rollencode-Einlösung, Anbieterkonfiguration, Geräteregistrierung und -widerruf und Austritt gehen ins Prüfprotokoll; Anmeldungen und Fehlversuche ins pseudonymisierte Sicherheitsprotokoll |
| Identität ↔ Metering | Beitritte (`member.joined`) und registrierte Kiosk-Geräte (`kiosk.device_month`) als Metering-Ereignisse ohne Personenbezug |
| Identität ↔ Challenges | Kiosk-Beiträge und Offline-Beiträge verwenden die Idempotenzregime aus A-009; die Person bleibt über alle Anmeldewege dieselbe |
| Identität ↔ Benachrichtigungen | Magic-Links und Rollencode-Einladungen per E-Mail werden über Benachrichtigungen versendet; Inhalte enthalten keine Personendaten außer der Anrede mit Anzeigenamen und dem Link |
| Identität ↔ Marke | Beitritts- und Kiosk-Oberflächen verwenden den Tokensatz des Tenants; die Tenant-Vorschau beim Beitritt zeigt Name und Logo |

## 10. Nachweise

1. Beitritt ohne E-Mail mit Passkey und Wiederherstellungscode; Login auf zweitem Gerät mit dem Code; neuer Code wird erzeugt.
2. Beitritt am Kiosk mit Kennung und PIN; Übertragung per QR auf ein eigenes Gerät; danach Passkey-Einrichtung.
3. Login mit Microsoft, Google und tenant-eigenem OIDC-Anbieter; im Datenbestand nur Hash aus Issuer und Subject; kein Name, keine E-Mail.
4. Dieselbe externe Identität kann keiner zweiten aktiven Person zugeordnet werden.
5. Rollencode für Programm-Manager: Klarname gesetzt, Magic-Link-Login für Tenant-Admin abgelehnt, Passkey-Login angenommen; sensible Aktion ohne frische Anmeldung abgelehnt.
6. Kiosk: fünf Fehlversuche sperren die Kennung, 20 Fehlversuche das Gerät; Widerruf des Geräts beendet Personensitzungen; keine Namensliste erreichbar.
7. Sitzungslaufzeiten: Verlängerung, absolutes Ende, „Abmeldung aus allen Sitzungen“ über zwei API-Instanzen.
8. Offline-Freigabe läuft nach 7 Tagen aus; wartende Beiträge werden nach erneuter Anmeldung synchronisiert, nach Austritt nicht.
9. Austritt entfernt alle Identitäten; erneuter Login mit derselben externen Identität führt in den Beitritt.
