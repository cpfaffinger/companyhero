// Start (Feed 2.3; Onboarding 1): Kopfkarte (laufende Challenge oder Tagesziel), Check-in-Karte bis zum heutigen Check-in,
// Stream mit Tagesüberschriften, Beitrag schreiben im Kreis der eigenen Sichtbarkeitsstufe (Feed 3.2). Reihenfolge und
// Sichtbarkeit entscheidet das Backend; der Client fragt im Vordergrund alle 60 Sekunden mit ETag und zeigt den Hinweis (Feed 2.6).
import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { Router } from '@angular/router';
import { filter, interval } from 'rxjs';
import { FeedApi, type FeedCard as FeedCardData, type FeedFehler } from '../../../../libs/data-access/feed/feed.api';
import { ProgressApi, type CheckInTile } from '../../../../libs/data-access/fortschritt/progress.api';
import { TextService } from '../../../../libs/theme/text.service';
import { ThemeService } from '../../../../libs/theme/theme.service';
import { ChallengeCard } from '../../../../libs/ui/challenge-card/challenge-card';
import { EmptyState } from '../../../../libs/ui/empty-state/empty-state';
import { FeedCard } from '../../../../libs/ui/feed-card/feed-card';

export const POST_MAX = 1000;

@Component({
  selector: 'ch-start-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatButton, MatFormField, MatLabel, MatHint, MatInput, ChallengeCard, EmptyState, FeedCard],
  templateUrl: './start-page.html',
  styleUrl: './start-page.scss',
})
export class StartPage {
  protected readonly texts = inject(TextService);
  protected readonly theme = inject(ThemeService);
  protected readonly feed = inject(FeedApi);
  private readonly progress = inject(ProgressApi);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly document = inject(DOCUMENT);

  readonly tiles = signal<CheckInTile[]>([]);
  readonly checkInDone = signal<string | null>(null);
  readonly postFehler = signal<FeedFehler | null>(null);
  readonly postGesendet = signal(false);
  readonly busy = signal(false);
  readonly postMax = POST_MAX;
  readonly post = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(POST_MAX)] });

  readonly tileOptions: { value: CheckInTile; key: string; icon: string }[] = [
    { value: 'moved', key: 'checkin.bewegt', icon: 'directions_walk' },
    { value: 'paused', key: 'checkin.pause', icon: 'self_improvement' },
    { value: 'rested', key: 'checkin.erholt', icon: 'bedtime' },
  ];

  /** Tagesüberschriften (Feed 2.3): Karten nach Kalendertag des Tenants gruppiert, Reihenfolge vom Backend. */
  readonly tage = computed(() => {
    const cards = this.feed.feed()?.cards ?? [];
    const groups: { day: string; label: string; cards: FeedCardData[] }[] = [];
    for (const card of cards) {
      const last = groups[groups.length - 1];
      if (last && last.day === card.day) {
        last.cards.push(card);
      } else {
        groups.push({ day: card.day, label: this.tagLabel(card.day), cards: [card] });
      }
    }
    return groups;
  });

  constructor() {
    this.feed.load().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => undefined });
    interval(60_000)
      .pipe(
        filter(() => this.document.visibilityState === 'visible'),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => this.feed.pruefen().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => undefined }));
  }

  neuLaden(): void {
    this.feed.load().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => undefined });
  }

  toggleTile(tile: CheckInTile): void {
    this.tiles.update((t) => (t.includes(tile) ? t.filter((x) => x !== tile) : [...t, tile]));
  }

  checkIn(): void {
    if (this.tiles().length === 0 || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.progress
      .checkIn(this.tiles())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (outcome) => {
          this.busy.set(false);
          this.checkInDone.set(outcome);
          this.neuLaden();
        },
        error: () => this.busy.set(false),
      });
  }

  senden(): void {
    if (this.post.invalid || this.busy()) {
      this.post.markAsTouched();
      return;
    }
    this.busy.set(true);
    this.postFehler.set(null);
    this.feed
      .post(this.post.value.trim())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.postGesendet.set(true);
          this.post.reset('');
          this.neuLaden();
        },
        error: (e: FeedFehler) => {
          this.busy.set(false);
          this.postFehler.set(e);
        },
      });
  }

  loeschen(card: FeedCardData): void {
    this.feed.remove(card.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: () => this.neuLaden(), error: () => undefined });
  }

  zurChallenge(): void {
    void this.router.navigate(['/t', this.theme.tenantTheme()?.tenantId ?? '_', 'challenges']);
  }

  zuIch(): void {
    void this.router.navigate(['/t', this.theme.tenantTheme()?.tenantId ?? '_', 'ich']);
  }

  private tagLabel(day: string): string {
    const today = new Date().toISOString().slice(0, 10);
    if (day === today) {
      return this.texts.t('feed.heute');
    }
    return new Intl.DateTimeFormat('de-AT', { weekday: 'long', day: 'numeric', month: 'long' }).format(new Date(day + 'T12:00:00'));
  }
}
