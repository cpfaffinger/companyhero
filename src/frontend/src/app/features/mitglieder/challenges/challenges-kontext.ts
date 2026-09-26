// Kontextspalte am Desktop (Marke 2.4, K07): eigener Anteil mit Kennzahlen aus vorhandenen Daten, Sichtbarkeitshinweis.
// Verwendet denselben Datenzugriff wie die Seite; kein zweiter Request (K11). Keine Nullwerte, keine Vergleiche mit anderen.
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ChallengesApi } from '../../../../libs/data-access/challenges/challenges.api';
import { TextService } from '../../../../libs/theme/text.service';
import { Kennzahl } from '../../../../libs/ui/kennzahl/kennzahl';

@Component({
  selector: 'ch-challenges-kontext',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Kennzahl],
  template: `
    @if (card(); as card) {
      <div class="ch-kontext__label">{{ texts.t('kontext.deinAnteil') }}</div>
      <div class="ch-kontext__grid">
        <ch-kennzahl [wert]="card.percent + ' %'" [label]="texts.t('challenge.firmenziel')" />
        <ch-kennzahl [wert]="daysLeft()" [label]="texts.t('challenge.nochTage', { tage: '' }).trim()" />
      </div>
      <p class="ch-kontext__today">
        {{ texts.t('challenge.beitragHeute') }}: <strong>{{ card.contributedToday ? texts.t('challenge.erledigt') : texts.t('challenge.offen') }}</strong>
      </p>
      <p class="ch-kontext__hint">{{ texts.t('kontext.sichtbarkeit') }}</p>
    }
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--ch-space-4);
    }

    .ch-kontext__label {
      font-size: var(--ch-type-label-m);
      font-weight: 600;
      letter-spacing: 0.06em;
      text-transform: uppercase;
      color: var(--ch-on-surface-variant);
    }

    .ch-kontext__grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--ch-space-2);
    }

    .ch-kontext__today {
      font-size: var(--ch-type-body-m);
      line-height: var(--ch-line-body-m);
      color: var(--ch-on-surface-variant);
    }

    .ch-kontext__today strong {
      color: var(--ch-on-surface);
    }

    .ch-kontext__hint {
      font-size: var(--ch-type-label-m);
      line-height: var(--ch-line-label-m);
      color: var(--ch-on-surface-variant);
    }
  `,
})
export class ChallengesKontext {
  protected readonly texts = inject(TextService);
  private readonly challenges = inject(ChallengesApi);

  readonly card = computed(() => this.challenges.running()?.[0] ?? null);
  readonly daysLeft = computed(() => {
    const card = this.card();
    if (!card) {
      return '';
    }
    return String(Math.max(0, Math.ceil((new Date(card.endsAt).getTime() - Date.now()) / 86_400_000)));
  });
}
