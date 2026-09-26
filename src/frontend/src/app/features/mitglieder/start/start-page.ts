// Start (Feed 3, Onboarding 1): Kopfkarte mit Willkommenstext in Tonalität und Anrede, nächste Handlung, laufende
// Challenge als Karte. Keine Nullwerte, kein leeres Diagramm; der Feed selbst folgt mit Stufe 6.
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { ChallengesApi } from '../../../../libs/data-access/challenges/challenges.api';
import { TextService } from '../../../../libs/theme/text.service';
import { ThemeService } from '../../../../libs/theme/theme.service';
import { ChallengeCard } from '../../../../libs/ui/challenge-card/challenge-card';
import { EmptyState } from '../../../../libs/ui/empty-state/empty-state';

@Component({
  selector: 'ch-start-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChallengeCard, EmptyState],
  template: `
    <section class="ch-start">
      <div class="ch-start__kopf">
        <h2 class="ch-start__titel">{{ theme.tenantTheme()?.willkommenstext || texts.t('start.willkommen') }}</h2>
        <p class="ch-start__hinweis">{{ texts.t('start.naechsteHandlung') }}</p>
      </div>
      @if (cards(); as cards) {
        @for (card of cards; track card.challengeId) {
          <ch-challenge-card [card]="card" (contribute)="zurChallenge()" />
        } @empty {
          <ch-empty-state [text]="texts.t('challenge.keineLaufend')" />
        }
      }
    </section>
  `,
  styles: `
    .ch-start {
      display: flex;
      flex-direction: column;
      gap: var(--ch-space-3);
    }

    .ch-start__kopf {
      display: flex;
      flex-direction: column;
      gap: var(--ch-space-2);
      padding: var(--ch-space-4) 0;
    }

    .ch-start__titel {
      font: 700 var(--ch-type-headline-m) / var(--ch-line-headline-m) var(--ch-font-display);
    }

    .ch-start__hinweis {
      color: var(--ch-on-surface-variant);
    }
  `,
})
export class StartPage {
  protected readonly texts = inject(TextService);
  protected readonly theme = inject(ThemeService);
  private readonly challenges = inject(ChallengesApi);
  private readonly router = inject(Router);

  readonly cards = toSignal(this.challenges.listRunning());

  zurChallenge(): void {
    void this.router.navigate(['/t', this.theme.tenantTheme()?.tenantId ?? '_', 'challenges']);
  }
}
