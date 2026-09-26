// Datenzugriff Datenschutz (Datenschutz 3.1, 6.4; K11): Sichtbarkeitsstufe lesen und ändern (wirkt sofort und rückwirkend),
// Zustimmungsprotokoll, Selbstexport als Datei (Link auf den Endpunkt, der Browser lädt mit der Sitzung).
import { inject, Injectable, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { ApiClient } from '../../api-client/api-client';
import type { components } from '../../api-client/generated/api';

export type VisibilityLevel = components['schemas']['VisibilityLevelDto'];
export type VisibilityInfo = components['schemas']['VisibilityResponse'];
export type Consent = components['schemas']['ConsentResponse'];

export const EXPORT_URL = '/api/me/export';

@Injectable({ providedIn: 'root' })
export class PrivacyApi {
  private readonly api = inject(ApiClient);

  readonly visibility = signal<VisibilityInfo | null>(null);

  loadVisibility(): Observable<VisibilityInfo> {
    return this.api.get('/api/me/visibility').pipe(tap((v) => this.visibility.set(v)));
  }

  setVisibility(level: VisibilityLevel): Observable<VisibilityInfo> {
    return this.api.put('/api/me/visibility', { level }).pipe(tap((v) => this.visibility.set(v)));
  }

  consents(): Observable<Consent[]> {
    return this.api.get('/api/me/consents');
  }
}
