// Referenzscreen (Frontend 7, Integrationsregeln 6): Challenge-Karte, Formular „Beitrag nachtragen“ mit Select und Textfeld
// aus Material, Dialog aus Material. Typed Reactive Forms besitzen den Formularzustand (K10); die Abbildung auf das DTO ist
// ausdrücklich; Serverfehler erscheinen am Feld mit stabiler Kennung; Erfolg erst nach Serverbestätigung (K15).
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { ChallengesApi, type ChallengeCard as ChallengeCardData, type ContributionError } from '../../../../libs/data-access/challenges/challenges.api';
import { TextService } from '../../../../libs/theme/text.service';
import { ChallengeCard } from '../../../../libs/ui/challenge-card/challenge-card';
import { EmptyState } from '../../../../libs/ui/empty-state/empty-state';
import { BeitragLoeschenDialog } from './beitrag-loeschen-dialog';

export const NOTE_MAX = 200;

@Component({
  selector: 'ch-challenges-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatButton, MatFormField, MatLabel, MatError, MatHint, MatInput, MatSelect, MatOption, ChallengeCard, EmptyState],
  templateUrl: './challenges-page.html',
  styleUrl: './challenges-page.scss',
})
export class ChallengesPage {
  protected readonly texts = inject(TextService);
  private readonly challenges = inject(ChallengesApi);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);

  readonly cards = toSignal(this.challenges.listRunning());
  readonly current = computed(() => this.cards()?.[0] ?? null);
  readonly celebrate = signal(false);
  readonly submitting = signal(false);
  readonly serverError = signal<ContributionError | null>(null);
  readonly noteMax = NOTE_MAX;

  /** Eine führende Eingabequelle: das Formular (K10). Tag ist Pflicht und hat keine Voreinstellung. */
  readonly form = new FormGroup({
    daysAgo: new FormControl<number | null>(null, { validators: [Validators.required] }),
    note: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(NOTE_MAX)] }),
  });

  readonly dayOptions = [
    { value: 0, key: 'form.heute' },
    { value: 1, key: 'form.gestern' },
    { value: 2, key: 'form.vorgestern' },
    { value: 3, key: 'form.vorDreiTagen' },
  ];

  readonly noteLength = toSignal(this.form.controls.note.valueChanges, { initialValue: '' });

  constructor() {
    this.form.controls.daysAgo.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.serverError.set(null));
  }

  /** „Heute erledigt“ auf der Karte: Häkchen für heute, ohne Formular (Onboarding 2: ein Tap). */
  heuteErledigt(card: ChallengeCardData): void {
    this.send(card, { challengeId: card.challengeId, daysAgo: 0, value: '1' });
  }

  /** Formular: Submit bildet ausdrücklich auf das DTO ab; die Notiz bleibt lokal. */
  eintragen(): void {
    const card = this.current();
    if (!card || this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }
    this.send(card, { challengeId: card.challengeId, daysAgo: this.form.controls.daysAgo.value ?? 0, value: '1', note: this.form.controls.note.value });
  }

  abbrechen(): void {
    this.form.reset({ daysAgo: null, note: '' });
    this.serverError.set(null);
  }

  loeschenDialog(): void {
    this.dialog.open(BeitragLoeschenDialog, { autoFocus: 'first-tabbable', restoreFocus: true });
  }

  private send(card: ChallengeCardData, input: { challengeId: string; daysAgo: number; value: string; note?: string }): void {
    this.submitting.set(true);
    this.serverError.set(null);
    const request = this.challenges.toRequest(input, new Date());
    this.challenges
      .submit(card.challengeId, request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.celebrate.set(true);
          this.form.reset({ daysAgo: null, note: '' });
          this.challenges.refresh().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
        },
        error: (error: ContributionError) => {
          this.submitting.set(false);
          this.serverError.set(error);
          this.form.controls.daysAgo.setErrors({ server: error.key });
          this.form.controls.daysAgo.markAsTouched();
        },
      });
  }
}
