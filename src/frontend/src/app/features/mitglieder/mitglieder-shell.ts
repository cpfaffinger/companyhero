// Feature-Einstieg der Mitglieder-App (A-002, K17): lädt das Theme des Tenants einmal, prüft den Tenant-Kennzeichner der
// URL gegen die Sitzung (der Pfad benennt den Tenant, ersetzt aber keine Berechtigungsprüfung, Backend 5.1) und rahmt die
// Seiten mit der Schale. Ohne Sitzung erscheint ein Leerzustand mit dem Weg zum Zugang; nichts wird geraten. Endet die
// Sitzung während der Nutzung (401, Zugang 5), führt die Schale ebenfalls zum Zugang (K11).
import { NgComponentOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, signal, type Type } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterOutlet } from '@angular/router';
import { AppShell } from '../../../libs/ui/app-shell/app-shell';
import { EmptyState } from '../../../libs/ui/empty-state/empty-state';
import { BrandingApi } from '../../../libs/data-access/branding/branding.api';
import { ApiClient } from '../../../libs/api-client/api-client';
import { SessionStore } from '../../../libs/data-access/zugang/session.store';
import { TextService } from '../../../libs/theme/text.service';
import { ThemeService } from '../../../libs/theme/theme.service';

export const TENANT_PLACEHOLDER = '_';

@Component({
  selector: 'ch-mitglieder-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, NgComponentOutlet, AppShell, EmptyState],
  template: `
    @if (state() === 'ready') {
      <ch-app-shell [base]="base()" [title]="title()" [hasContext]="kontext() !== null">
        <router-outlet />
        <ng-container chContext><ng-container *ngComponentOutlet="kontext()" /></ng-container>
      </ch-app-shell>
    } @else if (state() === 'unauthenticated') {
      <div class="ch-shell-fallback">
        <header class="ch-shell-fallback__brand">
          <svg width="34" height="24" viewBox="0 0 28 20" aria-hidden="true">
            <path d="M2 18 A16 16 0 0 1 8 8" fill="none" stroke="var(--ch-primary)" stroke-width="4" stroke-linecap="round" />
            <path d="M10 6.5 A16 16 0 0 1 18 4" fill="none" stroke="var(--ch-primary)" stroke-width="4" stroke-linecap="round" />
            <path d="M20.5 4.5 A16 16 0 0 1 26 10" fill="none" stroke="var(--ch-progress)" stroke-width="4" stroke-linecap="round" />
          </svg>
          <span class="ch-shell-fallback__name">{{ theme.produktname() }}</span>
        </header>
        <ch-empty-state [text]="texts.t('zugang.noetig')">
          <a [href]="zugangUrl()">{{ texts.t('zugang.link') }}</a>
        </ch-empty-state>
      </div>
    }
  `,
  styles: `
    .ch-shell-fallback {
      display: flex;
      flex-direction: column;
      gap: var(--ch-space-4);
      padding: var(--ch-space-4);
      max-width: 480px;
    }

    .ch-shell-fallback__brand {
      display: flex;
      align-items: center;
      gap: var(--ch-space-3);
      padding: var(--ch-space-3) 0;
    }

    .ch-shell-fallback__name {
      font: 700 var(--ch-type-title-m) / var(--ch-line-title-m) var(--ch-font-display);
      color: var(--ch-primary);
    }
  `,
})
export class MitgliederShell {
  private readonly branding = inject(BrandingApi);
  protected readonly theme = inject(ThemeService);
  protected readonly texts = inject(TextService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly sessions = inject(SessionStore);
  private readonly api = inject(ApiClient);

  readonly state = signal<'loading' | 'ready' | 'unauthenticated'>('loading');
  readonly tenantId = signal<string>(TENANT_PLACEHOLDER);
  readonly base = computed(() => `/t/${this.tenantId()}`);
  readonly titleKey = signal<string | null>(null);
  readonly title = computed(() => (this.titleKey() ? this.texts.t(this.titleKey()!) : ''));
  /** Kontextspalte am Desktop (K07): Komponente aus den Routendaten der aktiven Seite. */
  readonly kontext = signal<Type<unknown> | null>(null);
  /** Zurück zur aktuellen Seite nach der Anmeldung; nur lokale Pfade (Zugang 3.2). */
  readonly zugangUrl = computed(() => '/zugang?zurueck=' + encodeURIComponent(this.router.url.startsWith('/t/') ? this.router.url : '/t/_/start'));

  constructor() {
    effect(() => {
      if (this.sessions.ended() && this.state() === 'ready') {
        this.theme.context.set('platform');
        this.state.set('unauthenticated');
      }
    });
    this.theme.context.set('tenant');
    this.theme.forcedScale.set(null);
    this.branding
      .tenantTheme()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (theme) => {
          this.tenantId.set(theme.tenantId);
          const requested = this.route.snapshot.paramMap.get('tenant');
          if (requested !== theme.tenantId) {
            // Platzhalter oder fremder Kennzeichner: auf den Tenant der Sitzung umleiten, Rest des Pfads erhalten.
            const rest = this.route.snapshot.firstChild?.url.map((s) => s.path) ?? [];
            void this.router.navigate(['/t', theme.tenantId, ...rest], { replaceUrl: true });
          }
          this.state.set('ready');
          // Sitzungsdaten für Offline-Freigabe und Sitzungsart (Zugang 5, 7); der Zugang selbst bleibt ein eigener Lazy-Einstieg (K17).
          this.api.get('/api/auth/session').pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (session) => this.sessions.set(session), error: () => undefined });
        },
        error: () => {
          // Anmeldeseite vor Tenant-Zuordnung in Plattformmarke (A-078); Texte aus dem öffentlichen Plattformkatalog.
          this.theme.context.set('platform');
          this.branding.platformTheme().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => undefined });
          this.state.set('unauthenticated');
        },
      });
    this.router.events.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.updateTitle());
  }

  private updateTitle(): void {
    let child = this.route.firstChild;
    while (child?.firstChild) {
      child = child.firstChild;
    }
    this.titleKey.set((child?.snapshot.data['titleKey'] as string | undefined) ?? null);
    this.kontext.set((child?.snapshot.data['kontext'] as Type<unknown> | undefined) ?? null);
  }
}
