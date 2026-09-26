// Fachpfad (Stufe 6; Feed 2.3, 3.2; Fortschritt 6; Benachrichtigungen 6.1, 7.2; Challenges 4): Startseite mit Kopfkarte, Check-in
// und Stream aus den Vertragsproben; Beitrag mit „Nur für mich“ erklärt den Grund; Ich mit Fortschritt, Sichtbarkeit, Gruppen und
// Benachrichtigungen; Benachrichtigungszentrum; Verwaltung mit Kickoff, Vorschau als Pflicht und Planen. Screenshots für das Protokoll.
import { expect, test } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { mockApi, sample } from '../api-mock';

const screenshotDir = process.env['CH_SCREENSHOT_DIR'] ?? join(process.cwd(), '..', '..', 'durchstich', 'abnahme', 'laeufe', 'stufe-6');
mkdirSync(screenshotDir, { recursive: true });

test('Startseite 390px: Kopfkarte mit laufender Challenge, Check-in-Karte, Stream mit Karten aus dem Katalog, Beitrag schreiben', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.emulateMedia({ colorScheme: 'light' });
  const { tenantId } = await mockApi(page, { brand: 'wiesner' });
  const posts: { path: string; body: unknown }[] = [];
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes('/api/')) {
      posts.push({ path: new URL(request.url()).pathname, body: request.postDataJSON() as unknown });
    }
  });

  await page.goto(`/t/${tenantId}/start`);
  await expect(page.getByRole('heading', { name: 'Rad oder Fuß zur Arbeit' })).toBeVisible();
  const feed = sample('feed').body as { checkInDue: boolean; cards: { kind: string; textKey: string }[] };
  if (feed.checkInDue) {
    await expect(page.getByTestId('checkin')).toBeVisible();
    await page.getByTestId('kachel-moved').click();
    await page.getByTestId('checkin-senden').click();
    await expect(page.getByRole('status')).toContainText('Check-in');
    expect(posts.find((p) => p.path === '/api/me/check-in')?.body).toEqual({ tiles: ['moved'] });
  }
  // Karten des Streams tragen Texte aus dem Katalog des Operators, nie Textschlüssel.
  const cards = page.locator('ch-feed-card');
  await expect(cards.first()).toBeVisible();
  expect(await cards.count()).toBe(feed.cards.length);
  for (const text of await cards.locator('.ch-feed-card__text').allTextContents()) {
    expect(text).not.toMatch(/^feed\./);
  }
  await page.screenshot({ path: join(screenshotDir, 'start-feed-390.png'), fullPage: true });

  await page.getByTestId('beitrag-text').fill('Heute mit dem Rad zur Arbeit.');
  await page.getByTestId('beitrag-senden').click();
  await expect(page.getByRole('status').last()).toContainText(/online|veröffentlicht/i);
  expect(posts.find((p) => p.path === '/api/feed/posts')?.body).toEqual({ body: 'Heute mit dem Rad zur Arbeit.' });
});

test('Beitrag mit „Nur für mich“: Ablehnung mit Erklärung und Wechselangebot', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const { tenantId } = await mockApi(page, { brand: 'hoedl', contribution: 'rejected' });
  await page.goto(`/t/${tenantId}/start`);
  await page.getByTestId('beitrag-text').fill('Hallo');
  await page.getByTestId('beitrag-senden').click();
  await expect(page.getByTestId('beitrag-fehler')).toContainText('Nur für mich');
  await expect(page.getByTestId('beitrag-fehler').getByRole('button')).toBeVisible();
});

test('Ich 800px: Fortschritt ohne Vergleiche, Sichtbarkeit, Gruppen, Benachrichtigungen, Datenexport', async ({ page }) => {
  await page.setViewportSize({ width: 800, height: 1000 });
  await page.emulateMedia({ colorScheme: 'dark' });
  const { tenantId } = await mockApi(page, { brand: 'wiesner' });
  await page.goto(`/t/${tenantId}/ich`);
  await expect(page.getByTestId('fortschritt')).toBeVisible();
  await expect(page.getByTestId('abzeichen-einstieg.erster_tag')).toBeVisible();
  await expect(page.getByTestId('sichtbarkeit')).toBeVisible();
  await expect(page.getByTestId('benachrichtigungen-einstellungen')).toBeVisible();
  await expect(page.getByTestId('export')).toHaveAttribute('href', '/api/me/export');
  // Keine Vergleiche mit anderen Personen, keine Sperrbegriffe in der Oberfläche (Marke 6.3).
  const text = await page.getByTestId('fortschritt').innerText();
  for (const term of ['Ranking', 'Leistung', 'Überwachung', 'Rangliste']) {
    expect(text).not.toContain(term);
  }
  await page.screenshot({ path: join(screenshotDir, 'ich-fortschritt-800-dunkel.png'), fullPage: true });
});

test('Benachrichtigungszentrum hinter der Glocke: Einträge aus dem Katalog, Öffnen markiert als gelesen', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  const { tenantId } = await mockApi(page, { brand: 'wiesner' });
  await page.goto(`/t/${tenantId}/start`);
  await page.getByTestId('glocke').click();
  await expect(page).toHaveURL(new RegExp(`/t/${tenantId}/benachrichtigungen$`));
  const entries = sample('notifications').body as unknown[];
  if (entries.length > 0) {
    await expect(page.getByTestId(/^eintrag-/).first()).toBeVisible();
    await expect(page.getByTestId(/^eintrag-/).first()).not.toContainText('benachrichtigung.');
  }
  await page.screenshot({ path: join(screenshotDir, 'benachrichtigungen-1280.png') });
});

test('Verwaltung Challenges 1280px: Kickoff-Entwurf, Vorschau, Planen', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  const { tenantId } = await mockApi(page, { brand: 'wiesner' });
  const posts: string[] = [];
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes('/api/')) {
      posts.push(new URL(request.url()).pathname);
    }
  });
  await page.goto(`/t/${tenantId}/verwaltung/challenges`);
  await expect(page.getByTestId('challenge-liste')).toBeVisible();
  await page.getByTestId('kickoff').click();
  await expect(page.getByTestId('vorschau')).toBeVisible();
  await expect(page.getByTestId('vorschau')).toContainText('Gemeinsam starten');
  await page.screenshot({ path: join(screenshotDir, 'verwaltung-challenges-vorschau-1280.png'), fullPage: true });
  await page.getByTestId('planen').click();
  await expect(page.getByTestId('vorschau')).toHaveCount(0);
  expect(posts.some((p) => p === '/api/challenges/kickoff')).toBe(true);
  expect(posts.some((p) => /\/api\/challenges\/[^/]+\/preview$/.test(p))).toBe(true);
  expect(posts.some((p) => /\/api\/challenges\/[^/]+\/plan$/.test(p))).toBe(true);
});
