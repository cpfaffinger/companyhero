// Zugang im Browser (Zugang 3, 6; A-007, A-015, A-018): Anmeldeseite in der Plattformmarke, Passkey mit virtuellem Authenticator
// im WebAuthn-JSON-Format, CSRF-Header aus dem lesbaren Cookie, Kiosk mit Gerätesitzung, Personensitzung mit Countdown,
// Übertragung als QR-Code und Personenwechsel ohne Rest.
import { expect, test } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { mockApi, sample } from '../api-mock';

const screenshotDir = process.env['CH_SCREENSHOT_DIR'] ?? join(process.cwd(), '..', '..', 'durchstich', 'abnahme', 'laeufe', 'stufe-4');
mkdirSync(screenshotDir, { recursive: true });

test('Beitritt und Anmeldung 390px: Plattformmarke, Passkey-Registrierung und -Anmeldung mit virtuellem Authenticator als WebAuthn-JSON mit CSRF-Header, keine Namensliste', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.emulateMedia({ colorScheme: 'light' });
  await mockApi(page, { brand: 'standard', unauthenticated: true });
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('WebAuthn.enable');
  await cdp.send('WebAuthn.addVirtualAuthenticator', {
    options: { protocol: 'ctap2', transport: 'internal', hasResidentKey: true, hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true },
  });
  const posts: { path: string; body: unknown; csrf: string | null }[] = [];
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes('/api/')) {
      posts.push({ path: new URL(request.url()).pathname, body: request.postDataJSON() as unknown, csrf: request.headers()['x-csrf-token'] ?? null });
    }
  });

  // Beitritt (Zugang 2.2): QR-Link /join/<Code> führt in den Beitritt; Tenant-Vorschau, Anzeigename, ausdrückliche Sichtbarkeit,
  // Passkey-Registrierung am Gerät; danach Kennung und Wiederherstellungscode genau einmal.
  await page.goto('/join/ABCDEFGH');
  await expect(page).toHaveURL(/\/zugang\/beitritt\/ABCDEFGH$/);
  await expect(page.getByTestId('tenant')).toContainText('Wiesner Probe');
  await page.getByTestId('anzeigename').fill('Anna');
  await page.getByTestId('beitreten').click();
  await expect(page.getByRole('alert')).toContainText('Sichtbarkeit');
  await page.getByTestId('sichtbarkeit-team').click();
  await page.getByTestId('beitreten').click();
  await expect(page.getByTestId('ergebnis')).toBeVisible();
  await expect(page.getByTestId('kiosk-kennung')).toHaveText(/^\d{6}$/);
  await expect(page.getByTestId('wiederherstellungscode')).toHaveText(/^[A-Z2-9]{3}-[A-Z2-9]{3}-[A-Z2-9]{3}-[A-Z2-9]{3}$/);
  await page.screenshot({ path: join(screenshotDir, 'zugang-beitritt-ergebnis-390.png') });
  const joined = posts.find((p) => p.path === '/api/join/ABCDEFGH')!;
  const joinBody = joined.body as { displayName: string; visibility: string; useExternal: boolean; kioskPin: null; email: null; passkey: { state: string; credential: { type: string; rawId: string; response: { attestationObject: string; clientDataJSON: string } } } };
  expect(joinBody.displayName).toBe('Anna');
  expect(joinBody.visibility).toBe('team');
  expect(joinBody.useExternal).toBe(false);
  expect(joinBody.kioskPin).toBeNull();
  expect(joinBody.passkey.state).toBe('probe-state');
  expect(joinBody.passkey.credential.type).toBe('public-key');
  expect(joinBody.passkey.credential.response.attestationObject).toMatch(/^[A-Za-z0-9_-]+$/);
  expect(joined.csrf).toBe('probe-csrf-token');

  await page.goto('/zugang?zurueck=%2Ft%2F_%2Fchallenges');
  await expect(page.getByRole('heading', { name: 'Anmelden' })).toBeVisible();
  // Plattformmarke vor der Zuordnung (A-078): Bildzeichen statt Tenant-Avatar, kein Namensfeld, keine Namensliste.
  expect(await page.locator('input[autocomplete="username"], input[name="name"]').count()).toBe(0);
  expect(await page.getByRole('listbox').count()).toBe(0);
  await expect(page.getByTestId('anbieter-google')).toHaveAttribute('href', /\/api\/auth\/oidc\/google\/start\?intent=login&returnUrl=/);
  await page.screenshot({ path: join(screenshotDir, 'zugang-anmelden-390.png') });

  // Magic-Link: E-Mail-Adresse geht mit CSRF-Header an die API; die Oberfläche bestätigt ohne Preisgabe, ob die Adresse existiert.
  await page.getByTestId('email').fill('anna@example.org');
  await page.getByRole('button', { name: 'Link senden' }).click();
  await expect(page.getByText('Wenn die Adresse hinterlegt ist', { exact: false })).toBeVisible();

  // Anmeldung mit dem eben registrierten Passkey: Optionen des Backends → Authenticator → Antwort im WebAuthn-JSON-Format.
  await page.getByTestId('passkey').click();
  await expect(page).toHaveURL(/\/t\/_\/challenges|\/t\/[0-9a-f-]+\/challenges/);

  const magic = posts.find((p) => p.path === '/api/auth/magic-link')!;
  expect(magic.body).toEqual({ email: 'anna@example.org' });
  expect(magic.csrf).toBe('probe-csrf-token');
  const passkey = posts.find((p) => p.path === '/api/auth/passkey')!;
  const answer = passkey.body as { state: string; credential: { id: string; rawId: string; type: string; response: { clientDataJSON: string; authenticatorData: string; signature: string } } };
  expect(answer.state).toBe('probe-state');
  expect(answer.credential.type).toBe('public-key');
  expect(answer.credential.rawId).toMatch(/^[A-Za-z0-9_-]+$/);
  expect(answer.credential.response.clientDataJSON).toMatch(/^[A-Za-z0-9_-]+$/);
  expect(answer.credential.response.signature).toMatch(/^[A-Za-z0-9_-]+$/);
  expect(passkey.csrf).toBe('probe-csrf-token');
});

test('Kiosk: ohne Gerätesitzung nur Registrierung; mit Gerätesitzung Anmeldemaske, Personensitzung mit Countdown, Beitrag über Vorgangskennung, QR-Übertragung, Abmelden ohne Rest', async ({ page }) => {
  await page.setViewportSize({ width: 1024, height: 768 });
  await page.emulateMedia({ colorScheme: 'light' });

  await mockApi(page, { brand: 'wiesner', kiosk: 'unregistered' });
  await page.goto('/kiosk');
  await expect(page.getByRole('heading', { name: 'Gerät registrieren' })).toBeVisible();
  expect(await page.getByLabel('Kennung').count()).toBe(0);

  const posts: { path: string; body: unknown; csrf: string | null }[] = [];
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().includes('/api/')) {
      posts.push({ path: new URL(request.url()).pathname, body: request.postDataJSON() as unknown, csrf: request.headers()['x-csrf-token'] ?? null });
    }
  });
  await mockApi(page, { brand: 'wiesner', kiosk: 'device' });
  await page.goto('/kiosk');
  await expect(page.getByTestId('geraet')).toContainText('Eingang Halle 2');
  await expect(page.getByLabel('Kennung')).toBeVisible();

  // Falsche PIN: gleiche Antwort wie unbekannte Kennung, keine Information über Personen.
  const kennung = (sample('kiosk-join').body as { kioskId: string }).kioskId;
  for (const digit of kennung) {
    await page.getByRole('button', { name: digit, exact: true }).click();
  }
  for (const digit of '0000') {
    await page.getByRole('button', { name: digit, exact: true }).click();
  }
  await page.getByRole('button', { name: 'Weiter' }).click();
  await expect(page.getByTestId('fehler')).toContainText('Kennung oder PIN passen nicht');

  // Richtige PIN: Personensitzung mit sichtbarem Countdown (Zugang 6.5).
  await page.reload();
  await expect(page.getByLabel('Kennung')).toBeVisible();
  for (const digit of kennung) {
    await page.getByRole('button', { name: digit, exact: true }).click();
  }
  for (const digit of '7391') {
    await page.getByRole('button', { name: digit, exact: true }).click();
  }
  await page.getByRole('button', { name: 'Weiter' }).click();
  await expect(page.getByTestId('personensitzung')).toBeVisible();
  await expect(page.getByTestId('countdown')).toContainText(/Noch (60|59|58) s/);
  const login = posts.filter((p) => p.path === '/api/kiosk/login').at(-1)!;
  expect(login.body).toEqual({ kioskId: kennung, pin: '7391' });
  expect(login.csrf).toBe('probe-csrf-token');
  await page.screenshot({ path: join(screenshotDir, 'kiosk-personensitzung-1024.png') });

  // Beitrag am Kiosk: Vorgangskennung reservieren, dann Beitrag mit Kanal kiosk und operationId (A-005).
  await page.getByTestId(/^beitrag-/).first().click();
  await expect(page.getByTestId('hinweis')).toContainText('Beitrag erfasst');
  const op = posts.find((p) => p.path.endsWith('/contribution-operations'))!;
  expect(op.csrf).toBe('probe-csrf-token');
  const contribution = posts.find((p) => p.path.endsWith('/contributions'))!.body as { channel: string; operationId: string; idempotencyKey: null; value: string };
  expect(contribution.channel).toBe('kiosk');
  expect(contribution.operationId).toBe('01a0de00-0000-7000-8000-000000000001');
  expect(contribution.idempotencyKey).toBeNull();
  expect(contribution.value).toBe('1');

  // Übertragung: QR-Code mit dem einmaligen Link, fünf Minuten.
  await page.getByTestId('uebertragen').click();
  await expect(page.getByTestId('transfer').locator('img')).toBeVisible();
  await expect(page.getByTestId('transfer')).toContainText(/noch (300|299|298) s/);
  await page.screenshot({ path: join(screenshotDir, 'kiosk-uebertragung-1024.png') });

  // Abmelden: Anzeige geleert, Maske leer, kein Rest der Person (A-005).
  await page.getByTestId('abmelden').click();
  await expect(page.getByLabel('Kennung')).toHaveValue('');
  expect(await page.getByText('Probe Kiosk').count()).toBe(0);
  expect(posts.some((p) => p.path === '/api/auth/logout' && p.csrf === 'probe-csrf-token')).toBe(true);
});
