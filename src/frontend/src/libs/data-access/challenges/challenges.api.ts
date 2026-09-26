// Datenzugriff der Domäne Challenges (K11, K13): ein gemeinsamer Serverdatenzugriff, keine zweite fachliche Wahrheit.
// Prozent, Kollektivstand und Ablehnungen kommen vom Backend; der Client bildet Formulareingaben ausdrücklich auf das DTO ab.
import { HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { catchError, map, Observable, shareReplay, throwError } from 'rxjs';
import { ApiClient, toProblem, type ApiProblem } from '../../api-client/api-client';
import type { components } from '../../api-client/generated/api';
import { uuidv7 } from '../uuidv7';

export type ChallengeCard = components['schemas']['ChallengeCardResponse'];
export type ContributionResponse = components['schemas']['ContributionResponse'];
export type ContributionRequest = components['schemas']['ContributionRequest'];
export type OperationResponse = components['schemas']['OperationResponse'];
export type ChallengeDraftRequest = components['schemas']['ChallengeDraftRequest'];
export type OwnContribution = components['schemas']['OwnContributionResponse'];
export type ReversalOutcome = components['schemas']['ReversalResponse']['outcome'];

/** Eingabe des Formulars „Beitrag nachtragen“ (K10: Typed Reactive Forms besitzen den Zustand, die Abbildung ist ausdrücklich). */
export interface ContributionInput {
  challengeId: string;
  /** Kalendertage zurück: 0 heute, 1 gestern, 2 vorgestern, 3 vor drei Tagen (Challenges 3: bis drei Tage rückwirkend). */
  daysAgo: number;
  /** Häkchen tragen genau 1; Zahlen als kulturunabhängiger Dezimalstring. */
  value: string;
  /** Persönliche Notiz: bleibt lokal, ist nicht Teil des Vertrags und wird nicht übertragen. */
  note?: string;
}

/** Stabile Fehlerkennung für die Oberfläche: Textschlüssel im Katalog (fehler.*). */
export interface ContributionError {
  key: string;
  status: number;
}

@Injectable({ providedIn: 'root' })
export class ChallengesApi {
  private readonly api = inject(ApiClient);
  private running$?: Observable<ChallengeCard[]>;
  /** Letzte Version der laufenden Challenges für Verbraucher, die nur lesen (Kontextspalte, Startseite). */
  readonly running = signal<ChallengeCard[] | null>(null);

  /** Laufende Challenges mit Kartendaten; mehrere Verbraucher teilen genau einen Request (K11). */
  listRunning(): Observable<ChallengeCard[]> {
    this.running$ ??= this.api.get('/api/challenges').pipe(
      map((cards) => {
        this.running.set(cards);
        return cards;
      }),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.running$;
  }

  /** Nach einer Änderung neu laden. */
  refresh(): Observable<ChallengeCard[]> {
    this.running$ = undefined;
    return this.listRunning();
  }

  /** Abbildung Formular → DTO; der Idempotenzschlüssel wird einmal erzeugt und bei Wiederholungen unverändert übergeben. */
  toRequest(input: ContributionInput, now: Date, idempotencyKey: string = uuidv7(now.getTime())): ContributionRequest {
    const recorded = new Date(now.getTime());
    recorded.setDate(recorded.getDate() - input.daysAgo);
    return {
      value: input.value,
      recordedAt: toOffsetIso(recorded),
      channel: 'mobile',
      idempotencyKey,
      operationId: null,
    };
  }

  /** Kiosk (A-005, Zugang 6.3): die Vorgangskennung wird serverseitig in der Kiosk-Personensitzung reserviert. */
  reserveOperation(challengeId: string): Observable<OperationResponse> {
    return this.api.post('/api/challenges/{challengeId}/contribution-operations', null, { path: { challengeId } }).pipe(
      catchError((error: unknown) => throwError(() => toContributionError(error))),
    );
  }

  /** Beitrag am Kiosk: Häkchen heute, Kanal kiosk, reservierte Vorgangskennung statt Idempotenzschlüssel des Clients. */
  toKioskRequest(operationId: string, now: Date): ContributionRequest {
    return { value: '1', recordedAt: toOffsetIso(now), channel: 'kiosk', idempotencyKey: null, operationId };
  }

  /** Verwaltung (Challenges 4.2): alle Challenges einschließlich Entwürfen und Beendeten; Programm-Manager, Tenant-Admin, Einsichtsrolle. */
  listAll(): Observable<ChallengeCard[]> {
    return this.api.get('/api/challenges/manage');
  }

  /** Kickoff-Challenge (Challenges 4.3): vorbelegter Entwurf. */
  kickoff(): Observable<ChallengeCard> {
    return this.api.post('/api/challenges/kickoff', null);
  }

  /** Wizard (Challenges 4.1): Entwurf aus den Achsen; Sammelziel und ganze Firma sind gesetzt. */
  createDraft(draft: ChallengeDraftRequest): Observable<ChallengeCard> {
    return this.api.post('/api/challenges', draft);
  }

  /** Vorschau ist Pflicht vor „Planen“ (Challenges 4.1). */
  preview(challengeId: string): Observable<ChallengeCard> {
    return this.api.post('/api/challenges/{challengeId}/preview', null, { path: { challengeId } });
  }

  plan(challengeId: string): Observable<void> {
    return this.api.post('/api/challenges/{challengeId}/plan', null, { path: { challengeId } }).pipe(map(() => undefined));
  }

  /** Vorzeitiges Ende mit Begründung; nur Programm-Manager (Challenges 4.1). */
  endEarly(challengeId: string, reason: string): Observable<void> {
    return this.api.post('/api/challenges/{challengeId}/end', { reason }, { path: { challengeId } }).pipe(map(() => undefined));
  }

  updateTexts(challengeId: string, title: string, description: string | null): Observable<void> {
    return this.api.put('/api/challenges/{challengeId}/texts', { title, description }, { path: { challengeId } }).pipe(map(() => undefined));
  }

  /** Eigene Beiträge und Korrektur als Gegenbuchung (Challenges 3), nur auf dem eigenen Gerät. */
  mine(challengeId: string): Observable<OwnContribution[]> {
    return this.api.get('/api/challenges/{challengeId}/contributions/mine', { path: { challengeId } });
  }

  reverse(challengeId: string, contributionId: string): Observable<ReversalOutcome> {
    return this.api.post('/api/challenges/{challengeId}/contributions/{contributionId}/reverse', null, { path: { challengeId, contributionId } }).pipe(map((r) => r.outcome));
  }

  submit(challengeId: string, request: ContributionRequest): Observable<ContributionResponse> {
    return this.api.post('/api/challenges/{challengeId}/contributions', request, { path: { challengeId } }).pipe(
      catchError((error: unknown) => throwError(() => toContributionError(error))),
    );
  }
}

export function toContributionError(error: unknown): ContributionError {
  if (error instanceof HttpErrorResponse) {
    const problem: ApiProblem = toProblem(error.status, error.error);
    if (error.status === 422 && problem.detail) {
      return { key: `fehler.${problem.detail}`, status: 422 };
    }
    if (error.status === 409) {
      return { key: 'fehler.Konflikt', status: 409 };
    }
    if (error.status === 0) {
      return { key: 'fehler.Verbindung', status: 0 };
    }
    return { key: 'fehler.Verbindung', status: error.status };
  }
  return { key: 'fehler.Verbindung', status: 0 };
}

/** Zeitpunkt mit explizitem Offset (K13), nie stilles UTC. */
export function toOffsetIso(date: Date): string {
  const pad = (n: number) => String(Math.abs(n)).padStart(2, '0');
  const offset = -date.getTimezoneOffset();
  const sign = offset >= 0 ? '+' : '-';
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}${sign}${pad(Math.floor(Math.abs(offset) / 60))}:${pad(Math.abs(offset) % 60)}`;
}
