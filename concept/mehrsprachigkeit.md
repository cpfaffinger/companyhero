# CompanyHero – Mehrsprachigkeit

**Stand:** 21.09.2026  
**Status:** Beschlossen gemäß A-104 im [Entscheidungsregister](architektur-entscheidungen.md). Keine Implementierung.  
**Art:** Modul M11 als Querschnitt über Oberfläche, Textkatalog, Inhalte, Benachrichtigungen und Aushang.  
**Bezug:** [Marke und Theme](marke-und-theme.md), [Feed und Inhalte](feed-und-inhalte.md), [Entitlements](entitlements.md).

In Produktion, Reinigung, Pflege und Bau entscheidet die Sprache darüber, ob jemand teilnehmen kann. Deutsch ist Standard; jede weitere Sprache ist das Modul Mehrsprachigkeit.

## 1. Sprachen

| Ebene | Festlegung |
|---|---|
| Standardsprache | je Tenant, Voreinstellung de-AT (A-033); ohne M11 die einzige Sprache |
| Oberflächensprachen | vom Operator vollständig übersetzt oder nicht verfügbar; Start: Deutsch, Englisch, Türkisch, Bosnisch/Kroatisch/Serbisch (eine Variante, lateinische Schrift), Rumänisch, Ungarisch, Slowakisch, Polnisch |
| Aktive Sprachen je Tenant | Auswahl aus den Oberflächensprachen, höchstens fünf (A-068); jede aktive Zusatzsprache ist `language.active_month` |
| Sprache der Person | Wahl im Beitritt und im Profil aus den aktiven Sprachen des Tenants; gilt für Oberfläche, Texte, Benachrichtigungen, Inhalte |
| Schrift und Formate | alle Startsprachen in lateinischer Schrift; Zahlen-, Datums- und Zeitformat je Sprache, Zeitzone je Tenant |

## 2. Was übersetzt wird und von wem

| Bestandteil | Quelle | Fallback |
|---|---|---|
| Oberfläche (Angular, Material) | Operator, vollständig je Sprache | keiner; unvollständige Sprachen sind nicht aktivierbar |
| Textkatalog mit drei Tonalitäten | Operator (A-080) | Standardsprache des Tenants, dann Deutsch, mit Kennzeichnung in der Operator-Prüfliste |
| Bezeichnungen, Willkommenstext | Tenant je Sprache (A-077) | Standardsprache des Tenants |
| Plattforminhalte, Vorlagen, Abzeichentexte | Operator redaktionell je Sprache | Standardsprache des Tenants, dann Ursprungssprache, gekennzeichnet (A-052) |
| Tenant-Inhalte, Ankündigungen, Challenges | Tenant-Redaktion je Sprache; optional KI-Authoring mit eigenen Zugangsdaten (A-057) | Standardsprache des Tenants, gekennzeichnet |
| Untertitel und Transkripte | wie der Inhalt | Ursprungssprache |
| Benachrichtigungen, Aushang, E-Mails | Textkatalog in Sprache der Person; Aushang in bis zu drei vom Programm-Manager gewählten Sprachen auf einem Blatt | Standardsprache |
| Rechtstexte | Operator je Sprache (A-020) | Deutsch als verbindliche Fassung, Übersetzung als Lesehilfe gekennzeichnet |
| Kiosk | Sprachwahl am Gerät je Sitzung aus den aktiven Sprachen; Voreinstellung Standardsprache | – |

## 3. Redaktion und Qualität

- Jeder Inhalt und jeder Textschlüssel zeigt in der Redaktion den Übersetzungsstand je aktiver Sprache; fehlende Sprachen erscheinen in der Prüfliste.
- Übersetzungen durchlaufen denselben Lebenszyklus wie Inhalte (A-053); ein Inhalt kann je Sprache getrennt veröffentlicht werden.
- Die Sperrliste der Begriffe (A-080) existiert je Sprache; die Sprachregeln gelten in jeder Sprache.
- Anzeigenamen, Gruppennamen und Firmenname werden nicht übersetzt.
- Suche arbeitet je Sprache mit Wortstammbildung der jeweiligen Sprache, soweit PostgreSQL sie bietet, sonst Ähnlichkeitssuche (A-055).

## 4. Verhalten bei Kündigung

Kündigt der Tenant M11, bleiben Personen in ihrer Sprache bis zum Monatsende; danach wechselt die Oberfläche auf die Standardsprache mit Hinweis. Übersetzte Tenant-Inhalte bleiben gespeichert und werden bei erneuter Buchung wieder angezeigt.

## 5. Schnittmengen

| Schnittmenge | Festlegung |
|---|---|
| Mehrsprachigkeit ↔ Marke | Textkatalog und Bezeichnungen je Sprache; Sperrliste je Sprache |
| Mehrsprachigkeit ↔ Feed und Inhalte | Textkarten je Sprache, Übersetzungsstand, getrennte Veröffentlichung, KI-Authoring |
| Mehrsprachigkeit ↔ Benachrichtigungen | Sprache der Person; Aushang mehrsprachig |
| Mehrsprachigkeit ↔ Zugang | Sprachwahl im Beitritt und am Kiosk |
| Mehrsprachigkeit ↔ Entitlements und Metering | M11, Grenzwert fünf, `language.active_month` |
| Mehrsprachigkeit ↔ Datenschutz | Rechtstexte je Sprache mit deutscher Fassung als verbindlich |

## 6. Nachweise

1. Tenant ohne M11 zeigt nur die Standardsprache; keine Sprachwahl im Beitritt.
2. Aktivierung einer sechsten Sprache wird abgelehnt; jede aktive Zusatzsprache erzeugt die Metrik.
3. Person mit Türkisch sieht Oberfläche, Textkatalog, Benachrichtigungen und Aushang auf Türkisch; fehlender Inhalt fällt gekennzeichnet auf die Standardsprache zurück.
4. Unvollständige Oberflächensprache ist für Tenants nicht wählbar.
5. Kiosk bietet Sprachwahl je Sitzung und fällt beim Sitzungsende auf die Standardsprache zurück.
