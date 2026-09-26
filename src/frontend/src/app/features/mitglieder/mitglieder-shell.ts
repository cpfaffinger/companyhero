// Feature-Einstieg der Mitglieder-App (A-002, K17): lädt das Theme des Tenants einmal, prüft den Tenant-Kennzeichner der
// URL gegen die Sitzung (der Pfad benennt den Tenant, ersetzt aber keine Berechtigungsprüfung, Backend 5.1) und rahmt die
// Seiten mit der Schale. Ohne Sitzung erscheint ein Leerzustand mit dem Weg zum Zugang; nichts wird geraten.
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterOutlet } from '@angular/router';
import { AppShell } from '../../../libs/ui/app-shell/app-shell';
import { EmptyState } from '../../../libs/ui/empty-state/empty-state';
import { BrandingApi } from '../../../libs/data-access/branding/branding.api';
import { TextService } from '../../../libs/theme/text.service';
import { ThemeService } from '../../../libs/theme/theme.service';

export const TENANT_PLACEHOLDER = '_';

@Component({
  selector: 'ch-mitglieder-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, AppShell, EmptyState],
  template: `
    @if (state() === 'ready') {
      <ch-app-shell [base]="base()" [title]="title()" [hasContext]="hasContext()">
        <router-outlet />
        <ng-container chContext><router-outlet name="kontext" /></ng-container>
      </ch-app-shell>
    } @else if (state() === 'unauthenticated') {
      <div class="ch-shell-fallback">
        <ch-empty-state [text]="texts.t('zugang.noetig')">
          <a href="/zugang">{{ texts.t('zugang.link') }}</a>
        </ch-empty-state>
      </div>
    }
  `,
  styles: `
    .ch-shell-fallback {
      padding: var(--ch-space-4);
      max-width: 480px;
    }
  `,
})
export class MitgliederShell {
  private readonly branding = inject(BrandingApi);
  private readonly theme = inject(ThemeService);
  protected readonly texts = inject(TextService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly state = signal<'loading' | 'ready' | 'unauthenticated'>('loading');
  readonly tenantId = signal<string>(TENANT_PLACEHOLDER);
  readonly base = computed(() => `/t/${this.tenantId()}`);
  readonly title = signal('');
  readonly hasContext = signal(false);

  constructor() {
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
    const key = child?.snapshot.data['titleKey'] as string | undefined;
    this.title.set(key ? this.texts.t(key) : '');
    this.hasContext.set(Boolean(child?.snapshot.data['kontext']));
  }
}
