// Datenzugriff des Fortschritts (Fortschritt 6; K11): persönlicher Stand, Tagesziel, Check-in. Nur eigene Werte; keine Vergleiche.
import { inject, Injectable, signal } from '@angular/core';
import { map, Observable, tap } from 'rxjs';
import { ApiClient } from '../../api-client/api-client';
import type { components } from '../../api-client/generated/api';

export type PersonalProgress = components['schemas']['PersonalProgressResponse'];
export type Badge = components['schemas']['BadgeResponse'];
export type CheckInTile = components['schemas']['CheckInTileDto'];
export type CheckInOutcome = components['schemas']['CheckInResponse']['outcome'];

@Injectable({ providedIn: 'root' })
export class ProgressApi {
  private readonly api = inject(ApiClient);

  readonly mine = signal<PersonalProgress | null>(null);

  load(): Observable<PersonalProgress> {
    return this.api.get('/api/me/progress').pipe(tap((p) => this.mine.set(p)));
  }

  setDailyGoal(goal: number): Observable<void> {
    return this.api.put('/api/me/progress/daily-goal', { goal }).pipe(map(() => undefined));
  }

  /** Check-in (Fortschritt 6.1): drei Kacheln, Mehrfachauswahl; „already_today“ ist kein Fehler. */
  checkIn(tiles: CheckInTile[]): Observable<CheckInOutcome> {
    return this.api.post('/api/me/check-in', { tiles }).pipe(map((r) => r.outcome));
  }
}
