// Geld (Stufe 7; Entitlements 8.1, 8.2; Metering 5, 9.8): Navigation nur aus aktiven Entitlements ohne Schlösser und Hinweise,
// Verwaltung „Module und Kosten“ mit Kostenvorschau, Positionen, Modulkatalog mit Kosten vor dem Klick und Bündelvorschlag,
// Verbrauch als Tagesaggregate (personennahe Metriken nur als Summe), Rechnungsentwurf mit Positionen. Screenshots für das Protokoll.
import { expect, test } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { mockApi, sample } from '../api-mock';

const screenshotDir = process.env['CH_SCREENSHOT_DIR'] ?? join(process.cwd(), '..', '..', 'durchstich', 'abnahme', 'laeufe', 'stufe-7');
mkdirSync(screenshotDir, { recursive: true });

test('Mitglieder-App nur mit Kern: zwei Bereiche, keine Schlösser, keine Hinweise auf Module', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const { tenantId } = await mockApi(page, { brand: 'hoedl', modules: 'core' });
  await page.goto(`/t/${tenantId}/start`);
  const nav = page.getByRole('navigation');
  await expect(nav.locator('.ch-shell__item')).toHaveCount(2);
  await expect(nav.locator('.ch-shell__item')).toHaveText([/Start/, /Ich/]);
  await expect(nav.getByText('Challenges')).toHaveCount(0);
  await expect(page.locator('.material-symbols-rounded', { hasText: /^lock/ })).toHaveCount(0);
  const text = await page.locator('body').innerText();
  expect(text).not.toMatch(/M1|Modul buchen|freischalten|Upgrade/i);
  await page.screenshot({ path: join(screenshotDir, 'nur-kern-390.png'), fullPage: true });

  // Mit M1 (Voreinstellung) erscheint der Bereich Challenges beim nächsten Laden.
  const full = await mockApi(page, { brand: 'hoedl', modules: 'default' });
  await page.goto(`/t/${full.tenantId}/start`);
  await expect(page.getByRole('navigation').locator('.ch-shell__item')).toHaveText([/Start/, /Challenges/, /Ich/]);
});

test('Verwaltung Module und Kosten 1280px: Kostenvorschau, Positionen, Verbrauch als Tagesaggregate, Rechnungsentwurf, Definitionen', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 1100 });
  await page.emulateMedia({ colorScheme: 'light' });
  const { tenantId } = await mockApi(page, { brand: 'wiesner' });
  await page.goto(`/t/${tenantId}/verwaltung/abrechnung`);

  const preview = sample('billing-preview').body as { net: string; lines: { model: string }[]; definitions: unknown[] };
  await expect(page.getByTestId('kostenvorschau')).toBeVisible();
  // Beträge im Landesformat mit Dezimalkomma, nie als binäre Gleitkommazahl gerundet.
  await expect(page.getByTestId('netto')).toContainText(preview.net.replace('.', ',') + ' €');
  await expect(page.getByTestId('prognose')).toContainText('€');
  await expect(page.getByTestId('positionen').locator('tbody tr')).toHaveCount(preview.lines.filter((l) => l.model !== 'revenue_share').length);
  // Kein Textschlüssel sichtbar: alle Texte aus dem Katalog des Operators.
  const body = await page.locator('body').innerText();
  expect(body).not.toMatch(/verwaltung\.abrechnung\./);
  expect(body).not.toMatch(/\bmetrik\./);

  // Verbrauch: personennahe Metriken nur als Monatssumme, andere mit Tagesaggregaten.
  await expect(page.getByTestId('verbrauch')).toBeVisible();
  await expect(page.getByTestId('verbrauch-member.active_month')).toContainText('nur Monatssumme');
  await expect(page.getByTestId('verbrauch-kiosk.device_month').getByRole('button')).toBeVisible();
  await page.getByTestId('verbrauch-kiosk.device_month').getByRole('button').click();
  await expect(page.getByTestId('verbrauch-kiosk.device_month').locator('li')).toHaveCount(1);
  await expect(page.getByTestId('export')).toHaveAttribute('href', /\/api\/billing\/usage\/export\?period=2026-09/);

  // Rechnungsentwurf mit Positionen.
  await page.getByTestId('rechnung-2026-09').click();
  await expect(page.getByTestId('rechnung-detail')).toBeVisible();
  const invoice = sample('billing-invoice').body as { lines: unknown[] };
  await expect(page.getByTestId('rechnung-detail').locator('tbody tr')).toHaveCount(invoice.lines.length);
  await expect(page.getByTestId('definitionen')).toContainText('Aktives Mitglied');
  await page.screenshot({ path: join(screenshotDir, 'verwaltung-abrechnung-1280.png'), fullPage: true });
});

test('Buchung mit Kosten vor dem Klick und Bündelvorschlag statt Ablehnung', async ({ page }) => {
  await page.setViewportSize({ width: 800, height: 1000 });
  const { tenantId } = await mockApi(page, { brand: 'wiesner' });
  const posts: { path: string; body: unknown }[] = [];
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes('/api/')) {
      posts.push({ path: new URL(request.url()).pathname, body: request.postDataJSON() as unknown });
    }
  });
  await page.goto(`/t/${tenantId}/verwaltung/abrechnung`);
  await expect(page.getByTestId('modul-M2')).toBeVisible();
  await expect(page.getByTestId('modul-M2')).toContainText('Datenschutz');
  await page.getByTestId('buchen-M2').click();
  // Auswirkung auf laufenden Monat und Folgemonat vor dem Klick (Entitlements 4.2 Nr. 2).
  await expect(page.getByTestId('auswirkung')).toContainText('Folgemonat');
  expect(posts).toHaveLength(0);
  await page.getByTestId('jetzt-buchen').click();
  await expect(page.getByTestId('buendel')).toContainText('Challenges, Arena');
  expect(posts.map((p) => p.path)).toEqual(['/api/entitlements/modules/M2/book']);
  await page.screenshot({ path: join(screenshotDir, 'buendelvorschlag-800.png'), fullPage: true });
  await page.getByTestId('buendel-buchen').click();
  await expect(page.getByTestId('meldung')).toContainText('Gebucht');
  expect(posts.at(-1)).toEqual({ path: '/api/entitlements/bundles/book', body: { modules: ['M1', 'M2'] } });
});
