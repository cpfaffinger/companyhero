// Ende-zu-Ende-Tests (K19, Frontend 6) gegen den Produktionsbuild: ein statischer Server liefert dist/ mit App-Shell-Fallback,
// die API-Antworten kommen als Vertragsproben aus e2e/fixtures/api (echte Antworten der API, siehe ContractSamplesTests).
// Screenshots der manuellen Abnahme des Referenzscreens landen unter durchstich/abnahme/laeufe/stufe-5/ (CH_SCREENSHOT_DIR).
import { defineConfig, devices } from '@playwright/test';

const port = 4310;

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: process.env['CI'] ? 1 : 0,
  reporter: process.env['CI'] ? [['list'], ['html', { open: 'never', outputFolder: 'e2e-report' }]] : 'list',
  outputDir: 'e2e-results',
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    trace: 'retain-on-failure',
    locale: 'de-AT',
    timezoneId: 'Europe/Vienna',
  },
  webServer: {
    command: `node e2e/serve-dist.mjs ${port}`,
    port,
    reuseExistingServer: !process.env['CI'],
    timeout: 30_000,
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
