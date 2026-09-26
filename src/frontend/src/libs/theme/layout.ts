// K07: eine gemeinsame Definition der Layoutgrenzen für CSS und TypeScript. Die SCSS-Werte stehen in _design-system.scss;
// layout.spec.ts prüft, dass beide übereinstimmen. Layout bleibt CSS-Aufgabe; TypeScript fragt nur, wenn eine Struktur
// wechselt (untere Leiste, Rail, Seitennavigation).
export const LAYOUT = {
  tabletMinWidth: 640,
  desktopMinWidth: 1024,
  contentMaxWidth: 1280,
  contextColumnWidth: 320,
  sideNavWidth: 240,
  railWidth: 88,
} as const;

export type LayoutTier = 'mobile' | 'tablet' | 'desktop';

export function layoutTier(width: number): LayoutTier {
  if (width >= LAYOUT.desktopMinWidth) {
    return 'desktop';
  }
  return width >= LAYOUT.tabletMinWidth ? 'tablet' : 'mobile';
}
