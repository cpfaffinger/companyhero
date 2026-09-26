// Einmalige Links (Zugang 3.1, 6.4): Magic-Link aus der E-Mail und Übertragung vom Kiosk. Der Token steht in der URL, wird genau
// einmal eingelöst und führt zur Mitgliedssitzung; nach der Übertragung folgt sofort die Einrichtung eines Weges.
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ZugangApi, type ZugangFehler } from '../../../../libs/data-access/zugang/zugang.api';
import { TextService } from '../../../../libs/theme/text.service';
import { zielNachAnmeldung } from '../zugang-ziel';

@Component({
  selector: 'ch-link-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButton, RouterLink],
  template: `
    <section class="ch-link">
      <h1 class="ch-link__title">{{ texts.t(art === 'magic' ? 'zugang.magicLink.titel' : 'zugang.transfer.titel') }}</h1>
      @if (fehler(); as f) {
        <p class="ch-link__status" role="alert">{{ texts.t(f.key) }}</p>
        <a matButton="filled" routerLink="/zugang">{{ texts.t('zugang.link') }}</a>
      } @else {
        <p role="status">{{ texts.t('zugang.link.wirdGeprueft') }}</p>
      }
    </section>
  `,
  styles: `
    .ch-link {
      display: flex;
      flex-direction: column;
      gap: var(--ch-space-4);
      align-items: flex-start;
    }

    .ch-link__title {
      font: 700 var(--ch-type-headline-l) / var(--ch-line-headline-l) var(--ch-font-display);
    }

    .ch-link__status {
      padding: var(--ch-space-3) var(--ch-space-4);
      border-radius: var(--ch-radius-card);
      background: var(--ch-error-container);
      color: var(--ch-on-error-container);
    }
  `,
})
export class LinkPage {
  protected readonly texts = inject(TextService);
  private readonly zugang = inject(ZugangApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly art: 'magic' | 'transfer' = (this.route.snapshot.data['art'] as 'magic' | 'transfer') ?? 'magic';
  readonly fehler = signal<ZugangFehler | null>(null);

  constructor() {
    const token = this.route.snapshot.queryParamMap.get('token');
    if (!token) {
      this.fehler.set({ key: 'zugang.fehler.linkUngueltig', status: 0 });
      return;
    }
    const call = this.art === 'magic' ? this.zugang.consumeMagicLink(token) : this.zugang.consumeTransfer(token);
    call.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (login) => void this.router.navigateByUrl(zielNachAnmeldung(login, null), { replaceUrl: true }),
      error: (e: ZugangFehler) => this.fehler.set(e.status === 401 ? { key: 'zugang.fehler.linkUngueltig', status: 401 } : e),
    });
  }
}
