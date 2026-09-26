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
  return { tenantId, theme };
}

export const hex = (value: string): string => {
  const n = parseInt(value.slice(1), 16);
  return `rgb(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255})`;
};
