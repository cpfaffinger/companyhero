// A-009 Nachweis: echte API-Antworten (Vertragsproben aus e2e/fixtures/api, aufgenommen vom Integrationstest
// ContractSamplesTests gegen Testcontainer-PostgreSQL) müssen dem generierten Client entsprechen. Die Proben werden mit
// den Typen des generierten Vertrags gelesen; die Laufzeitprüfungen belegen K13: Dezimalwerte als Strings mit vier
// Nachkommastellen, Kennungen als Strings, Zeitpunkte mit Offset, Null erhalten, Aufzählungen als benannte Werte.
// Struktur- und Typabgleich Probe ↔ laufende API liegt im Backend (ContractSamplesTests); JSON-Importe weiten Literale zu
// string, deshalb prüft dieser Test die Aufzählungswerte zur Laufzeit statt über die Zuweisung.
import type { components } from './generated/api';
import challenges from '../../../e2e/fixtures/api/challenges.json';
import collective from '../../../e2e/fixtures/api/collective.json';
import contributionCreated from '../../../e2e/fixtures/api/contribution-created.json';
import contributionRejected from '../../../e2e/fixtures/api/contribution-rejected.json';
import manifest from '../../../e2e/fixtures/api/manifest-wiesner.json';
import platform from '../../../e2e/fixtures/api/platform.json';
import themeHoedl from '../../../e2e/fixtures/api/theme-hoedl.json';
import themeRejected from '../../../e2e/fixtures/api/theme-rejected.json';
import themeWiesner from '../../../e2e/fixtures/api/theme-wiesner.json';

type Schemas = components['schemas'];

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;
const decimal4 = /^-?\d+\.\d{4}$/;
const offsetDateTime = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(\+\d{2}:\d{2}|Z)$/;
const hex = /^#[0-9A-F]{6}$/;

/** Liest eine Probe als Vertragstyp, nachdem alle Pflichtfelder des Typs vorhanden sind. */
function sample<T extends object>(body: unknown, requiredKeys: readonly (keyof T & string)[]): T {
  expect(body).toBeTypeOf('object');
  for (const key of requiredKeys) {
    expect(body, `Pflichtfeld ${key} fehlt`).toHaveProperty(key);
  }
  return body as T;
}

const themeKeys = ['tenantId', 'version', 'verfahren', 'schema', 'produktname', 'anrede', 'tonalitaet', 'bezeichnungen', 'willkommenstext', 'hell', 'dunkel', 'ersetzungen', 'texte', 'manifestPfad', 'veroeffentlichtAm'] as const;
const cardKeys = ['challengeId', 'title', 'metric', 'target', 'startsAt', 'endsAt', 'collective', 'percent', 'contributedToday'] as const;

describe('Vertragsproben gegen den generierten Client (A-009, K13)', () => {
  it('Theme-Antworten entsprechen ThemeResponse: Tokens je Modus, Anrede, Texte, Manifestpfad', () => {
    const wiesner = sample<Schemas['ThemeResponse']>(themeWiesner.body, themeKeys);
    const hoedl = sample<Schemas['ThemeResponse']>(themeHoedl.body, themeKeys);
    expect(themeWiesner.status).toBe(200);
    expect(wiesner.tenantId).toMatch(uuid);
    expect(['du', 'sie']).toContain(wiesner.anrede);
    expect(['sachlich', 'freundlich', 'motivierend']).toContain(wiesner.tonalitaet);
    expect(wiesner.anrede).toBe('du');
    expect(hoedl.anrede).toBe('sie');
    expect(wiesner.texte['challenge.beitragHeute']).toBe('Dein Beitrag heute');
    expect(hoedl.texte['challenge.beitragHeute']).toBe('Ihr Beitrag heute');
    // Die Saatfarben selbst sind Backend-Nachweise (ThemeDerivationTests); hier zählt der Vertrag: zwei Marken, zwei Tokensätze.
    expect(wiesner.hell['primary']).not.toBe(hoedl.hell['primary']);
    expect(wiesner.hell['primary']).not.toBe(wiesner.dunkel['primary']);
    for (const theme of [wiesner, hoedl]) {
      for (const mode of [theme.hell, theme.dunkel]) {
        for (const role of ['primary', 'on-primary', 'progress', 'progress-container', 'surface', 'on-surface', 'outline', 'team', 'error', 'chart-6']) {
          expect(mode[role], role).toMatch(hex);
        }
      }
      expect(theme.manifestPfad).toBe(`/api/branding/tenants/${theme.tenantId}/manifest.webmanifest`);
      expect(typeof theme.version).toBe('number');
      expect(theme.veroeffentlichtAm).toMatch(offsetDateTime);
      for (const e of theme.ersetzungen) {
        expect(['hell', 'dunkel']).toContain(e.modus);
        expect(e.grund.length).toBeGreaterThan(0);
      }
    }
    expect(hoedl.willkommenstext).toBeNull();
  });

  it('Plattformtheme und Manifest entsprechen dem Vertrag', () => {
    const p = sample<Schemas['PlatformThemeResponse']>(platform.body, ['plattformname', 'verfahren', 'hell', 'dunkel', 'texte']);
    expect(p.plattformname).toBe('CompanyHero');
    expect(p.hell['primary']).toMatch(hex);
    expect(p.hell['primary']).not.toBe(themeWiesner.body.hell['primary']);
    const m = sample<Schemas['WebManifest']>(manifest.body, ['id', 'name', 'short_name', 'start_url', 'scope', 'display', 'lang', 'theme_color', 'background_color', 'icons']);
    expect(manifest.contentType).toBe('application/manifest+json');
    expect(m.display).toBe('standalone');
    expect(m.start_url).toBe(`${m.scope}start`);
    expect(m.icons.some((i) => i.purpose === 'maskable')).toBe(true);
    expect(m.theme_color).toBe(themeWiesner.body.hell['primary']);
  });

  it('Challenge-Karte und Kollektivstand: Dezimalwerte als Strings, Prozent als Ganzzahl, Zeitpunkte mit Offset', () => {
    expect(challenges.body.length).toBeGreaterThan(0);
    const card = sample<Schemas['ChallengeCardResponse']>(challenges.body[0], cardKeys);
    expect(card.challengeId).toMatch(uuid);
    expect(['checkmark', 'count']).toContain(card.metric);
    expect(card.target).toMatch(decimal4);
    expect(typeof card.target).toBe('string');
    expect(card.startsAt).toMatch(offsetDateTime);
    expect(card.endsAt).toMatch(offsetDateTime);
    expect(Number.isInteger(card.percent)).toBe(true);
    expect(typeof card.contributedToday).toBe('boolean');
    // Kollektivwerte erscheinen erst ab fünf Beitragenden (A-023): davor null, danach Dezimalstring und Ganzzahl.
    expect(card.collective?.total === null || decimal4.test(card.collective?.total ?? '')).toBe(true);

    const c = sample<Schemas['CollectiveResponse']>(collective.body, ['total', 'contributionCount', 'contributorCount', 'updatedAt', 'percent']);
    expect(c.total === null || decimal4.test(c.total)).toBe(true);
    expect(c.contributionCount === null || Number.isInteger(c.contributionCount)).toBe(true);
    expect(Number.isInteger(c.percent)).toBe(true);
    expect(c.updatedAt).toMatch(offsetDateTime);
  });

  it('Beitrag: Erfolg mit Kennung, Ablehnung als Problem Details mit stabiler Fehlerkennung', () => {
    const created = sample<Schemas['ContributionResponse']>(contributionCreated.body, ['contributionId', 'outcome']);
    expect(contributionCreated.status).toBe(201);
    expect(created.contributionId).toMatch(uuid);
    expect(created.outcome).toBe('recorded');

    const rejected = sample<Schemas['ProblemDetails']>(contributionRejected.body, ['status', 'title', 'detail']);
    expect(contributionRejected.status).toBe(422);
    expect(rejected.detail).toBe('InFuture');

    const themeProblem = sample<Schemas['HttpValidationProblemDetails']>(themeRejected.body, ['errors']);
    expect(themeRejected.status).toBe(400);
    expect(themeProblem.errors['theme']?.[0]).toContain('Saatfarbe');
  });
});
