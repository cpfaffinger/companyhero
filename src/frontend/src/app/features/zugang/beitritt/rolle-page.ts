// Rollencode (Zugang 2.3; A-014): personalisierter Einmalcode für eine Funktionsrolle. Neue Person: Klarname (wird Anzeigename),
// Sichtbarkeit, Weg mit den Voraussetzungen der Rolle (E-Mail oder Anbieter; Passkey oder Anbieter). Bestehende, angemeldete
// Person: Einlösen für die eigene Person; die Sitzung endet danach, weil sich die Sitzungsart ändert (Zugang 5).
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatRadioButton, MatRadioGroup } from '@angular/material/radio';
import { ActivatedRoute, Router } from '@angular/router';
import { SessionStore } from '../../../../libs/data-access/zugang/session.store';
import { ZugangApi, type JoinPreview, type JoinResponse, type ZugangFehler } from '../../../../libs/data-access/zugang/zugang.api';
import { webAuthnVerfuegbar } from '../../../../libs/data-access/zugang/webauthn';
import { TextService } from '../../../../libs/theme/text.service';
import { beitrittForm, VISIBILITIES } from './beitritt-form';

@Component({
  selector: 'ch-rolle-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatButton, MatFormField, MatLabel, MatError, MatHint, MatInput, MatRadioGroup, MatRadioButton],
  templateUrl: './rolle-page.html',
  styleUrl: './beitritt-page.scss',
})
export class RollePage {
  protected readonly texts = inject(TextService);
  private readonly zugang = inject(ZugangApi);
  private readonly sessions = inject(SessionStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly code = this.route.snapshot.paramMap.get('code') ?? '';
  readonly preview = signal<JoinPreview | null>(null);
  readonly fehler = signal<ZugangFehler | null>(null);
  readonly busy = signal(false);
  readonly ergebnis = signal<JoinResponse | null>(null);
  readonly eingeloest = signal(false);
  readonly passkeyMoeglich = webAuthnVerfuegbar();
  readonly visibilities = VISIBILITIES;
  readonly form = beitrittForm();
  /** Angemeldete Person: die Rolle kommt zur bestehenden Person. */
  readonly angemeldet = computed(() => this.sessions.isPerson());
  readonly rolleKey = computed(() => `rolle.${this.preview()?.role ?? 'member'}`);

  constructor() {
    this.form.controls.name.addValidators(Validators.required);
    this.zugang
      .session()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ error: () => undefined });
    this.zugang
      .rolePreview(this.code)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (preview) => {
          this.preview.set(preview);
          if (preview.pendingExternal) {
            this.form.controls.weg.setValue('extern');
          }
        },
        error: (e: ZugangFehler) => this.fehler.set(e),
      });
  }

  oidcUrl(key: string): string {
    return this.zugang.oidcStartUrl(key, 'join', `/zugang/rolle/${this.code}`);
  }

  /** Neue Person mit Klarname; Passkey verlangt zusätzlich eine E-Mail (Voraussetzungen der Rolle). */
  beitreten(): void {
    const weg = this.form.controls.weg.value;
    if (weg === 'passkey' || weg === 'email') {
      this.form.controls.email.addValidators(Validators.required);
    } else {
      this.form.controls.email.removeValidators(Validators.required);
    }
    this.form.controls.email.updateValueAndValidity();
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const request = { realName: v.name.trim(), visibility: v.visibility!, email: weg === 'extern' ? null : v.email.trim() };
    this.busy.set(true);
    this.fehler.set(null);
    const call =
      weg === 'passkey'
        ? this.zugang.roleJoinWithPasskey(this.code, request, this.texts.t('zugang.passkey.diesesGeraet'))
        : this.zugang.roleJoin(this.code, { ...request, passkey: null, useExternal: weg === 'extern' });
    call.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (result) => {
        this.busy.set(false);
        this.ergebnis.set(result);
      },
      error: (e: ZugangFehler) => {
        this.busy.set(false);
        this.fehler.set(e);
      },
    });
  }

  /** Bestehende Person: Klarname setzen und Rolle hinzufügen; danach neu anmelden. */
  einloesen(): void {
    if (this.form.controls.name.invalid) {
      this.form.controls.name.markAsTouched();
      return;
    }
    this.busy.set(true);
    this.fehler.set(null);
    this.zugang
      .redeemRoleCode(this.code, this.form.controls.name.value.trim())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.eingeloest.set(true);
          this.sessions.set(null);
        },
        error: (e: ZugangFehler) => {
          this.busy.set(false);
          this.fehler.set(e);
        },
      });
  }

  weiter(): void {
    const result = this.ergebnis();
    void this.router.navigateByUrl(result ? `/t/${result.tenantId}/start` : '/zugang');
  }
}
