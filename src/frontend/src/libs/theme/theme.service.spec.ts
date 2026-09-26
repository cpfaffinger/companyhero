import { TestBed } from '@angular/core/testing';
import { ThemeService, type PlatformTheme, type TenantTheme } from './theme.service';
import themeWiesner from '../../../e2e/fixtures/api/theme-wiesner.json';
import platform from '../../../e2e/fixtures/api/platform.json';

type MediaListener = (event: { matches: boolean }) => void;

describe('ThemeService (K05, A-013, A-079)', () => {
  let listeners: MediaListener[];
  let systemDark: boolean;

  beforeEach(() => {
    listeners = [];
    systemDark = false;
    localStorage.clear();
    window.matchMedia = ((query: string) => ({
      matches: systemDark,
      media: query,
      addEventListener: (_: string, listener: MediaListener) => listeners.push(listener),
      removeEventListener: () => undefined,
    })) as unknown as typeof window.matchMedia;
    document.head.innerHTML = '<meta name="theme-color" content=""><link rel="manifest">';
    document.documentElement.removeAttribute('style');
    TestBed.configureTestingModule({});
  });

  const wiesner = themeWiesner.body as TenantTheme;
  const plattform = platform.body as PlatformTheme;
  const cssVar = (role: string) => document.documentElement.style.getPropertyValue(`--ch-${role}`);

  it('schreibt den Backend-Tokensatz als --ch-* Rollen, color-scheme, Manifestverweis und Titel gemeinsam', () => {
    const service = TestBed.inject(ThemeService);
    service.tenantTheme.set(wiesner);
    TestBed.tick();

    expect(cssVar('primary')).toBe(wiesner.hell['primary']);
    expect(cssVar('progress')).toBe(wiesner.hell['progress']);
    expect(document.documentElement.style.colorScheme).toBe('light');
    expect(document.documentElement.dataset['chMode']).toBe('light');
    expect(document.querySelector<HTMLLinkElement>('link[rel="manifest"]')?.getAttribute('href')).toBe(wiesner.manifestPfad);
    expect(document.querySelector<HTMLMetaElement>('meta[name="theme-color"]')?.content).toBe(wiesner.hell['primary']);
    expect(document.title).toBe('Wiesner Aktiv');
  });

  it('folgt der Systempräferenz nur im Modus system', () => {
    const service = TestBed.inject(ThemeService);
    service.tenantTheme.set(wiesner);
    TestBed.tick();
    expect(service.effectiveMode()).toBe('light');

    listeners.forEach((l) => l({ matches: true }));
    TestBed.tick();
    expect(service.effectiveMode()).toBe('dark');
    expect(cssVar('primary')).toBe(wiesner.dunkel['primary']);
    expect(document.documentElement.style.colorScheme).toBe('dark');

    service.setMode('light');
    TestBed.tick();
    expect(service.effectiveMode()).toBe('light');
    listeners.forEach((l) => l({ matches: false }));
    listeners.forEach((l) => l({ matches: true }));
    TestBed.tick();
    expect(service.effectiveMode()).toBe('light');
    expect(cssVar('primary')).toBe(wiesner.hell['primary']);
    expect(localStorage.getItem('ch.mode')).toBe('light');
  });

  it('wechselt gemeinsam mit dem Markenkontext zur Plattform und zurück, ohne den Tenant zu vergessen', () => {
    const service = TestBed.inject(ThemeService);
    service.tenantTheme.set(wiesner);
    service.platformTheme.set(plattform);
    TestBed.tick();
    expect(cssVar('primary')).toBe(wiesner.hell['primary']);

    service.context.set('platform');
    TestBed.tick();
    expect(cssVar('primary')).toBe(plattform.hell['primary']);
    expect(cssVar('primary')).not.toBe(wiesner.hell['primary']);
    expect(document.documentElement.dataset['chContext']).toBe('platform');
    expect(document.querySelector<HTMLLinkElement>('link[rel="manifest"]')?.hasAttribute('href')).toBe(false);
    expect(document.title).toBe('CompanyHero');

    service.context.set('tenant');
    TestBed.tick();
    expect(cssVar('primary')).toBe(wiesner.hell['primary']);
    expect(service.tenantTheme()?.version).toBe(wiesner.version);
  });

  it('Großflächenmodus ist persönliche Einstellung, am Kiosk erzwungen', () => {
    const service = TestBed.inject(ThemeService);
    TestBed.tick();
    expect(document.documentElement.dataset['chScale']).toBe('standard');
    service.setScale('gross');
    TestBed.tick();
    expect(document.documentElement.dataset['chScale']).toBe('gross');
    expect(localStorage.getItem('ch.scale')).toBe('gross');
    service.setScale('standard');
    service.forcedScale.set('gross');
    TestBed.tick();
    expect(service.effectiveScale()).toBe('gross');
  });
});
