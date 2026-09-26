// Beitritt mit Code (Zugang 2.1, 2.2; A-014): Tenant-Vorschau, Anzeigename, Sichtbarkeit, Zugang sichern mit Passkey, E-Mail
// oder externem Anbieter (nach dessen Prüfung liegt die Identität geschützt vor, `pendingExternal`); erzwungene Anbieter lassen
// nur den Anbieterweg. Nach dem Beitritt: Kiosk-Kennung und, ohne E-Mail und Anbieter, der Wiederherstellungscode genau einmal.
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { MatRadioButton, MatRadioGroup } from '@angular/material/radio';
import { ActivatedRoute, Router } from '@angular/router';
import { ZugangApi, type JoinPreview, type JoinResponse, type ZugangFehler } from '../../../../libs/data-access/zugang/zugang.api';
import { webAuthnVerfuegbar } from '../../../../libs/data-access/zugang/webauthn';
import { TextService } from '../../../../libs/theme/text.service';
import { beitrittForm, groupChoices, VISIBILITIES } from './beitritt-form';

@Component({
  selector: 'ch-beitritt-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatButton, MatFormField, MatLabel, MatError, MatHint, MatInput, MatRadioGroup, MatRadioButton, MatSelect, MatOption],
  templateUrl: './beitritt-page.html',
  styleUrl: './beitritt-page.scss',
})
export class BeitrittPage {
  protected readonly texts = inject(TextService);
  private readonly zugang = inject(ZugangApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly code = signal<string | null>(this.route.snapshot.paramMap.get('code'));
  /** Nach dem externen Anbieter ohne Code (Zugang 3.2): erst den Beitrittscode erfragen. */
  readonly externOhneCode = this.route.snapshot.queryParamMap.get('extern') === '1' && !this.code();
  readonly preview = signal<JoinPreview | null>(null);
  readonly fehler = signal<ZugangFehler | null>(null);
  readonly busy = signal(false);
  readonly ergebnis = signal<JoinResponse | null>(null);
  readonly passkeyMoeglich = webAuthnVerfuegbar();
  readonly visibilities = VISIBILITIES;

  readonly form = beitrittForm();
  readonly codeEingabe = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(8)] });

  readonly forced = computed(() => this.preview()?.ways.forcedProviderKeys ?? []);
  readonly nurAnbieter = computed(() => this.forced().length > 0 && !this.preview()?.pendingExternal);
  readonly anbieter = computed(() => {
    const p = this.preview();
    if (!p) {
      return [];
    }
    return this.forced().length > 0 ? p.ways.providers.filter((x) => this.forced().includes(x.key)) : p.ways.providers;
  });

  constructor() {
    const code = this.code();
    if (code) {
      this.laden(code);
    }
  }

  codeUebernehmen(): void {
    if (this.codeEingabe.invalid) {
      this.codeEingabe.markAsTouched();
      return;
    }
    const code = this.codeEingabe.value.trim().toUpperCase();
    this.code.set(code);
    void this.router.navigate(['/zugang/beitritt', code], { replaceUrl: true, queryParamsHandling: 'preserve' });
    this.laden(code);
  }

  oidcUrl(key: string): string {
    return this.zugang.oidcStartUrl(key, 'join', `/zugang/beitritt/${this.code() ?? ''}`);
  }

  beitreten(): void {
    const code = this.code();
    const preview = this.preview();
    if (!code || !preview) {
      return;
    }
    const weg = this.form.controls.weg.value;
    if (weg === 'email') {
      this.form.controls.email.addValidators(Validators.required);
      this.form.controls.email.updateValueAndValidity();
    } else {
      this.form.controls.email.removeValidators(Validators.required);
      this.form.controls.email.updateValueAndValidity();
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const v = this.form.getRawValue();
    const request = {
      displayName: v.name.trim(),
      visibility: v.visibility!,
      email: weg === 'email' ? v.email.trim() : null,
      kioskPin: v.kioskPin ? v.kioskPin : null,
      groups: groupChoices(v.groups),
    };
    this.busy.set(true);
    this.fehler.set(null);
    const call =
      weg === 'passkey'
        ? this.zugang.joinWithPasskey(code, request, this.texts.t('zugang.passkey.diesesGeraet'))
        : this.zugang.join(code, { ...request, passkey: null, useExternal: weg === 'extern' });
    call.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (result) => {
        this.busy.set(false);
        this.ergebnis.set(result);
      },
      error: (e: ZugangFehler) => {
        this.busy.set(false);
        if (e.key === 'zugang.fehler.display_name_taken') {
          this.form.controls.name.setErrors({ server: e.key });
        } else {
          this.fehler.set(e);
        }
      },
    });
  }

  weiter(): void {
    const result = this.ergebnis();
    if (result) {
      void this.router.navigateByUrl(`/t/${result.tenantId}/start`);
    }
  }

  private laden(code: string): void {
    this.zugang
      .joinPreview(code)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (preview) => {
          this.preview.set(preview);
          // Gruppenwahl je Dimension des Tenants (Organisation 2.1): optional, ohne Voreinstellung.
          for (const dimension of preview.dimensions) {
            if (!this.form.controls.groups.contains(dimension.dimensionId)) {
              this.form.controls.groups.addControl(dimension.dimensionId, new FormControl<string | null>(null));
            }
          }
          if (preview.pendingExternal) {
            this.form.controls.weg.setValue('extern');
          } else if (!preview.ways.passkey || !this.passkeyMoeglich) {
            this.form.controls.weg.setValue(preview.ways.magicLink ? 'email' : 'extern');
          }
        },
        error: (e: ZugangFehler) => this.fehler.set(e),
      });
  }
}
