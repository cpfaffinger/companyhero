// Ich → Fortschritt, Sichtbarkeit, Gruppen, Benachrichtigungen, Auskunft (Fortschritt 6.2; Datenschutz 3.1, 6.4; Organisation 2.1;
// Benachrichtigungen 3.2, 6.1). Nur eigene Werte, keine Vergleiche; die Sichtbarkeitsstufe wirkt sofort und rückwirkend (A-022).
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { MatButtonToggle, MatButtonToggleGroup } from '@angular/material/button-toggle';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatOption, MatSelect } from '@angular/material/select';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { NotificationsApi, type PushZustand } from '../../../../libs/data-access/benachrichtigungen/notifications.api';
import { EXPORT_URL, PrivacyApi, type VisibilityLevel } from '../../../../libs/data-access/datenschutz/privacy.api';
import { ProgressApi } from '../../../../libs/data-access/fortschritt/progress.api';
import { OrganisationApi } from '../../../../libs/data-access/organisation/organisation.api';
import { TextService } from '../../../../libs/theme/text.service';
import { ThemeService } from '../../../../libs/theme/theme.service';
import { Kennzahl } from '../../../../libs/ui/kennzahl/kennzahl';

@Component({
  selector: 'ch-fortschritt-einstellungen',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButton, MatButtonToggleGroup, MatButtonToggle, MatFormField, MatLabel, MatSelect, MatOption, MatSlideToggle, Kennzahl],
  templateUrl: './fortschritt-einstellungen.html',
  styleUrl: './fortschritt-einstellungen.scss',
})
export class FortschrittEinstellungen {
  protected readonly texts = inject(TextService);
  protected readonly theme = inject(ThemeService);
  protected readonly progress = inject(ProgressApi);
  protected readonly privacy = inject(PrivacyApi);
  protected readonly organisation = inject(OrganisationApi);
  protected readonly notifications = inject(NotificationsApi);
  private readonly destroyRef = inject(DestroyRef);

  readonly exportUrl = EXPORT_URL;
  readonly pushZustand = signal<PushZustand>(this.notifications.pushZustand());
  readonly pushFehler = signal<string | null>(null);
  readonly gespeichert = signal(false);
  readonly visibilities: { value: VisibilityLevel; key: string }[] = [
    { value: 'only_me', key: 'sichtbarkeit.nurIch' },
    { value: 'team', key: 'sichtbarkeit.team' },
    { value: 'company', key: 'sichtbarkeit.firma' },
  ];

  /** Bezeichnung der Stufe aus der Marke (Fortschritt 5): Stufe n → Bezeichnung n. */
  readonly stufeName = computed(() => {
    const level = this.progress.mine()?.level ?? 1;
    const names = this.theme.tenantTheme()?.bezeichnungen.stufen ?? [];
    return names[level - 1] ?? `${level}`;
  });

  readonly gewaehlt = computed(() => {
    const map = new Map<string, string | null>();
    for (const c of this.organisation.choices() ?? []) {
      map.set(c.dimensionId, c.groupId);
    }
    return map;
  });

  constructor() {
    this.progress.load().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => undefined });
    this.privacy.loadVisibility().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => undefined });
    this.organisation.loadDimensions().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => undefined });
    this.organisation.loadChoices().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => undefined });
    this.notifications.loadSettings().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => undefined });
    this.notifications.loadSubscriptions().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => undefined });
  }

  setVisibility(value: unknown): void {
    if (value === 'only_me' || value === 'team' || value === 'company') {
      this.privacy.setVisibility(value).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => undefined });
    }
  }

  setDailyGoal(goal: number): void {
    this.progress
      .setDailyGoal(goal)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: () => this.progress.load().pipe(takeUntilDestroyed(this.destroyRef)).subscribe(), error: () => undefined });
  }

  chooseGroup(dimensionId: string, groupId: string | null): void {
    this.organisation
      .choose(dimensionId, groupId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: () => this.organisation.loadChoices().pipe(takeUntilDestroyed(this.destroyRef)).subscribe(), error: () => undefined });
  }

  /** Persönliche Schalter je Kategorie (Benachrichtigungen 6.1); In-App ist immer aktiv. */
  setSetting(field: 'pushChallenge' | 'pushProgress' | 'emailChallenge' | 'emailProgress', value: boolean): void {
    const s = this.notifications.settings();
    if (!s) {
      return;
    }
    const request = { pushChallenge: s.pushChallenge, pushProgress: s.pushProgress, emailChallenge: s.emailChallenge, emailProgress: s.emailProgress, quietStart: s.quietStart, quietEnd: s.quietEnd, [field]: value };
    this.notifications
      .saveSettings(request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: () => this.gespeichert.set(true), error: () => undefined });
  }

  /** Der Browser-Prompt erscheint erst nach diesem Tap (Benachrichtigungen 3.2). */
  pushEinschalten(): void {
    this.pushFehler.set(null);
    this.notifications
      .pushEinschalten(this.texts.t('zugang.passkey.diesesGeraet'))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.pushZustand.set(this.notifications.pushZustand()),
        error: (e: Error) => {
          this.pushZustand.set(this.notifications.pushZustand());
          this.pushFehler.set(e.message === 'push_denied' ? 'benachrichtigung.pushVerweigert' : 'benachrichtigung.pushNichtVerfuegbar');
        },
      });
  }

  geraetAbmelden(subscriptionId: string): void {
    this.notifications
      .removeSubscription(subscriptionId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: () => this.notifications.loadSubscriptions().pipe(takeUntilDestroyed(this.destroyRef)).subscribe(), error: () => undefined });
  }
}
