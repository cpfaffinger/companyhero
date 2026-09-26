// Kiosk (Zugang 6, A-018, A-081): Anmeldemaske mit Kennung, PIN und Ziffernblock, immer im Großflächenmodus; die linke Spalte
// trägt Marke, Gerätehinweis und den Kollektivstand des Tenants, sobald eine Gerätesitzung besteht (Stufe 4). Bis dahin
// bindet die Maske keinen Backend-Aufruf an und meldet das sichtbar.
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, of } from 'rxjs';
import { BrandingApi } from '../../../libs/data-access/branding/branding.api';
import { ChallengesApi } from '../../../libs/data-access/challenges/challenges.api';
import { TextService } from '../../../libs/theme/text.service';
import { ThemeService } from '../../../libs/theme/theme.service';
import { CollectiveBar } from '../../../libs/ui/collective-bar/collective-bar';
import { KioskAnmeldemaske, type KioskAnmeldung } from '../../../libs/ui/kiosk-anmeldemaske/kiosk-anmeldemaske';

@Component({
  selector: 'ch-kiosk-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [KioskAnmeldemaske, CollectiveBar],
  templateUrl: './kiosk-page.html',
  styleUrl: './kiosk-page.scss',
})
export class KioskPage {
  protected readonly texts = inject(TextService);
  protected readonly theme = inject(ThemeService);
  private readonly branding = inject(BrandingApi);
  private readonly challenges = inject(ChallengesApi);

  readonly hinweis = signal<string | null>(null);
  readonly cards = toSignal(this.challenges.listRunning().pipe(catchError(() => of(null))));
  readonly loaded = toSignal(this.branding.tenantTheme().pipe(catchError(() => of(null))));

  constructor() {
    this.theme.context.set('tenant');
    this.theme.forcedScale.set('gross');
  }

  anmelden(anmeldung: KioskAnmeldung): void {
    // Stufe 4 bindet hier die Kiosk-Personensitzung an (Kennung, PIN, Drosselung). Kennung und PIN verlassen die Maske nicht.
    void anmeldung;
    this.hinweis.set(this.texts.t('kiosk.nochNichtVerbunden'));
  }
}
