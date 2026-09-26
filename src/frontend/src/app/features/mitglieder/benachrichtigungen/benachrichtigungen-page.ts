// Benachrichtigungszentrum hinter der Glocke (Benachrichtigungen 7.2): Liste nach Zeit, ungelesen hervorgehoben, Öffnen
// markiert als gelesen und führt zum Ziel, „Alle gelesen“. Texte aus dem Katalog; keine Anzeigenamen Dritter.
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { Router } from '@angular/router';
import { NotificationsApi, type Notification } from '../../../../libs/data-access/benachrichtigungen/notifications.api';
import { TextService } from '../../../../libs/theme/text.service';
import { ThemeService } from '../../../../libs/theme/theme.service';
import { EmptyState } from '../../../../libs/ui/empty-state/empty-state';

@Component({
  selector: 'ch-benachrichtigungen-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButton, EmptyState],
  template: `
    <section class="ch-benachrichtigungen">
      @if (notifications.entries(); as entries) {
        @if (entries.length > 0) {
          <div class="ch-benachrichtigungen__kopf">
            <span>{{ texts.t('benachrichtigung.ungelesen', { n: ungelesen() }) }}</span>
            <button matButton type="button" (click)="alleGelesen()" [disabled]="ungelesen() === 0" data-testid="alle-gelesen">{{ texts.t('benachrichtigung.alleGelesen') }}</button>
          </div>
          <ul class="ch-benachrichtigungen__liste">
            @for (entry of entries; track entry.id) {
              <li>
                <button type="button" class="ch-benachrichtigungen__eintrag" [class.ch-benachrichtigungen__eintrag--neu]="!entry.readAt" (click)="oeffnen(entry)" [attr.data-testid]="'eintrag-' + entry.category">
                  <span class="material-symbols-rounded" aria-hidden="true">{{ entry.category === 'challenge' ? 'flag' : 'military_tech' }}</span>
                  <span class="ch-benachrichtigungen__text">{{ texts.t(entry.textKey, entry.params) }}</span>
                  <time [attr.datetime]="entry.occurredAt">{{ zeit(entry) }}</time>
                </button>
              </li>
            }
          </ul>
        } @else {
          <ch-empty-state [text]="texts.t('benachrichtigung.leer')" />
        }
      }
    </section>
  `,
  styles: `
    .ch-benachrichtigungen {
      display: flex;
      flex-direction: column;
      gap: var(--ch-space-3);
      padding-top: var(--ch-space-3);
    }

    .ch-benachrichtigungen__kopf {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--ch-space-3);
      color: var(--ch-on-surface-variant);
    }

    .ch-benachrichtigungen__liste {
      display: flex;
      flex-direction: column;
      gap: var(--ch-space-2);
      list-style: none;
      margin: 0;
      padding: 0;
    }

    .ch-benachrichtigungen__eintrag {
      display: flex;
      align-items: center;
      gap: var(--ch-space-3);
      width: 100%;
      min-height: var(--ch-touch-target);
      padding: var(--ch-space-3) var(--ch-space-4);
      border-radius: var(--ch-radius-card);
      border: 1px solid var(--ch-outline-variant);
      background: var(--ch-surface);
      color: var(--ch-on-surface);
      font: inherit;
      text-align: left;
      cursor: pointer;
    }

    .ch-benachrichtigungen__eintrag--neu {
      background: var(--ch-primary-container);
      color: var(--ch-on-primary-container);
      border-color: transparent;
    }

    .ch-benachrichtigungen__text {
      flex-grow: 1;
    }
  `,
})
export class BenachrichtigungenPage {
  protected readonly texts = inject(TextService);
  protected readonly notifications = inject(NotificationsApi);
  private readonly theme = inject(ThemeService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly ungelesen = computed(() => (this.notifications.entries() ?? []).filter((e) => !e.readAt).length);

  constructor() {
    this.notifications.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => undefined });
  }

  oeffnen(entry: Notification): void {
    if (!entry.readAt) {
      this.notifications.markRead(entry.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => undefined });
    }
    const tenant = this.theme.tenantTheme()?.tenantId ?? '_';
    void this.router.navigate(['/t', tenant, entry.target.startsWith('/challenges') ? 'challenges' : 'ich']);
  }

  alleGelesen(): void {
    this.notifications
      .markAllRead()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: () => this.notifications.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe(), error: () => undefined });
  }

  zeit(entry: Notification): string {
    return new Intl.DateTimeFormat('de-AT', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }).format(new Date(entry.occurredAt));
  }
}
