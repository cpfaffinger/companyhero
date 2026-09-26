// Karte des Streams (Feed 2.2): System-Ereignis, Sammelkarte oder Mitglieder-Beitrag. Text aus dem Katalog in Tonalität und
// Anrede des Tenants mit den Platzhaltern der Karte; Anzeigename nur, wenn das Backend ihn nach Sichtbarkeit geliefert hat.
import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { MatButton } from '@angular/material/button';
import type { FeedCard as FeedCardData } from '../../data-access/feed/feed.api';
import { TextService } from '../../theme/text.service';

@Component({
  selector: 'ch-feed-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButton],
  template: `
    <article class="ch-feed-card" [class.ch-feed-card--post]="card().kind === 'member_post'" [class.ch-feed-card--aggregate]="card().kind === 'aggregate'" [attr.data-testid]="'feed-' + card().kind">
      <div class="ch-feed-card__kopf">
        <span class="material-symbols-rounded ch-feed-card__icon" aria-hidden="true">{{ icon() }}</span>
        @if (card().person; as person) {
          <span class="ch-feed-card__person">{{ person.displayName }}</span>
        }
        <time class="ch-feed-card__zeit" [attr.datetime]="card().occurredAt">{{ zeit() }}</time>
      </div>
      @if (card().kind === 'member_post') {
        <p class="ch-feed-card__text">{{ card().body }}</p>
      } @else {
        <p class="ch-feed-card__text">{{ text() }}</p>
      }
      @if (card().reference?.kind === 'challenge' && card().reference?.id) {
        <button matButton type="button" (click)="open.emit(card())">{{ texts.t('feed.oeffnen') }}</button>
      }
      @if (card().kind === 'member_post' && card().mine) {
        <button matButton type="button" (click)="remove.emit(card())">{{ texts.t('feed.beitragLoeschen') }}</button>
      }
    </article>
  `,
  styles: `
    .ch-feed-card {
      display: flex;
      flex-direction: column;
      gap: var(--ch-space-2);
      padding: var(--ch-space-4);
      border-radius: var(--ch-radius-card);
      border: 1px solid var(--ch-outline-variant);
      background: var(--ch-surface);
    }

    .ch-feed-card--aggregate {
      background: var(--ch-progress-container);
      color: var(--ch-on-progress-container);
      border-color: transparent;
    }

    .ch-feed-card--post {
      background: var(--ch-surface-container);
    }

    .ch-feed-card__kopf {
      display: flex;
      align-items: center;
      gap: var(--ch-space-2);
      font-size: var(--ch-type-label-m);
      line-height: var(--ch-line-label-m);
      color: var(--ch-on-surface-variant);
    }

    .ch-feed-card--aggregate .ch-feed-card__kopf {
      color: inherit;
    }

    .ch-feed-card__person {
      font-weight: 600;
      color: var(--ch-on-surface);
    }

    .ch-feed-card__zeit {
      margin-left: auto;
    }

    .ch-feed-card__text {
      white-space: pre-wrap;
    }
  `,
})
export class FeedCard {
  protected readonly texts = inject(TextService);

  readonly card = input.required<FeedCardData>();
  readonly open = output<FeedCardData>();
  readonly remove = output<FeedCardData>();

  readonly text = computed(() => this.texts.t(this.card().textKey, this.card().params));
  readonly icon = computed(() => {
    const c = this.card();
    if (c.kind === 'member_post') {
      return 'chat_bubble';
    }
    if (c.kind === 'aggregate') {
      return 'groups';
    }
    return c.reference?.kind === 'challenge' ? 'flag' : c.reference?.kind === 'badge' ? 'military_tech' : c.reference?.kind === 'level' ? 'stairs' : 'check_circle';
  });
  readonly zeit = computed(() => new Intl.DateTimeFormat('de-AT', { hour: '2-digit', minute: '2-digit' }).format(new Date(this.card().occurredAt)));
}
