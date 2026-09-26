// Datenzugriff der Domäne Entitlements (Entitlements 2 bis 5; K11, K12): Navigation der Mitglieder-App aus aktiven Modulen,
// Katalog und Buchungen der Verwaltung. Ob ein Modul nutzbar ist, entscheidet ausschließlich die API (K14); die Oberfläche zeigt
// nie Schlösser oder Hinweise auf inaktive Module (A-064).
import { inject, Injectable, signal } from '@angular/core';
import { Observable, shareReplay, tap } from 'rxjs';
import { ApiClient } from '../../api-client/api-client';
import type { components } from '../../api-client/generated/api';

export type Navigation = components['schemas']['NavigationResponse'];
export type ModuleStatus = components['schemas']['ModuleStatusResponse'];
export type Booking = components['schemas']['BookingResponse'];
export type Trial = components['schemas']['TrialResponse'];
export type Cancellation = components['schemas']['CancellationResponse'];
export type EntitlementHistoryEntry = components['schemas']['EntitlementHistoryResponse'];

/** Bereich der Navigation mit Pfad, Symbol und Textschlüssel; nur Bereiche aktiver Module (Entitlements 2.2). */
export interface NavArea {
  path: string;
  icon: string;
  labelKey: string;
}

const AREAS: Record<string, NavArea> = {
  start: { path: 'start', icon: 'home', labelKey: 'nav.start' },
  challenges: { path: 'challenges', icon: 'flag', labelKey: 'nav.challenges' },
  entdecken: { path: 'entdecken', icon: 'explore', labelKey: 'nav.entdecken' },
  firma: { path: 'firma', icon: 'apartment', labelKey: 'nav.firma' },
  ich: { path: 'ich', icon: 'person', labelKey: 'nav.ich' },
};

/** Bereiche laut API in Navigationseinträge; unbekannte Bereiche werden nicht erfunden. */
export function toNavAreas(areas: readonly string[]): NavArea[] {
  return areas.map((a) => AREAS[a]).filter((a): a is NavArea => a !== undefined);
}

@Injectable({ providedIn: 'root' })
export class EntitlementsApi {
  private readonly api = inject(ApiClient);
  private navigation$?: Observable<Navigation>;
  readonly navigation = signal<Navigation | null>(null);

  /** Navigation aus aktiven Entitlements; ein Request je Laden der App (Entitlements 5: Buchung wirkt beim nächsten Laden). */
  loadNavigation(): Observable<Navigation> {
    this.navigation$ ??= this.api.get('/api/entitlements/me').pipe(
      tap((n) => this.navigation.set(n)),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.navigation$;
  }

  modules(): Observable<ModuleStatus[]> {
    return this.api.get('/api/entitlements/modules');
  }

  book(module: string): Observable<Booking> {
    return this.api.post('/api/entitlements/modules/{module}/book', null, { path: { module } });
  }

  bookBundle(modules: string[]): Observable<Booking> {
    return this.api.post('/api/entitlements/bundles/book', { modules });
  }

  startTrial(module: string): Observable<Trial> {
    return this.api.post('/api/entitlements/modules/{module}/trial', null, { path: { module } });
  }

  cancel(module: string): Observable<Cancellation> {
    return this.api.post('/api/entitlements/modules/{module}/cancel', null, { path: { module } });
  }

  revokeCancellation(module: string): Observable<Cancellation> {
    return this.api.post('/api/entitlements/modules/{module}/revoke-cancellation', null, { path: { module } });
  }

  history(): Observable<EntitlementHistoryEntry[]> {
    return this.api.get('/api/entitlements/history');
  }
}
