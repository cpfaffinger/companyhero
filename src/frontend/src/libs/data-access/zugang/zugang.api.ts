// Datenzugriff des Zugangs (Zugang 2 bis 5 und 8; K11, K12): Sitzung, Anmeldewege, Beitritt, Rollencodes, Kontoverwaltung.
// Alle Aufrufe laufen über den generierten Vertrag; das Sitzungs-Cookie sendet der Browser (withCredentials), das CSRF-Token
// setzt der HttpClient aus dem lesbaren Cookie `ch_csrf` (A-007). Geheimnisse liegen nie im Client.
import { HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, from, map, Observable, of, switchMap, tap, throwError } from 'rxjs';
import { ApiClient, toProblem } from '../../api-client/api-client';
import type { components } from '../../api-client/generated/api';
import { OfflineFreigabe } from './offline-freigabe';
import { SessionStore, type SessionInfo } from './session.store';
import { passkeyErstellen, passkeyVerwenden } from './webauthn';

export type LoginResponse = components['schemas']['LoginResponse'];
export type JoinPreview = components['schemas']['JoinPreviewResponse'];
export type JoinRequest = components['schemas']['JoinRequest'];
export type RoleJoinRequest = components['schemas']['RoleJoinRequest'];
export type JoinResponse = components['schemas']['JoinResponse'];
export type AccessOverview = components['schemas']['AccessOverviewResponse'];
export type Provider = components['schemas']['ProviderResponse'];
export type PasskeyAnswer = components['schemas']['PasskeyAnswerRequest'];
export type Visibility = components['schemas']['VisibilityDto'];

/** Stabile Fehlerkennung für die Oberfläche: Textschlüssel im Katalog (zugang.fehler.*). */
export interface ZugangFehler {
  key: string;
  status: number;
}

@Injectable({ providedIn: 'root' })
export class ZugangApi {
  private readonly api = inject(ApiClient);
  private readonly store = inject(SessionStore);
  private readonly freigabe = inject(OfflineFreigabe);

  /** Sitzung des Browsers; 401 bedeutet „keine Sitzung“, kein Fehler. */
  session(): Observable<SessionInfo | null> {
    return this.api.get('/api/auth/session').pipe(
      tap((session) => this.store.set(session)),
      catchError((error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 401) {
          this.store.set(null);
          return of(null);
        }
        return throwError(() => error);
      }),
    );
  }

  logout(): Observable<void> {
    return this.api.post('/api/auth/logout', null).pipe(
      map(() => undefined),
      tap(() => this.store.set(null)),
    );
  }

  /** Abmeldung aus allen Sitzungen (Zugang 4, 5): beendet auch die Offline-Freigabe dieses Geräts. */
  logoutAll(): Observable<number> {
    return this.api.post('/api/auth/logout-all', null).pipe(
      map((r) => r.count),
      tap(() => {
        this.store.set(null);
        this.freigabe.beenden();
      }),
    );
  }

  providers(tenantId?: string): Observable<Provider[]> {
    return this.api.get('/api/auth/providers', tenantId ? { query: { tenant: tenantId } } : undefined);
  }

  /** Start des externen Anbieters als Top-Level-Navigation (Zugang 3.1: Callback und Codeaustausch im Backend). */
  oidcStartUrl(key: string, intent: 'login' | 'join' | 'link', returnUrl: string): string {
    return `/api/auth/oidc/${encodeURIComponent(key)}/start?intent=${intent}&returnUrl=${encodeURIComponent(returnUrl)}`;
  }

  /** Anmeldung mit Passkey: Optionen → Authenticator → Antwort; Sitzung als Cookie. */
  loginWithPasskey(): Observable<LoginResponse> {
    return this.api.post('/api/auth/passkey/options', null).pipe(
      switchMap((ceremony) => from(passkeyVerwenden(ceremony.options)).pipe(map((credential) => ({ state: ceremony.state, credential, deviceName: null })))),
      switchMap((answer) => this.api.post('/api/auth/passkey', answer)),
      tap(() => this.store.ended.set(false)),
      catchError((error: unknown) => throwError(() => toZugangFehler(error))),
    );
  }

  loginWithRecoveryCode(code: string): Observable<LoginResponse> {
    return this.api.post('/api/auth/recovery', { code }).pipe(catchError((error: unknown) => throwError(() => toZugangFehler(error))));
  }

  requestMagicLink(email: string): Observable<void> {
    return this.api.post('/api/auth/magic-link', { email }).pipe(
      map(() => undefined),
      catchError((error: unknown) => throwError(() => toZugangFehler(error))),
    );
  }

  consumeMagicLink(token: string): Observable<LoginResponse> {
    return this.api.post('/api/auth/magic-link/consume', { token }).pipe(catchError((error: unknown) => throwError(() => toZugangFehler(error))));
  }

  /** Übertragung vom Kiosk (Zugang 6.4): Mitgliedssitzung auf dem eigenen Gerät, Einrichtung folgt sofort. */
  consumeTransfer(token: string): Observable<LoginResponse> {
    return this.api.post('/api/auth/transfer', { token }).pipe(catchError((error: unknown) => throwError(() => toZugangFehler(error))));
  }

  joinPreview(code: string): Observable<JoinPreview> {
    return this.api.get('/api/join/{code}', { path: { code } }).pipe(catchError((error: unknown) => throwError(() => toZugangFehler(error))));
  }

  /** Beitritt mit Passkey (Zugang 2.2): Registrierung am Gerät, dann Beitritt in einer Transaktion. */
  joinWithPasskey(code: string, request: Omit<JoinRequest, 'passkey' | 'useExternal'>, deviceName: string | null): Observable<JoinResponse> {
    return this.api.post('/api/join/{code}/passkey-options', { displayName: request.displayName }, { path: { code } }).pipe(
      switchMap((ceremony) => from(passkeyErstellen(ceremony.options)).pipe(map((credential): PasskeyAnswer => ({ state: ceremony.state, credential, deviceName })))),
      switchMap((passkey) => this.api.post('/api/join/{code}', { ...request, passkey, useExternal: false }, { path: { code } })),
      catchError((error: unknown) => throwError(() => toZugangFehler(error))),
    );
  }

  /** Beitritt mit E-Mail oder mit der zuvor geprüften externen Identität. */
  join(code: string, request: JoinRequest): Observable<JoinResponse> {
    return this.api.post('/api/join/{code}', request, { path: { code } }).pipe(catchError((error: unknown) => throwError(() => toZugangFehler(error))));
  }

  rolePreview(code: string): Observable<JoinPreview> {
    return this.api.get('/api/join/role/{code}', { path: { code } }).pipe(catchError((error: unknown) => throwError(() => toZugangFehler(error))));
  }

  roleJoinWithPasskey(code: string, request: Omit<RoleJoinRequest, 'passkey' | 'useExternal'>, deviceName: string | null): Observable<JoinResponse> {
    return this.api.post('/api/join/role/{code}/passkey-options', { displayName: request.realName }, { path: { code } }).pipe(
      switchMap((ceremony) => from(passkeyErstellen(ceremony.options)).pipe(map((credential): PasskeyAnswer => ({ state: ceremony.state, credential, deviceName })))),
      switchMap((passkey) => this.api.post('/api/join/role/{code}', { ...request, passkey, useExternal: false }, { path: { code } })),
      catchError((error: unknown) => throwError(() => toZugangFehler(error))),
    );
  }

  roleJoin(code: string, request: RoleJoinRequest): Observable<JoinResponse> {
    return this.api.post('/api/join/role/{code}', request, { path: { code } }).pipe(catchError((error: unknown) => throwError(() => toZugangFehler(error))));
  }

  /** Bestehende Person löst einen Rollencode ein (Zugang 2.3); die Sitzung endet danach, weil sich die Sitzungsart ändert. */
  redeemRoleCode(code: string, realName: string | null): Observable<void> {
    return this.api.post('/api/access/role-codes/redeem', { code, realName }).pipe(
      map(() => undefined),
      catchError((error: unknown) => throwError(() => toZugangFehler(error))),
    );
  }

  access(): Observable<AccessOverview> {
    return this.api.get('/api/me/access');
  }

  addPasskey(deviceName: string): Observable<void> {
    return this.api.post('/api/me/passkeys/options', null).pipe(
      switchMap((ceremony) => from(passkeyErstellen(ceremony.options)).pipe(map((credential): PasskeyAnswer => ({ state: ceremony.state, credential, deviceName })))),
      switchMap((answer) => this.api.post('/api/me/passkeys', answer)),
      map(() => undefined),
      catchError((error: unknown) => throwError(() => toZugangFehler(error))),
    );
  }

  removePasskey(passkeyId: string): Observable<void> {
    return this.api.delete('/api/me/passkeys/{passkeyId}', { path: { passkeyId } }).pipe(
      map(() => undefined),
      catchError((error: unknown) => throwError(() => toZugangFehler(error))),
    );
  }

  setEmail(email: string): Observable<void> {
    return this.api.put('/api/me/email', { email }).pipe(
      map(() => undefined),
      catchError((error: unknown) => throwError(() => toZugangFehler(error))),
    );
  }

  removeEmail(): Observable<void> {
    return this.api.delete('/api/me/email').pipe(
      map(() => undefined),
      catchError((error: unknown) => throwError(() => toZugangFehler(error))),
    );
  }

  unlinkProvider(linkId: string): Observable<void> {
    return this.api.delete('/api/me/providers/{linkId}', { path: { linkId } }).pipe(
      map(() => undefined),
      catchError((error: unknown) => throwError(() => toZugangFehler(error))),
    );
  }

  /** Neuer Wiederherstellungscode; der alte verfällt, der neue wird genau einmal angezeigt. */
  renewRecoveryCode(): Observable<string> {
    return this.api.post('/api/me/recovery-code', null).pipe(map((r) => r.code));
  }

  /** Kiosk-PIN aus der eigenen App setzen oder ändern (Zugang 6.2). */
  setKioskPin(pin: string): Observable<void> {
    return this.api.post('/api/kiosk/pin', { pin }).pipe(
      map(() => undefined),
      catchError((error: unknown) => throwError(() => toZugangFehler(error))),
    );
  }

  /** Austritt (Zugang 8): alle Sitzungen und Identitäten enden; die Offline-Freigabe dieses Geräts ebenso. */
  leave(): Observable<void> {
    return this.api.post('/api/me/leave', null).pipe(
      map(() => undefined),
      tap(() => {
        this.store.set(null);
        this.freigabe.beenden();
      }),
    );
  }
}

/** Ablehnungen des Zugangs als stabile Textschlüssel; unbekannte Gründe werden nicht erraten (Zugang 2.1: keine Information über Codes). */
export function toZugangFehler(error: unknown): ZugangFehler {
  if (error instanceof Error && error.message === 'passkey_abgebrochen') {
    return { key: 'zugang.fehler.passkeyAbgebrochen', status: 0 };
  }
  if (error instanceof HttpErrorResponse) {
    const problem = toProblem(error.status, error.error);
    if (error.status === 0) {
      return { key: 'fehler.Verbindung', status: 0 };
    }
    if (problem.detail && /^[a-z_]+$/.test(problem.detail)) {
      return { key: `zugang.fehler.${problem.detail}`, status: error.status };
    }
    if (error.status === 401) {
      return { key: 'zugang.fehler.abgelehnt', status: 401 };
    }
    if (error.status === 404) {
      return { key: 'zugang.fehler.codeUnbekannt', status: 404 };
    }
    return { key: 'fehler.Verbindung', status: error.status };
  }
  if (error instanceof DOMException) {
    // Der Authenticator hat abgebrochen oder abgelehnt (NotAllowedError, InvalidStateError).
    return { key: 'zugang.fehler.passkeyAbgebrochen', status: 0 };
  }
  return { key: 'fehler.Verbindung', status: 0 };
}
