// Datenzugriff der Organisation (Organisation 2.1, 3; K11): Dimensionen mit Gruppen, eigene Gruppenwahl, Tenant-Stammdaten.
import { inject, Injectable, signal } from '@angular/core';
import { map, Observable, tap } from 'rxjs';
import { ApiClient } from '../../api-client/api-client';
import type { components } from '../../api-client/generated/api';

export type Dimension = components['schemas']['DimensionResponse'];
export type GroupChoice = components['schemas']['GroupChoiceResponse'];
export type TenantInfo = components['schemas']['TenantResponse'];

@Injectable({ providedIn: 'root' })
export class OrganisationApi {
  private readonly api = inject(ApiClient);

  readonly dimensions = signal<Dimension[] | null>(null);
  readonly choices = signal<GroupChoice[] | null>(null);

  loadDimensions(): Observable<Dimension[]> {
    return this.api.get('/api/organisation/dimensions').pipe(tap((d) => this.dimensions.set(d)));
  }

  loadChoices(): Observable<GroupChoice[]> {
    return this.api.get('/api/me/groups').pipe(tap((c) => this.choices.set(c)));
  }

  /** Eigene Wahl je Dimension, jederzeit änderbar (Organisation 2.1); `null` hebt die Wahl auf. */
  choose(dimensionId: string, groupId: string | null): Observable<void> {
    return this.api.put('/api/me/groups', { dimensionId, groupId }).pipe(map(() => undefined));
  }

  tenant(): Observable<TenantInfo> {
    return this.api.get('/api/organisation/tenant');
  }
}
