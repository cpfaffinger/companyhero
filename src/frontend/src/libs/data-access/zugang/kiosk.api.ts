// Datenzugriff des Kiosks (Zugang 6, A-005, A-018): Gerätesitzung, persönliche Anmeldung mit Kennung und PIN, Countdown der
// Personensitzung, PIN-Neusetzung, Übertragung auf das eigene Gerät. Kennung und PIN gehen nur an /api/kiosk/login.
import { HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { catchError, map, Observable, tap, throwError } from 'rxjs';
import { ApiClient, toProblem } from '../../api-client/api-client';
import type { components } from '../../api-client/generated/api';
import { SessionStore } from './session.store';
import { toZugangFehler, type ZugangFehler } from './zugang.api';

export type KioskDevice = components['schemas']['KioskDeviceResponse'];
export type KioskLogin = components['schemas']['KioskLoginResponse'];
export type KioskJoinRequest = components['schemas']['KioskJoinRequest'];
export type JoinResponse = components['schemas']['JoinResponse'];
export type Transfer = components['schemas']['TransferResponse'];

/** Personensitzung am Kiosk aus Sicht des Geräts: sichtbarer Countdown, absolutes Ende (Zugang 6.5). */
export interface KioskPerson {
  personId: string;
  displayName: string;
  idleSeconds: number;
  idleUntil: Date;
  absoluteUntil: Date;
}

/** Verbleibende Sekunden bis zum Ende der Personensitzung: gleitendes oder absolutes Ende, was zuerst kommt; nie negativ. */
export function restsekunden(person: Pick<KioskPerson, 'idleUntil' | 'absoluteUntil'>, now: Date): number {
  const ende = Math.min(person.idleUntil.getTime(), person.absoluteUntil.getTime());
  return Math.max(0, Math.ceil((ende - now.getTime()) / 1000));
}

/** Jede bestätigte Eingabe setzt den gleitenden Countdown zurück, nie über das absolute Ende hinaus. */
export function verlaengert(person: KioskPerson, now: Date): KioskPerson {
  const next = now.getTime() + person.idleSeconds * 1000;
  return { ...person, idleUntil: new Date(Math.min(next, person.absoluteUntil.getTime())) };
}

@Injectable({ providedIn: 'root' })
export class KioskApi {
  private readonly api = inject(ApiClient);
  private readonly store = inject(SessionStore);

  /** Zustand des Geräts; ohne Gerätesitzung (401) ist das Gerät nicht registriert. */
  readonly device = signal<KioskDevice | null | undefined>(undefined);

  loadDevice(): Observable<KioskDevice | null> {
    return this.api.get('/api/kiosk/device').pipe(
      tap((device) => this.device.set(device)),
      catchError((error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 401) {
          this.device.set(null);
          return [null];
        }
        return throwError(() => error);
      }),
    );
  }

  /** Registrierung mit dem einmaligen Code der Verwaltung (Zugang 6.1); das Gerätegeheimnis kommt als Cookie. */
  register(code: string): Observable<KioskDevice> {
    return this.api.post('/api/kiosk/register', { code }).pipe(
      tap((device) => this.device.set(device)),
      catchError((error: unknown) => throwError(() => toZugangFehler(error))),
    );
  }

  login(kioskId: string, pin: string, now: Date = new Date()): Observable<KioskPerson> {
    return this.api.post('/api/kiosk/login', { kioskId, pin }).pipe(
      map(
        (login: KioskLogin): KioskPerson => ({
          personId: login.personId,
          displayName: login.displayName,
          idleSeconds: login.idleSeconds,
          // Der Client rechnet mit seiner eigenen Uhr, damit der Countdown auch bei Uhrenabweichung sichtbar zählt.
          idleUntil: new Date(now.getTime() + login.idleSeconds * 1000),
          absoluteUntil: new Date(now.getTime() + Math.max(0, new Date(login.absoluteUntil).getTime() - new Date(login.idleUntil).getTime()) + login.idleSeconds * 1000),
        }),
      ),
      catchError((error: unknown) => throwError(() => toKioskFehler(error))),
    );
  }

  /** Ende der Personensitzung; die Gerätesitzung bleibt. */
  logout(): Observable<void> {
    return this.api.post('/api/auth/logout', null).pipe(
      map(() => undefined),
      tap(() => this.store.set(null)),
    );
  }

  joinAtKiosk(code: string, request: KioskJoinRequest): Observable<JoinResponse> {
    return this.api.post('/api/join/{code}/kiosk', request, { path: { code } }).pipe(catchError((error: unknown) => throwError(() => toZugangFehler(error))));
  }

  changePin(pin: string): Observable<void> {
    return this.api.post('/api/kiosk/pin', { pin }).pipe(
      map(() => undefined),
      catchError((error: unknown) => throwError(() => toZugangFehler(error))),
    );
  }

  /** Vergessene PIN ohne eigenes Gerät: Neusetzung mit dem Wiederherstellungscode am Kiosk (Zugang 6.2). */
  resetPin(kioskId: string, recoveryCode: string, pin: string): Observable<void> {
    return this.api.post('/api/kiosk/pin/reset', { kioskId, recoveryCode, pin }).pipe(
      map(() => undefined),
      catchError((error: unknown) => throwError(() => toKioskFehler(error))),
    );
  }

  /** „Auf mein Handy übertragen“ (Zugang 6.4): einmaliger Link, fünf Minuten, als QR-Code angezeigt. */
  transfer(): Observable<Transfer> {
    return this.api.post('/api/kiosk/transfer', null).pipe(catchError((error: unknown) => throwError(() => toZugangFehler(error))));
  }
}

/** 401 am Kiosk ist „Kennung oder PIN falsch“ (gleiche Antwort für beides, Zugang 6.2); 423 nennt die Sperre. */
export function toKioskFehler(error: unknown): ZugangFehler {
  if (error instanceof HttpErrorResponse) {
    const problem = toProblem(error.status, error.error);
    if (error.status === 423 && problem.detail) {
      return { key: `zugang.fehler.${problem.detail}`, status: 423 };
    }
    if (error.status === 401) {
      return { key: 'zugang.fehler.kiosk_login_failed', status: 401 };
    }
  }
  return toZugangFehler(error);
}
