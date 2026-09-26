// Kiosk-Anmeldemaske R10 (Marke 2.5, 7; Zugang 6.2; A-018, A-081): Kennung, PIN, Ziffernblock, immer Großflächenmodus.
import { expect, test } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { mockApi } from '../api-mock';

const screenshotDir = process.env['CH_SCREENSHOT_DIR'] ?? join(process.cwd(), '..', '..', 'durchstich', 'abnahme', 'laeufe', 'stufe-5');
mkdirSync(screenshotDir, { recursive: true });

test('Kiosk 1024px: Großflächenmodus erzwungen, Ziffernblock mit 72-px-Tasten, keine Namensliste', async ({ page }) => {
  await page.setViewportSize({ width: 1024, height: 768 });
  await page.emulateMedia({ colorScheme: 'light' });
  await mockApi(page, { brand: 'wiesner' });
  await page.goto('/kiosk');
  await expect(page.getByRole('heading', { name: 'Kiosk' })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.dataset['chScale'])).toBe('gross');
  for (const key of ['1', '2', '3', '0']) {
    const box = (await page.getByRole('button', { name: key, exact: true }).boundingBox())!;
    expect(box.height).toBeGreaterThanOrEqual(56);
  }
  await page.getByRole('button', { name: '4', exact: true }).click();
  await page.getByRole('button', { name: '8', exact: true }).click();
  await expect(page.getByLabel('Kennung')).toHaveValue('48');
  expect(await page.getByRole('listbox').count()).toBe(0);
  await expect(page.getByRole('button', { name: 'Weiter' })).toBeDisabled();
  await page.screenshot({ path: join(screenshotDir, 'kiosk-wiesner-1024.png') });
});
