import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { LAYOUT, layoutTier } from './layout';

describe('Layoutgrenzen (K07): eine Definition für CSS und TypeScript', () => {
  it('SCSS und TypeScript nennen dieselben Werte', () => {
    const scss = readFileSync(join(process.cwd(), 'src', 'libs', 'theme', '_design-system.scss'), 'utf8');
    const value = (name: string) => Number(scss.match(new RegExp(`\\$${name}:\\s*(\\d+)px;`))?.[1]);
    expect(value('ch-breakpoint-tablet')).toBe(LAYOUT.tabletMinWidth);
    expect(value('ch-breakpoint-desktop')).toBe(LAYOUT.desktopMinWidth);
    expect(value('ch-content-max')).toBe(LAYOUT.contentMaxWidth);
    expect(value('ch-context-column')).toBe(LAYOUT.contextColumnWidth);
    expect(value('ch-side-nav')).toBe(LAYOUT.sideNavWidth);
    expect(value('ch-rail')).toBe(LAYOUT.railWidth);
  });

  it('ordnet Breiten den Stufen mobil, Tablet, Desktop zu', () => {
    expect(layoutTier(390)).toBe('mobile');
    expect(layoutTier(639)).toBe('mobile');
    expect(layoutTier(640)).toBe('tablet');
    expect(layoutTier(800)).toBe('tablet');
    expect(layoutTier(1023)).toBe('tablet');
    expect(layoutTier(1024)).toBe('desktop');
    expect(layoutTier(1280)).toBe('desktop');
  });
});
