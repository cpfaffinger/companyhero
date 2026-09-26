// Vertragsproben (echte API-Antworten, aufgenommen von ContractSamplesTests) als Antworten für die Ende-zu-Ende-Tests.
// Zeitangaben der Challenge werden relativ zum Testzeitpunkt gesetzt, damit „noch 9 Tage“ und „Stand vor 4 Min“ stabil sind.
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import type { Page } from '@playwright/test';

export type Brand = 'wiesner' | 'hoedl' | 'standard';

interface Sample {
  status: number;
  contentType: string | null;
  body: unknown;
}

const fixtures = join(process.cwd(), 'e2e', 'fixtures', 'api');

export function sample(name: string): Sample {
  return JSON.parse(readFileSync(join(fixtures, `${name}.json`), 'utf8')) as Sample;
}

export interface MockOptions {
  brand: Brand;
  /** Antwort auf einen Beitrag: Erfolg (201) oder die Ablehnung aus der Probe (422).*/
  contribution?: 'created' | 'rejected';
  /** Ohne Sitzung: Theme-Endpunkt antwortet 401 (Anmeldeseite vor Zuordnung in Plattformmarke). */
  unauthenticated?: boolean;
  /** Kiosk (Zugang 6): Gerät registriert (Gerätesitzung) oder nicht registriert (401 am Gerätezustand). */
  kiosk?: 'device' | 'unregistered';
  /** Entitlements (Stufe 7): Kern plus M1 (Voreinstellung) oder nur Kern (zwei Bereiche, keine Hinweise auf Module). */
  modules?: 'default' | 'core';
}

/** Zustandsändernde Requests der Zugangs-Endpunkte, die ein Test prüft: Methode, Pfad, Körper, CSRF-Header. */
export interface RecordedRequest {
  method: string;
  path: string;
  body: unknown;
  csrf: string | null;
}

export async function mockApi(page: Page, options: MockOptions): Promise<{ tenantId: string; theme: Sample }> {
  const theme = sample(`theme-${options.brand}`);
  const platform = sample('platform');
  const challenges = sample('challenges');
  const now = Date.now();
  const cards = (challenges.body as { endsAt: string; startsAt: string; collective: { updatedAt: string } | null }[]).map((card) => ({
    ...card,
    startsAt: new Date(now - 12 * 86_400_000).toISOString(),
    endsAt: new Date(now + 9 * 86_400_000 - 60_000).toISOString(),
    collective: card.collective ? { ...card.collective, updatedAt: new Date(now - 4 * 60_000).toISOString() } : null,
  }));
  const tenantId = (theme.body as { tenantId: string }).tenantId;

  const respond = (s: Sample, body?: unknown) => ({ status: s.status, contentType: s.contentType ?? 'application/json', body: JSON.stringify(body ?? s.body) });

  await page.route('**/api/branding/theme', (route) => {
    if (options.unauthenticated) {
      return route.fulfill({ status: 401, body: '' });
    }
    return route.fulfill(respond(theme));
  });
  await page.route('**/api/branding/platform', (route) => route.fulfill(respond(platform)));
  await page.route('**/api/branding/tenants/*/manifest.webmanifest', (route) => route.fulfill(respond(sample('manifest-wiesner'))));
  await page.route('**/api/challenges', (route) => route.fulfill(respond(challenges, cards)));
  await page.route('**/api/challenges/*/contributions', (route) => {
    const s = sample(options.contribution === 'rejected' ? 'contribution-rejected' : 'contribution-created');
    return route.fulfill(respond(s));
  });
  await mockAccess(page, options);
  await mockFachpfad(page, options);
  await mockGeld(page, options);
  return { tenantId, theme };
}

/**
 * Geld (Stufe 7): Navigation aus Entitlements, Modulkatalog mit Buchung (Bündelvorschlag aus der Probe beim ersten Versuch, danach
 * gebucht), Kostenvorschau, Simulation, Verbrauch als Tagesaggregate, Rechnungsentwürfe aus den Vertragsproben.
 */
export async function mockGeld(page: Page, options: MockOptions): Promise<void> {
  const respond = (s: Sample, body?: unknown) => ({ status: s.status, contentType: s.contentType ?? 'application/json', body: JSON.stringify(body ?? s.body) });
  await page.route('**/api/entitlements/me', (route) => route.fulfill(respond(sample(options.modules === 'core' ? 'entitlements-me-core' : 'entitlements-me'))));
  await page.route('**/api/entitlements/modules', (route) => route.fulfill(respond(sample('entitlements-modules'))));
  await page.route('**/api/entitlements/modules/*/book', (route) => route.fulfill(respond(sample('entitlements-book-suggested'))));
  await page.route('**/api/entitlements/bundles/book', (route) => route.fulfill(respond(sample('entitlements-bundle-booked'))));
  await page.route('**/api/entitlements/modules/*/trial', (route) => route.fulfill(respond(sample('entitlements-trial'))));
  await page.route('**/api/entitlements/modules/*/cancel', (route) => route.fulfill(respond(sample('entitlements-cancel'))));
  await page.route('**/api/entitlements/modules/*/revoke-cancellation', (route) => route.fulfill(respond(sample('entitlements-cancel'), { ...(sample('entitlements-cancel').body as object), activeUntil: null })));
  await page.route('**/api/entitlements/history', (route) => route.fulfill(respond(sample('entitlements-history'))));
  await page.route('**/api/billing/preview', (route) => route.fulfill(respond(sample('billing-preview'))));
  await page.route('**/api/billing/preview/simulate?*', (route) => route.fulfill(respond(sample('billing-simulate'))));
  await page.route('**/api/billing/usage?*', (route) => route.fulfill(respond(sample('billing-usage'))));
  await page.route('**/api/billing/invoices', (route) => route.fulfill(respond(sample('billing-invoices'))));
  await page.route('**/api/billing/invoices/*', (route) => route.fulfill(respond(sample('billing-invoice'))));
  await page.route('**/api/billing/metrics', (route) => route.fulfill(respond(sample('billing-metrics'))));
}

/**
 * Fachpfad (Stufe 6): Feed, Fortschritt, Benachrichtigungen, Gruppen, Sichtbarkeit und Challenge-Verwaltung aus den Vertragsproben.
 * Zustandsändernde Aufrufe antworten mit den Proben (Beitrag, Check-in, Kickoff) oder 204; die Proben selbst bleiben unverändert.
 */
export async function mockFachpfad(page: Page, options: MockOptions): Promise<void> {
  const respond = (s: Sample, body?: unknown) => ({ status: s.status, contentType: s.contentType ?? 'application/json', body: JSON.stringify(body ?? s.body) });
  const feed = sample('feed');
  const now = Date.now();
  const feedBody = feed.body as { head: { challenge: { startsAt: string; endsAt: string; collective: { updatedAt: string } | null } | null }; cards: { day: string; occurredAt: string }[] };
  const today = new Date(now).toISOString().slice(0, 10);
  const feedNow = {
    ...feedBody,
    head: feedBody.head.challenge
      ? { ...feedBody.head, challenge: { ...feedBody.head.challenge, startsAt: new Date(now - 12 * 86_400_000).toISOString(), endsAt: new Date(now + 9 * 86_400_000 - 60_000).toISOString(), collective: feedBody.head.challenge.collective ? { ...feedBody.head.challenge.collective, updatedAt: new Date(now - 4 * 60_000).toISOString() } : null } }
      : feedBody.head,
    cards: feedBody.cards.map((card) => ({ ...card, day: today })),
  };
  await page.route('**/api/feed', (route) => route.fulfill(respond(feed, feedNow)));
  await page.route('**/api/feed/posts', (route) => (options.contribution === 'rejected' ? route.fulfill({ status: 422, contentType: 'application/problem+json', body: JSON.stringify({ status: 422, title: 'Mit „Nur für mich“ ist kein Beitrag möglich', detail: 'visibility_only_me' }) }) : route.fulfill(respond(sample('feed-post-created')))));
  await page.route('**/api/me/progress', (route) => route.fulfill(respond(sample('me-progress'))));
  await page.route('**/api/me/progress/daily-goal', (route) => route.fulfill({ status: 204, body: '' }));
  await page.route('**/api/me/check-in', (route) => route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({ outcome: 'recorded' }) }));
  await page.route('**/api/notifications', (route) => route.fulfill(respond(sample('notifications'))));
  await page.route('**/api/notifications?*', (route) => route.fulfill(respond(sample('notifications'))));
  await page.route('**/api/notifications/*/read', (route) => route.fulfill({ status: 204, body: '' }));
  await page.route('**/api/notifications/read-all', (route) => route.fulfill({ status: 204, body: '' }));
  await page.route('**/api/notifications/vapid', (route) => route.fulfill(respond(sample('vapid'))));
  await page.route('**/api/me/notifications', (route) => route.fulfill(respond(sample('me-notifications'))));
  await page.route('**/api/me/notifications/subscriptions', (route) => route.fulfill(respond(sample('me-notification-subscriptions'))));
  await page.route('**/api/organisation/dimensions', (route) => route.fulfill(respond(sample('dimensions'))));
  await page.route('**/api/organisation/tenant', (route) => route.fulfill(respond(sample('tenant'))));
  await page.route('**/api/me/groups', (route) => (route.request().method() === 'GET' ? route.fulfill(respond(sample('me-groups'))) : route.fulfill({ status: 204, body: '' })));
  await page.route('**/api/me/visibility', (route) => (route.request().method() === 'GET' ? route.fulfill(respond(sample('me-visibility'))) : route.fulfill({ status: 204, body: '' })));
  await page.route('**/api/me/consents', (route) => route.fulfill(respond(sample('me-consents'))));
  await page.route('**/api/challenges/manage', (route) => route.fulfill(respond(sample('challenges-manage'))));
  await page.route('**/api/challenges/kickoff', (route) => route.fulfill(respond(sample('challenge-kickoff'))));
  await page.route('**/api/challenges/*/preview', (route) => route.fulfill(respond(sample('challenge-kickoff'), { ...(sample('challenge-kickoff').body as object), previewed: true })));
  await page.route('**/api/challenges/*/plan', (route) => route.fulfill({ status: 204, body: '' }));
}

/**
 * Zugang (Stufe 4): Sitzung, Anbieter, Beitrittsvorschau, Konto, Kiosk aus den Vertragsproben. Das CSRF-Cookie setzt der Mock wie
 * die API (lesbar, `ch_csrf`); der Client muss es als Header `X-CSRF-Token` zurückgeben (A-007).
 */
export async function mockAccess(page: Page, options: MockOptions): Promise<RecordedRequest[]> {
  const recorded: RecordedRequest[] = [];
  const record = (route: Parameters<Parameters<Page['route']>[1]>[0]) => {
    const request = route.request();
    if (request.method() !== 'GET') {
      recorded.push({ method: request.method(), path: new URL(request.url()).pathname, body: request.postDataJSON() as unknown, csrf: request.headers()['x-csrf-token'] ?? null });
    }
  };
  const respond = (s: Sample, body?: unknown, headers?: Record<string, string>) => ({ status: s.status, contentType: s.contentType ?? 'application/json', body: s.body === null && body === undefined ? '' : JSON.stringify(body ?? s.body), headers });
  const csrfCookie = { headers: { 'set-cookie': 'ch_csrf=probe-csrf-token; Path=/; SameSite=Lax' } };

  await page.context().addCookies([{ name: 'ch_csrf', value: 'probe-csrf-token', domain: 'localhost', path: '/' }]);
  await page.route('**/api/auth/session', (route) => route.fulfill(respond(sample(options.unauthenticated ? 'session-none' : 'session'))));
  await page.route('**/api/auth/providers**', (route) => route.fulfill(respond(sample('providers'))));
  await page.route('**/api/auth/passkey/options', (route) => {
    record(route);
    return route.fulfill(respond(sample('session'), passkeyOptions('auth')));
  });
  await page.route('**/api/auth/passkey', (route) => {
    record(route);
    return route.fulfill(respond(sample('session'), { tenantId: (sample('session').body as { tenantId: string }).tenantId, personId: 'p', kind: 'member', newRecoveryCode: null, mustSetUpAccess: false }, csrfCookie.headers));
  });
  await page.route('**/api/auth/magic-link', (route) => {
    record(route);
    return route.fulfill({ status: 202, body: '' });
  });
  await page.route('**/api/auth/logout', (route) => {
    record(route);
    return route.fulfill({ status: 204, body: '' });
  });
  // Beitritt: Vorschau (GET), Passkey-Optionen für die neue Person, Beitritt (201 mit Kennung und Wiederherstellungscode).
  await page.route('**/api/join/*', (route) => {
    record(route);
    return route.request().method() === 'GET' ? route.fulfill(respond(sample('join-preview'))) : route.fulfill(respond(sample('kiosk-join'), undefined, csrfCookie.headers));
  });
  await page.route('**/api/join/*/passkey-options', (route) => {
    record(route);
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(passkeyOptions('create')) });
  });
  await page.route('**/api/join/*/kiosk', (route) => {
    record(route);
    return route.fulfill(respond(sample('kiosk-join')));
  });
  await page.route('**/api/me/access', (route) => route.fulfill(respond(sample('me-access'))));
  await page.route('**/api/kiosk/device', (route) => (options.kiosk === 'device' ? route.fulfill(respond(sample('kiosk-device'), undefined, csrfCookie.headers)) : route.fulfill({ status: 401, body: '' })));
  await page.route('**/api/kiosk/login', (route) => {
    record(route);
    const body = route.request().postDataJSON() as { pin: string };
    if (body.pin === '0000') {
      return route.fulfill(respond(sample('kiosk-login-rejected')));
    }
    const login = sample('kiosk-login').body as { idleUntil: string; absoluteUntil: string; idleSeconds: number };
    const now = Date.now();
    return route.fulfill(respond(sample('kiosk-login'), { ...login, idleUntil: new Date(now + login.idleSeconds * 1000).toISOString(), absoluteUntil: new Date(now + 600_000).toISOString() }));
  });
  await page.route('**/api/kiosk/transfer', (route) => {
    record(route);
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ url: 'https://localhost/zugang/transfer?token=probe-token', expiresAt: new Date(Date.now() + 300_000).toISOString() }) });
  });
  await page.route('**/api/challenges/*/contribution-operations', (route) => {
    record(route);
    return route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({ operationId: '01a0de00-0000-7000-8000-000000000001' }) });
  });
  return recorded;
}

/** WebAuthn-Optionen im Format des Backends (Fido2NetLib-JSON, base64url) für den virtuellen Authenticator. */
export function passkeyOptions(kind: 'auth' | 'create'): { options: unknown; state: string } {
  const challenge = Buffer.from('probe-challenge-32-bytes-long-000').toString('base64url');
  if (kind === 'create') {
    return {
      state: 'probe-state',
      options: {
        rp: { id: 'localhost', name: 'CompanyHero' },
        user: { id: Buffer.from('user-handle-16by').toString('base64url'), name: 'Probe', displayName: 'Probe' },
        challenge,
        pubKeyCredParams: [{ type: 'public-key', alg: -7 }],
        timeout: 60000,
        attestation: 'none',
        authenticatorSelection: { residentKey: 'required', userVerification: 'preferred' },
        excludeCredentials: [],
      },
    };
  }
  return { state: 'probe-state', options: { challenge, timeout: 60000, rpId: 'localhost', allowCredentials: [], userVerification: 'preferred' } };
}

export const hex = (value: string): string => {
  const n = parseInt(value.slice(1), 16);
  return `rgb(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255})`;
};
