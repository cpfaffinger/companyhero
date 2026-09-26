// Anmeldeseite (Zugang 3, A-015, A-078): Passkey, externe Anbieter, Magic-Link, Wiederherstellungscode; Beitritt mit Code.
// Der Client kennt keine Personen: kein Namensfeld, keine Namensliste; Kennung und PIN sind hier kein Weg (Zugang 6.4).
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatError, MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { ActivatedRoute, Router } from '@angular/router';
import { catchError, of } from 'rxjs';
import { webAuthnVerfuegbar } from '../../../../libs/data-access/zugang/webauthn';
import { ZugangApi, type LoginResponse, type ZugangFehler } from '../../../../libs/data-access/zugang/zugang.api';
import { TextService } from '../../../../libs/theme/text.service';
import { zielNachAnmeldung } from '../zugang-ziel';

@Component({
  selector: 'ch-anmelden-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatButton, MatFormField, MatLabel, MatError, MatInput],
  templateUrl: './anmelden-page.html',
  styleUrl: './anmelden-page.scss',
})
export class AnmeldenPage {
  protected readonly texts = inject(TextService);
  private readonly zugang = inject(ZugangApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly zurueck = this.route.snapshot.queryParamMap.get('zurueck');
  /** Hinweis des Backends nach einem externen Anbieter (weg, anbieter, abgelehnt). */
  readonly hinweis = signal<string | null>(hinweisKey(this.route.snapshot.queryParamMap.get('fehler')));
  readonly fehler = signal<ZugangFehler | null>(null);
  readonly busy = signal(false);
  readonly passkeyMoeglich = webAuthnVerfuegbar();
  readonly providers = toSignal(this.zugang.providers().pipe(catchError(() => of([]))), { initialValue: [] });
  readonly magicGesendet = signal(false);
  /** Nach Wiederherstellungscode: der neue Code, genau einmal angezeigt (Zugang 3.1). */
  readonly neuerCode = signal<{ code: string; login: LoginResponse } | null>(null);

  readonly email = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email] });
  readonly code = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(12)] });
  readonly beitrittscode = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(8)] });

  readonly returnUrl = this.zurueck ?? '/t/_/start';

  oidcUrl(key: string): string {
    return this.zugang.oidcStartUrl(key, 'login', this.returnUrl);
  }

  mitPasskey(): void {
    this.start();
    this.zugang
      .loginWithPasskey()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (login) => this.angemeldet(login), error: (e: ZugangFehler) => this.abgelehnt(e) });
  }

  magicLink(): void {
    if (this.email.invalid) {
      this.email.markAsTouched();
      return;
    }
    this.start();
    this.zugang
      .requestMagicLink(this.email.value.trim())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.magicGesendet.set(true);
        },
        error: (e: ZugangFehler) => this.abgelehnt(e),
      });
  }

  wiederherstellen(): void {
    if (this.code.invalid) {
      this.code.markAsTouched();
      return;
    }
    this.start();
    this.zugang
      .loginWithRecoveryCode(this.code.value)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (login) => {
          if (login.newRecoveryCode) {
            this.busy.set(false);
            this.neuerCode.set({ code: login.newRecoveryCode, login });
          } else {
            this.angemeldet(login);
          }
        },
        error: (e: ZugangFehler) => this.abgelehnt(e),
      });
  }

  codeGesehen(): void {
    const state = this.neuerCode();
    if (state) {
      this.angemeldet(state.login);
    }
  }

  zumBeitritt(): void {
    if (this.beitrittscode.invalid) {
      this.beitrittscode.markAsTouched();
      return;
    }
    void this.router.navigate(['/zugang/beitritt', this.beitrittscode.value.trim().toUpperCase()]);
  }

  private start(): void {
    this.busy.set(true);
    this.fehler.set(null);
    this.hinweis.set(null);
  }

  private angemeldet(login: LoginResponse): void {
    void this.router.navigateByUrl(zielNachAnmeldung(login, this.zurueck));
  }

  private abgelehnt(fehler: ZugangFehler): void {
    this.busy.set(false);
    this.fehler.set(fehler);
  }
}

function hinweisKey(fehler: string | null): string | null {
  switch (fehler) {
    case 'weg':
      return 'zugang.fehler.way_disabled';
    case 'anbieter':
      return 'zugang.fehler.anbieterFehler';
    case 'abgelehnt':
      return 'zugang.fehler.anbieterAbgelehnt';
    default:
      return null;
  }
}
