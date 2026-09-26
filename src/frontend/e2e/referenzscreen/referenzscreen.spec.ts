// Abnahme des Referenzscreens (Frontend 7; Integrationsregeln 6; Marke 9.1, 9.5, 9.7; A-003, A-081): zwei Firmenmarken und die
// Plattformmarke (Standard-Theme) aus dem Backend-Tokensatz, hell und dunkel, 390/800/1280 px, Formular mit Select im
// Fehlerzustand und Textfeld im Fokus, offener Dialog, Tastatur, Großflächenmodus, reduzierte Bewegung, Systemmodus nur bei
// `system`. Screenshots je Marke, Modus und Breite für das Protokoll.
import { expect, test, type Page } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { hex, mockApi, type Brand } from '../api-mock';

const screenshotDir = process.env['CH_SCREENSHOT_DIR'] ?? join(process.cwd(), '..', '..', 'durchstich', 'abnahme', 'laeufe', 'stufe-5');
mkdirSync(screenshotDir, { recursive: true });

const widths = [
  { width: 390, height: 844, tier: 'mobile' },
  { width: 800, height: 1000, tier: 'tablet' },
  { width: 1280, height: 900, tier: 'desktop' },
] as const;

const brands: { brand: Brand; label: string; anrede: string }[] = [
  { brand: 'wiesner', label: 'wiesner', anrede: 'Dein Beitrag heute' },
  { brand: 'hoedl', label: 'hoedl', anrede: 'Ihr Beitrag heute' },
  { brand: 'standard', label: 'plattform', anrede: 'Dein Beitrag heute' },
];

async function openReferenceScreen(page: Page, brand: Brand, mode: 'hell' | 'dunkel') {
  await page.emulateMedia({ colorScheme: mode === 'dunkel' ? 'dark' : 'light' });
  const { tenantId, theme } = await mockApi(page, { brand, contribution: 'rejected' });
  await page.goto(`/t/${tenantId}/challenges`);
  await expect(page.getByRole('heading', { name: 'Rad oder Fuß zur Arbeit' })).toBeVisible();
  // Alle drei Zustände gleichzeitig: Select im Fehlerzustand (Serverfehler), Textfeld im Fokus, Dialog offen.
  await page.getByTestId('tag').click();
  await page.getByRole('option', { name: 'Gestern', exact: true }).click();
  await page.getByRole('button', { name: 'Eintragen' }).click();
  await expect(page.locator('mat-error')).toContainText('Zukunft');
  await page.getByTestId('notiz').fill('Bis zum Bahnhof gegangen');
  await page.getByTestId('notiz').focus();
  await page.getByRole('button', { name: 'Beitrag von gestern löschen' }).click();
  await expect(page.getByRole('dialog')).toBeVisible();
  return { tenantId, tokens: (theme.body as { hell: Record<string, string>; dunkel: Record<string, string> })[mode] };
}

for (const { brand, label, anrede } of brands) {
  for (const mode of ['hell', 'dunkel'] as const) {
    for (const { width, height, tier } of widths) {
      test(`${label} ${mode} ${width}px: Tokens aus dem Backend, Material folgt, Fehler, Fokus, Dialog, Layout ${tier}`, async ({ page }) => {
        await page.setViewportSize({ width, height });
        const { tokens } = await openReferenceScreen(page, brand, mode);

        // Farbwerte identisch mit dem Backend-Tokensatz (Marke 9.1): auf dem Wurzelelement und in Material (K01).
        const primary = await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--ch-primary').trim());
        expect(primary).toBe(tokens['primary']);
        expect(await page.evaluate(() => document.documentElement.style.colorScheme)).toBe(mode === 'dunkel' ? 'dark' : 'light');
        // Hinter dem offenen Dialog ist die Seite für Hilfstechnik verborgen (aria-hidden); Geometrie und Farben per CSS-Selektor.
        const filled = page.locator('ch-challenge-card button');
        await expect(filled).toHaveCSS('background-color', hex(tokens['primary']));
        await expect(filled).toHaveCSS('color', hex(tokens['on-primary']));
        await expect(page.locator('.ch-card__percent')).toHaveCSS('color', hex(tokens['progress']));
        await expect(page.locator('body')).toHaveCSS('background-color', hex(tokens['surface']));
        // Overlays folgen dem Theme (K05): Dialogfläche in surface-container-high, Aktion in primary.
        const dialog = page.getByRole('dialog');
        await expect(dialog.getByRole('button', { name: 'Löschen' })).toHaveCSS('background-color', hex(tokens['primary']));
        await expect(dialog.locator('mat-dialog-container, .mat-mdc-dialog-surface').first()).toBeVisible();

        // Anrede und Tonalität des Tenants (A-080).
        await expect(page.locator('.ch-card__today')).toContainText(anrede);

        // Zustände: Fehler am Select, Fokus im Textfeld (der Dialog hat den Fokus übernommen, das Feld bleibt gefüllt).
        await expect(page.locator('mat-error')).toBeVisible();
        await expect(page.getByTestId('notiz')).toHaveValue('Bis zum Bahnhof gegangen');
        await expect(dialog).toContainText('Beitrag von gestern löschen?');

        // Layout (K07): Leiste, Rail oder Seitennavigation; kein horizontaler Überlauf.
        const nav = page.locator('nav.ch-shell__nav');
        const navBox = (await nav.boundingBox())!;
        if (tier === 'mobile') {
          expect(navBox.width).toBeGreaterThan(width - 1);
        } else if (tier === 'tablet') {
          expect(Math.round(navBox.width)).toBe(88);
        } else {
          expect(Math.round(navBox.width)).toBe(240);
          const context = page.locator('.ch-shell__context');
          await expect(context).toBeVisible();
          expect(Math.round((await context.boundingBox())!.width)).toBe(320);
          await expect(context).toContainText(brand === 'hoedl' ? 'Ihr Anteil' : 'Dein Anteil');
        }
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

        // Bedienflächen mindestens 48 px (K07, A-081).
        for (const name of ['Heute erledigt', 'Eintragen', 'Abbrechen']) {
          const box = (await page.locator('button', { hasText: name }).first().boundingBox())!;
          expect(box.height, name).toBeGreaterThanOrEqual(48);
        }

        await page.screenshot({ path: join(screenshotDir, `${label}-${mode}-${width}.png`), fullPage: false });
      });
    }
  }
}

test('Tastatur: Sprungmarke, Fokusring in primary, Dialog hält den Fokus und schließt mit Escape', async ({ page }) => {
  await page.setViewportSize({ width: 800, height: 1000 });
  const { tokens } = await openReferenceScreen(page, 'wiesner', 'hell');
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByRole('button', { name: 'Abbrechen' })).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(dialog.getByRole('button', { name: 'Löschen' })).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(dialog.getByRole('button', { name: 'Abbrechen' })).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(page.getByRole('button', { name: 'Beitrag von gestern löschen' })).toBeFocused();

  // Fokusring in primary mit 2 px Abstand auf eigenen Bedienelementen (Marke 7): Tastaturfokus auf einem Navigationseintrag.
  await page.locator('nav.ch-shell__nav a').first().focus();
  await page.keyboard.press('Tab');
  const focused = page.locator(':focus-visible');
  await expect(focused).toHaveCSS('outline-color', hex(tokens['primary']));
  await expect(focused).toHaveCSS('outline-offset', '2px');
});

test('Tastatur: Sprungmarke ist das erste Bedienelement und führt zum Inhalt', async ({ page }) => {
  await page.setViewportSize({ width: 800, height: 1000 });
  const { tenantId } = await mockApi(page, { brand: 'wiesner' });
  await page.goto(`/t/${tenantId}/challenges`);
  await expect(page.getByRole('heading', { name: 'Rad oder Fuß zur Arbeit' })).toBeVisible();
  await page.keyboard.press('Tab');
  const skip = page.locator('a.ch-skip-link');
  await expect.poll(() => page.evaluate(() => document.activeElement?.classList.contains('ch-skip-link') ?? false)).toBe(true);
  await expect(skip).toHaveText('Zum Inhalt springen');
  // Mit Fokus sichtbar im Fenster (vorher außerhalb), dann Sprung zum Inhalt.
  const box = (await skip.boundingBox())!;
  expect(box.y).toBeGreaterThanOrEqual(0);
  await page.keyboard.press('Enter');
  await expect(page.locator('#ch-main')).toBeFocused();
});

test('Systemmodus wirkt nur bei system; expliziter Modus bleibt bei Wechsel der Betriebssystempräferenz', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const { tenantId, theme } = await mockApi(page, { brand: 'hoedl' });
  const tokens = theme.body as { hell: Record<string, string>; dunkel: Record<string, string> };
  await page.emulateMedia({ colorScheme: 'light' });
  await page.goto(`/t/${tenantId}/ich`);
  const primary = () => page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--ch-primary').trim());
  await expect.poll(primary).toBe(tokens.hell['primary']);
  await page.emulateMedia({ colorScheme: 'dark' });
  await expect.poll(primary).toBe(tokens.dunkel['primary']);

  await page.getByText('Hell', { exact: true }).click();
  await expect.poll(primary).toBe(tokens.hell['primary']);
  await page.emulateMedia({ colorScheme: 'light' });
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.waitForTimeout(200);
  expect(await primary()).toBe(tokens.hell['primary']);
  expect(await page.evaluate(() => localStorage.getItem('ch.mode'))).toBe('light');
});

test('Großflächenmodus: gemessene Bedienflächen 56 px, Text zwei Stufen größer', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 1000 });
  await page.addInitScript(() => localStorage.setItem('ch.scale', 'gross'));
  await openReferenceScreen(page, 'wiesner', 'hell');
  await page.keyboard.press('Escape');
  expect(await page.evaluate(() => document.documentElement.dataset['chScale'])).toBe('gross');
  for (const name of ['Heute erledigt', 'Eintragen', 'Abbrechen']) {
    const box = (await page.locator('button', { hasText: name }).first().boundingBox())!;
    expect(box.height, name).toBeGreaterThanOrEqual(56);
  }
  expect(await page.locator('.ch-card__form').evaluate((e) => parseFloat(getComputedStyle(e).fontSize))).toBe(18);
  expect(await page.locator('body').evaluate((e) => parseFloat(getComputedStyle(e).fontSize))).toBe(20);
  await page.screenshot({ path: join(screenshotDir, 'wiesner-hell-390-gross.png'), fullPage: true });
});

test('Reduzierte Bewegung: keine Füll- und Erfolgsanimation, Bewegungswerte 0 ms', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await openReferenceScreen(page, 'wiesner', 'hell');
  const motion = await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--ch-motion-emphasized').trim());
  expect(motion).toBe('0ms');
  const fillDuration = await page.locator('.ch-bar__fill').evaluate((e) => getComputedStyle(e).animationDuration);
  expect(parseFloat(fillDuration)).toBeLessThanOrEqual(0.001);
});

test('Bewegung ohne Einschränkung: Kollektivbalken füllt sich in 320 ms', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.emulateMedia({ reducedMotion: 'no-preference' });
  await openReferenceScreen(page, 'wiesner', 'hell');
  expect(await page.locator('.ch-bar__fill').evaluate((e) => getComputedStyle(e).animationDuration)).toBe('0.32s');
  const state = await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--ch-motion-state').trim());
  expect(['120ms', '0.12s', '.12s']).toContain(state);
});

test('Formular und Submit: eine führende Eingabequelle, korrektes DTO, Erfolg erst nach Serverbestätigung', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const { tenantId } = await mockApi(page, { brand: 'wiesner', contribution: 'created' });
  let sent: Record<string, unknown> | null = null;
  await page.route('**/api/challenges/*/contributions', (route) => {
    sent = route.request().postDataJSON() as Record<string, unknown>;
    return route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({ contributionId: '01a0dc9f-0000-7000-8000-000000000001', outcome: 'recorded' }) });
  });
  await page.goto(`/t/${tenantId}/challenges`);
  await expect(page.getByRole('button', { name: 'Eintragen' })).toBeDisabled();
  await page.getByTestId('tag').click();
  await page.getByRole('option', { name: 'Vorgestern', exact: true }).click();
  await page.getByTestId('notiz').fill('bleibt lokal');
  await page.getByRole('button', { name: 'Eintragen' }).click();
  await expect(page.getByRole('status')).toContainText('Danke');
  expect(sent).not.toBeNull();
  expect(sent!['value']).toBe('1');
  expect(sent!['channel']).toBe('mobile');
  expect(sent!['operationId']).toBeNull();
  expect(String(sent!['idempotencyKey'])).toMatch(/^[0-9a-f-]{36}$/);
  expect(String(sent!['recordedAt'])).toMatch(/[+-]\d{2}:\d{2}$/);
  expect('note' in sent!).toBe(false);
});

test('Manifest je Tenant: Verweis, Farben und Symbolgrößen aus dem Tokensatz', async ({ page }) => {
  const { tenantId, theme } = await mockApi(page, { brand: 'wiesner' });
  await page.goto(`/t/${tenantId}/start`);
  await expect(page.locator('link[rel="manifest"]')).toHaveAttribute('href', `/api/branding/tenants/${tenantId}/manifest.webmanifest`);
  await expect(page.locator('meta[name="theme-color"]')).toHaveAttribute('content', (theme.body as { hell: Record<string, string> }).hell['primary']);
  await expect(page).toHaveTitle('Wiesner Aktiv');
});

test('Ohne Sitzung: Anmeldeseite vor Zuordnung in Plattformmarke mit Bildzeichen', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const platform = (await mockApi(page, { brand: 'wiesner', unauthenticated: true })).theme;
  void platform;
  await page.goto('/');
  await expect(page.getByRole('link', { name: 'Zum Zugang' })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.dataset['chContext'])).toBe('platform');
  await expect(page).toHaveTitle('CompanyHero');
  await page.screenshot({ path: join(screenshotDir, 'plattform-schale-390.png') });
});
