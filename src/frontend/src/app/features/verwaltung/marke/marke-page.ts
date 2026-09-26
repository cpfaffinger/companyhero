// Verwaltung „Marke und Sprache“ (Marke 3.5, A-013, A-077): Theme-Dokument bearbeiten, Live-Vorschau über den
// Backend-Endpunkt ohne Persistierung, Kontrastbericht mit Ersetzungen, Veröffentlichen als neue Version. Eigener
// Feature-Einstieg, nie im ersten Ladepaket (K17, A-096). Berechtigung entscheidet das Backend (K14).
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { toProblem } from '../../../../libs/api-client/api-client';
import type { components } from '../../../../libs/api-client/generated/api';
import { BrandingApi } from '../../../../libs/data-access/branding/branding.api';
import { TextService } from '../../../../libs/theme/text.service';
import { ThemeService } from '../../../../libs/theme/theme.service';

type ThemeDocumentRequest = components['schemas']['ThemeDocumentRequest'];
type ThemePreviewResponse = components['schemas']['ThemePreviewResponse'];

const SHOWN_ROLES = ['primary', 'on-primary', 'primary-container', 'progress', 'progress-container', 'team', 'surface', 'on-surface', 'error'];

@Component({
  selector: 'ch-marke-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatButton, MatFormField, MatLabel, MatInput, MatSelect, MatOption],
  templateUrl: './marke-page.html',
  styleUrl: './marke-page.scss',
})
export class MarkePage {
  protected readonly texts = inject(TextService);
  private readonly theme = inject(ThemeService);
  private readonly branding = inject(BrandingApi);
  private readonly destroyRef = inject(DestroyRef);

  readonly roles = SHOWN_ROLES;
  readonly preview = signal<ThemePreviewResponse | null>(null);
  readonly reasons = signal<string[]>([]);
  readonly published = signal<number | null>(null);

  readonly form = new FormGroup({
    produktname: new FormControl(this.theme.tenantTheme()?.produktname ?? '', { nonNullable: true, validators: [Validators.required, Validators.maxLength(40)] }),
    saatfarbe: new FormControl(this.theme.tokens()?.['primary'] ?? '', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^#[0-9A-Fa-f]{6}$/)] }),
    anrede: new FormControl<'du' | 'sie'>(this.theme.tenantTheme()?.anrede ?? 'du', { nonNullable: true }),
    tonalitaet: new FormControl<'sachlich' | 'freundlich' | 'motivierend'>(this.theme.tenantTheme()?.tonalitaet ?? 'freundlich', { nonNullable: true }),
  });

  vorschau(): void {
    this.reasons.set([]);
    this.branding
      .preview(this.document())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (preview) => this.preview.set(preview), error: (error: { status: number; error: unknown }) => this.reasons.set(toProblem(error.status, error.error).errors?.['theme'] ?? ['Vorschau nicht möglich.']) });
  }

  veroeffentlichen(): void {
    this.reasons.set([]);
    this.branding
      .publish(this.document())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (theme) => this.published.set(theme.version), error: (error: { status: number; error: unknown }) => this.reasons.set(toProblem(error.status, error.error).errors?.['theme'] ?? ['Veröffentlichung nicht möglich.']) });
  }

  /** Ausdrückliche Abbildung Formular → Theme-Dokument (K10); Bezeichnungen und Willkommenstext bleiben wie veröffentlicht. */
  private document(): ThemeDocumentRequest {
    const current = this.theme.tenantTheme();
    const value = this.form.getRawValue();
    return {
      schema: 1,
      produktname: value.produktname.trim(),
      saatfarbe: value.saatfarbe.toUpperCase(),
      akzent: null,
      anrede: value.anrede,
      tonalitaet: value.tonalitaet,
      bezeichnungen: current?.bezeichnungen ?? { punkte: 'Punkte', serie: 'Serie', stufen: ['Neu dabei', 'Dabei', 'Dranbleiber', 'Vorbild', 'Urgestein'] },
      willkommenstext: current?.willkommenstext ?? null,
    };
  }
}
