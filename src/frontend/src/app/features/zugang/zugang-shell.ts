// Schale des Zugangs (A-078): Plattformmarke vor der Tenant-Zuordnung, Texte aus dem öffentlichen Plattformkatalog.
import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterOutlet } from '@angular/router';
import { BrandingApi } from '../../../libs/data-access/branding/branding.api';
import { ThemeService } from '../../../libs/theme/theme.service';

@Component({
  selector: 'ch-zugang-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet],
  template: `
    <div class="ch-zugang">
      <header class="ch-zugang__brand">
        <svg width="34" height="24" viewBox="0 0 28 20" aria-hidden="true">
          <path d="M2 18 A16 16 0 0 1 8 8" fill="none" stroke="var(--ch-primary)" stroke-width="4" stroke-linecap="round" />
          <path d="M10 6.5 A16 16 0 0 1 18 4" fill="none" stroke="var(--ch-primary)" stroke-width="4" stroke-linecap="round" />
          <path d="M20.5 4.5 A16 16 0 0 1 26 10" fill="none" stroke="var(--ch-progress)" stroke-width="4" stroke-linecap="round" />
        </svg>
        <span class="ch-zugang__name">{{ theme.produktname() }}</span>
      </header>
      <main class="ch-zugang__main" id="ch-main">
        <router-outlet />
      </main>
    </div>
  `,
  styles: `
    .ch-zugang {
      display: flex;
      flex-direction: column;
      gap: var(--ch-space-4);
      padding: var(--ch-space-4);
      max-width: 560px;
      margin: 0 auto;
    }

    .ch-zugang__brand {
      display: flex;
      align-items: center;
      gap: var(--ch-space-3);
      padding: var(--ch-space-3) 0;
    }

    .ch-zugang__name {
      font: 700 var(--ch-type-title-m) / var(--ch-line-title-m) var(--ch-font-display);
      color: var(--ch-primary);
    }

    .ch-zugang__main {
      display: flex;
      flex-direction: column;
      gap: var(--ch-space-6);
    }
  `,
})
export class ZugangShell {
  protected readonly theme = inject(ThemeService);
  private readonly branding = inject(BrandingApi);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    this.theme.context.set('platform');
    this.theme.forcedScale.set(null);
    this.branding.platformTheme().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => undefined });
  }
}
