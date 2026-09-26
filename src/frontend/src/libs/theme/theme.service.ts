// Der eine Theme-Service (K05, A-078, A-079): verwaltet `system | light | dark`, den Markenkontext (Tenant oder Plattform)
// und den Großflächenmodus, und setzt daraus genau einen effektiven Modus. Tokens und color-scheme werden gemeinsam gesetzt;
// nur bei `system` wirkt die Betriebssystempräferenz. Der Tokensatz kommt aus dem Backend (A-013) und wird als --ch-* Rollen
// auf das Wurzelelement geschrieben; das Manifest je Tenant wird als Verweis nachgeführt. Keine Palettenableitung hier.
import { DOCUMENT } from '@angular/common';
import { computed, effect, inject, Injectable, signal } from '@angular/core';
import type { components } from '../api-client/generated/api';

export type ThemeMode = 'system' | 'light' | 'dark';
export type BrandContext = 'tenant' | 'platform';
export type Scale = 'standard' | 'gross';
export type TenantTheme = components['schemas']['ThemeResponse'];
export type PlatformTheme = components['schemas']['PlatformThemeResponse'];
export type TokenMap = Record<string, string>;

const MODE_KEY = 'ch.mode';
const SCALE_KEY = 'ch.scale';
const TOKEN_PREFIX = '--ch-';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly systemDark = signal(false);
  private readonly appliedRoles = new Set<string>();

  readonly mode = signal<ThemeMode>(readStored(MODE_KEY, ['system', 'light', 'dark']) ?? 'system');
  readonly scale = signal<Scale>(readStored(SCALE_KEY, ['standard', 'gross']) ?? 'standard');
  readonly context = signal<BrandContext>('tenant');
  /** Kiosk (A-081): immer Großflächenmodus, unabhängig von der persönlichen Einstellung. */
  readonly forcedScale = signal<Scale | null>(null);
  readonly tenantTheme = signal<TenantTheme | null>(null);
  readonly platformTheme = signal<PlatformTheme | null>(null);

  /** Genau ein effektiver Modus; die Systempräferenz wirkt nur bei `system` (K05). */
  readonly effectiveMode = computed<'light' | 'dark'>(() => {
    const mode = this.mode();
    return mode === 'system' ? (this.systemDark() ? 'dark' : 'light') : mode;
  });

  readonly effectiveScale = computed<Scale>(() => this.forcedScale() ?? this.scale());

  /** Der aktive Tokensatz: Tenant- oder Plattformmarke im effektiven Modus. */
  readonly tokens = computed<TokenMap | null>(() => {
    const source = this.context() === 'platform' ? this.platformTheme() : this.tenantTheme();
    if (!source) {
      return null;
    }
    return this.effectiveMode() === 'dark' ? source.dunkel : source.hell;
  });

  readonly produktname = computed(() => (this.context() === 'platform' ? this.platformTheme()?.plattformname : this.tenantTheme()?.produktname) ?? '');

  readonly texte = computed<Record<string, string>>(() => (this.context() === 'platform' ? this.platformTheme()?.texte : this.tenantTheme()?.texte) ?? {});

  readonly anrede = computed<'du' | 'sie'>(() => (this.context() === 'tenant' ? this.tenantTheme()?.anrede : undefined) ?? 'du');

  constructor() {
    const media = this.document.defaultView?.matchMedia?.('(prefers-color-scheme: dark)');
    if (media) {
      this.systemDark.set(media.matches);
      media.addEventListener('change', (event) => this.systemDark.set(event.matches));
    }
    effect(() => this.apply());
  }

  setMode(mode: ThemeMode): void {
    this.mode.set(mode);
    store(MODE_KEY, mode);
  }

  setScale(scale: Scale): void {
    this.scale.set(scale);
    store(SCALE_KEY, scale);
  }

  /** Schreibt Tokens, Modus, Kontext, Maßstab, Manifestverweis und Titel gemeinsam (Laufzeitwechsel ohne Neuladen). */
  private apply(): void {
    const root = this.document.documentElement;
    const tokens = this.tokens();
    const mode = this.effectiveMode();
    const context = this.context();
    const scale = this.effectiveScale();

    for (const role of this.appliedRoles) {
      if (!tokens || !(role in tokens)) {
        root.style.removeProperty(TOKEN_PREFIX + role);
        this.appliedRoles.delete(role);
      }
    }
    if (tokens) {
      for (const [role, value] of Object.entries(tokens)) {
        root.style.setProperty(TOKEN_PREFIX + role, value);
        this.appliedRoles.add(role);
      }
    }

    root.style.colorScheme = mode;
    root.dataset['chMode'] = mode;
    root.dataset['chContext'] = context;
    root.dataset['chScale'] = scale;

    const themeColor = this.document.querySelector<HTMLMetaElement>('meta[name="theme-color"]');
    if (themeColor && tokens?.['primary']) {
      themeColor.content = tokens['primary'];
    }

    const manifest = this.document.querySelector<HTMLLinkElement>('link[rel="manifest"]');
    const tenant = this.tenantTheme();
    if (manifest) {
      if (context === 'tenant' && tenant) {
        manifest.href = tenant.manifestPfad;
      } else {
        manifest.removeAttribute('href');
      }
    }

    const name = this.produktname();
    if (name) {
      this.document.title = name;
    }
  }
}

function readStored<T extends string>(key: string, allowed: readonly T[]): T | null {
  try {
    const value = globalThis.localStorage?.getItem(key);
    return allowed.includes(value as T) ? (value as T) : null;
  } catch {
    return null;
  }
}

function store(key: string, value: string): void {
  try {
    globalThis.localStorage?.setItem(key, value);
  } catch {
    // Speicher nicht verfügbar (privates Fenster): Einstellung gilt für die Sitzung.
  }
}
