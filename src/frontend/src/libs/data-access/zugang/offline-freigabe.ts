// Offline-Freigabe (Zugang 7, A-019): kein Token und kein Geheimnis, sondern ein lokal gespeicherter Ablaufzeitpunkt sieben
// Tage nach dem letzten Serverkontakt aus einer Mitgliedssitzung. Nach Ablauf sperrt die Oberfläche den persönlichen Bereich
// bis zur nächsten Online-Anmeldung; wartende Beiträge bleiben erhalten und werden nach erneuter Anmeldung derselben Person im
// selben Tenant synchronisiert. Der Kiosk besitzt keine Offline-Freigabe; der Austritt beendet sie.
import { Injectable, signal } from '@angular/core';

export const FREIGABE_TAGE = 7;
const KEY = 'ch.zugang.freigabe';

export interface Freigabe {
  tenantId: string;
  personId: string;
  /** Letzter Serverkontakt als ISO-Zeitpunkt. */
  letzterKontakt: string;
}

/** Ablauf der Freigabe: sieben Tage nach dem letzten Serverkontakt. */
export function freigabeBis(letzterKontakt: Date): Date {
  return new Date(letzterKontakt.getTime() + FREIGABE_TAGE * 86_400_000);
}

export function gilt(freigabe: Freigabe | null, now: Date): boolean {
  return freigabe !== null && now.getTime() < freigabeBis(new Date(freigabe.letzterKontakt)).getTime();
}

/** Wartende Beiträge werden nur für dieselbe Person im selben Tenant synchronisiert. */
export function gehoertZu(freigabe: Freigabe | null, tenantId: string, personId: string): boolean {
  return freigabe !== null && freigabe.tenantId === tenantId && freigabe.personId === personId;
}

@Injectable({ providedIn: 'root' })
export class OfflineFreigabe {
  readonly aktuell = signal<Freigabe | null>(lesen());

  /** Jeder erfolgreiche Serverkontakt aus einer Mitgliedssitzung verlängert die Freigabe. */
  verlaengern(tenantId: string, personId: string, now: Date = new Date()): void {
    const freigabe: Freigabe = { tenantId, personId, letzterKontakt: now.toISOString() };
    this.aktuell.set(freigabe);
    schreiben(freigabe);
  }

  gilt(now: Date = new Date()): boolean {
    return gilt(this.aktuell(), now);
  }

  /** Austritt oder Abmeldung von allen Sitzungen auf diesem Gerät: die Freigabe endet sofort. */
  beenden(): void {
    this.aktuell.set(null);
    schreiben(null);
  }
}

function lesen(): Freigabe | null {
  try {
    const raw = globalThis.localStorage?.getItem(KEY);
    if (!raw) {
      return null;
    }
    const parsed = JSON.parse(raw) as Partial<Freigabe>;
    return typeof parsed.tenantId === 'string' && typeof parsed.personId === 'string' && typeof parsed.letzterKontakt === 'string' ? (parsed as Freigabe) : null;
  } catch {
    return null;
  }
}

function schreiben(freigabe: Freigabe | null): void {
  try {
    if (freigabe) {
      globalThis.localStorage?.setItem(KEY, JSON.stringify(freigabe));
    } else {
      globalThis.localStorage?.removeItem(KEY);
    }
  } catch {
    // Ohne Speicher (privater Modus) gibt es keine Offline-Freigabe; die Online-Sitzung bleibt unberührt.
  }
}
