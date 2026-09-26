import { TestBed } from '@angular/core/testing';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { TextService } from './text.service';
import { ThemeService, type TenantTheme } from './theme.service';
import themeWiesner from '../../../e2e/fixtures/api/theme-wiesner.json';
import themeHoedl from '../../../e2e/fixtures/api/theme-hoedl.json';

describe('TextService (A-080): Tonalität und Anrede des Tenants, Sperrliste', () => {
  beforeEach(() => TestBed.configureTestingModule({}));

  it('liefert Texte in der Anrede des Tenants und setzt Bezüge im Landesformat ein', () => {
    const theme = TestBed.inject(ThemeService);
    const texts = TestBed.inject(TextService);
    theme.tenantTheme.set(themeWiesner.body as TenantTheme);
    expect(texts.t('challenge.beitragHeute')).toBe('Dein Beitrag heute');
    expect(texts.t('challenge.nochTage', { tage: 9 })).toBe('noch 9 Tage');
    expect(texts.t('form.zeichen', { n: 1234, max: 200 })).toBe('1.234 von 200 Zeichen');
    expect(texts.t('gibt.es.nicht')).toBe('gibt.es.nicht');

    theme.tenantTheme.set(themeHoedl.body as TenantTheme);
    expect(texts.t('challenge.beitragHeute')).toBe('Ihr Beitrag heute');
    expect(texts.anrede()).toBe('sie');
  });

  it('jeder in der Oberfläche verwendete Schlüssel steht im Katalog, und kein Sperrbegriff steht in der Oberfläche', () => {
    const root = join(process.cwd(), 'src');
    const files = walk(root).filter((f) => (f.endsWith('.html') || f.endsWith('.ts')) && !f.endsWith('.spec.ts') && !f.includes('generated'));
    const used = new Set<string>();
    const blocked = ['Gesundheitsdaten', 'Tracking', 'Monitoring', 'Auswertung', 'Überwachung', 'Krankenstand', 'Fehlzeiten', 'Leistung', 'Ranking'];
    for (const file of files) {
      const source = readFileSync(file, 'utf8');
      for (const match of source.matchAll(/\bt\(\s*'([a-zA-Z0-9.]+)'/g)) {
        used.add(match[1]);
      }
      for (const term of blocked) {
        expect(source.includes(term), `${file} enthält den Sperrbegriff ${term}`).toBe(false);
      }
    }
    const catalog = themeWiesner.body.texte as Record<string, string>;
    const missing = [...used].filter((key) => !(key in catalog));
    expect(used.size).toBeGreaterThan(20);
    expect(missing).toEqual([]);
    for (const text of Object.values(catalog)) {
      for (const term of blocked) {
        expect(text.includes(term), `Katalogtext enthält ${term}: ${text}`).toBe(false);
      }
    }
  });
});

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const path = join(dir, entry);
    return statSync(path).isDirectory() ? walk(path) : [path];
  });
}
