// Sitzungszustand des Clients (Zugang 5, A-007, K11): die Sitzung liegt serverseitig in einem HttpOnly-Cookie; der Client kennt
// nur, was GET /api/auth/session sagt, und merkt sich, ob der Server zuletzt mit 401 geantwortet hat. Kein Token, kein Geheimnis.
import { computed, Injectable, signal } from '@angular/core';
import type { components } from '../../api-client/generated/api';

export type SessionInfo = components['schemas']['SessionResponse'];
export type SessionKind = components['schemas']['SessionKindDto'];

@Injectable({ providedIn: 'root' })
export class SessionStore {
  /** `undefined`: noch nicht geprüft; `null`: keine Sitzung. */
  readonly session = signal<SessionInfo | null | undefined>(undefined);
  /** Der Server hat einen Request der Mitglieder-App mit 401 beantwortet: die Sitzung ist beendet (K11: der Client behandelt 401). */
  readonly ended = signal(false);

  readonly kind = computed<SessionKind | null>(() => this.session()?.kind ?? null);
  readonly isPerson = computed(() => this.kind() === 'member' || this.kind() === 'privileged');
  readonly isKioskDevice = computed(() => this.kind() === 'kiosk_device');

  set(session: SessionInfo | null): void {
    this.session.set(session);
    if (session) {
      this.ended.set(false);
    }
  }

  markEnded(): void {
    this.session.set(null);
    this.ended.set(true);
  }
}
