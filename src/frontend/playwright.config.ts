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
  timeout: 60_000,
  expect: { timeout: 15_000 },
  use: {
    // localhost statt 127.0.0.1: WebAuthn verlangt eine Domain als Relying-Party-ID (Zugang 3.1), keine IP-Adresse.
    baseURL: `http://localhost:${port}`,
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
  // Lokale Umgebungen mit vorinstalliertem Chromium (CH_CHROMIUM=/pfad/zu/chrome) statt Download; CI lädt den Browser selbst.
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], launchOptions: process.env['CH_CHROMIUM'] ? { executablePath: process.env['CH_CHROMIUM'] } : {} } }],
});
