// Ich (Marke 4.3, A-078, A-081): Darstellung system | hell | dunkel je Person, Großflächenmodus als persönliche Einstellung,
// Hinweis zur Installation mit Tenant-Manifest (A-079), „Über“ mit der Plattform, Zugang (Wege, Wiederherstellungscode,
// Kiosk-Kennung, Sitzungen, Austritt; Zugang 4, 6.2, 8). Standardbedienelemente aus Material.
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonToggle, MatButtonToggleGroup } from '@angular/material/button-toggle';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { TextService } from '../../../../libs/theme/text.service';
import { ThemeService, type ThemeMode } from '../../../../libs/theme/theme.service';
import { FortschrittEinstellungen } from './fortschritt-einstellungen';
import { ZugangEinstellungen } from './zugang-einstellungen';

@Component({
  selector: 'ch-ich-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonToggleGroup, MatButtonToggle, MatSlideToggle, ZugangEinstellungen, FortschrittEinstellungen],
  template: `
    <section class="ch-ich">
      <div class="ch-ich__block">
        <h2 class="ch-ich__title" id="ch-ich-darstellung">{{ texts.t('ich.darstellung') }}</h2>
        <mat-button-toggle-group [value]="theme.mode()" (change)="setMode($event.value)" aria-labelledby="ch-ich-darstellung">
          <mat-button-toggle value="system">{{ texts.t('ich.modus.system') }}</mat-button-toggle>
          <mat-button-toggle value="light">{{ texts.t('ich.modus.hell') }}</mat-button-toggle>
          <mat-button-toggle value="dark">{{ texts.t('ich.modus.dunkel') }}</mat-button-toggle>
        </mat-button-toggle-group>
      </div>
      <div class="ch-ich__block">
        <mat-slide-toggle [checked]="theme.scale() === 'gross'" (change)="theme.setScale($event.checked ? 'gross' : 'standard')">{{ texts.t('ich.grossflaeche') }}</mat-slide-toggle>
        <p class="ch-ich__hint">{{ texts.t('ich.grossflaecheHinweis') }}</p>
      </div>
      <ch-fortschritt-einstellungen class="ch-ich__block ch-ich__block--voll" />
      <ch-zugang-einstellungen class="ch-ich__block ch-ich__block--voll" />
      <div class="ch-ich__block">
        <h2 class="ch-ich__title">{{ texts.t('ich.installieren') }}</h2>
        <p class="ch-ich__hint">{{ texts.t('ich.installierenHinweis') }}</p>
      </div>
      <div class="ch-ich__block">
        <h2 class="ch-ich__title">{{ texts.t('ich.ueber') }}</h2>
        <p class="ch-ich__hint">{{ texts.t('ich.ueberText') }}</p>
      </div>
    </section>
  `,
  styles: `
    .ch-ich {
      display: flex;
      flex-direction: column;
      gap: var(--ch-space-6);
      padding-top: var(--ch-space-4);
    }

    .ch-ich__block {
      display: flex;
      flex-direction: column;
      gap: var(--ch-space-3);
      align-items: flex-start;
    }

    .ch-ich__block--voll {
      align-items: stretch;
    }

    .ch-ich__title {
      font: 600 var(--ch-type-title-m) / var(--ch-line-title-m) var(--ch-font-text);
    }

    .ch-ich__hint {
      color: var(--ch-on-surface-variant);
    }
  `,
})
export class IchPage {
  protected readonly texts = inject(TextService);
  protected readonly theme = inject(ThemeService);

  setMode(value: unknown): void {
    if (value === 'system' || value === 'light' || value === 'dark') {
      this.theme.setMode(value as ThemeMode);
    }
  }
}
