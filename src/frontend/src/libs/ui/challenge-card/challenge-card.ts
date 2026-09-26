// Challenge-Karte (Marke 2.5 Fortschritt, Challenges 6.2): Sammelziel mit Häkchen, Kollektivbalken mit Meilensteinen,
// Altersangabe des Standes (A-042), eigener Beitrag heute, eine Aktion. Alle Zahlen kommen vom Backend; die Karte rechnet
// nichts fachlich nach. Texte aus dem Textkatalog in Tonalität und Anrede des Tenants.
import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { MatButton } from '@angular/material/button';
import type { ChallengeCard as ChallengeCardData } from '../../data-access/challenges/challenges.api';
import { TextService } from '../../theme/text.service';
import { CollectiveBar, MILESTONES } from '../collective-bar/collective-bar';
import { ProgressRing } from '../progress-ring/progress-ring';

@Component({
  selector: 'ch-challenge-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButton, CollectiveBar, ProgressRing],
  templateUrl: './challenge-card.html',
  styleUrl: './challenge-card.scss',
})
export class ChallengeCard {
  protected readonly texts = inject(TextService);

  readonly card = input.required<ChallengeCardData>();
  /** Zeitpunkt der Anzeige; als Eingabe, damit Tests und Referenzscreen reproduzierbar sind. */
  readonly now = input<Date>(new Date());
  readonly celebrate = input(false);
  readonly contribute = output<ChallengeCardData>();

  readonly percent = computed(() => this.card().percent);
  readonly nextMilestone = computed(() => MILESTONES.find((m) => m > this.percent()) ?? 100);

  readonly daysLeft = computed(() => {
    const end = new Date(this.card().endsAt).getTime();
    return Math.max(0, Math.ceil((end - this.now().getTime()) / 86_400_000));
  });

  readonly daysLeftText = computed(() => (this.daysLeft() === 1 ? this.texts.t('challenge.nochEinTag') : this.texts.t('challenge.nochTage', { tage: this.daysLeft() })));

  readonly standText = computed(() => {
    const updated = this.card().collective?.updatedAt;
    if (!updated) {
      return this.texts.t('challenge.standGerade');
    }
    const minutes = Math.max(0, Math.round((this.now().getTime() - new Date(updated).getTime()) / 60_000));
    return minutes < 1 ? this.texts.t('challenge.standGerade') : this.texts.t('challenge.stand', { minuten: minutes });
  });

  readonly formText = computed(() => this.texts.t(this.card().metric === 'count' ? 'challenge.form.zahl' : 'challenge.form.sammelziel'));

  readonly barLabel = computed(() =>
    this.percent() >= 100 ? this.texts.t('challenge.zielErreicht') : this.texts.t('challenge.prozentErreicht', { prozent: this.percent(), meilenstein: this.nextMilestone() }),
  );

  readonly percentText = computed(() => `${this.texts.formatNumber(this.percent())} %`);
}
