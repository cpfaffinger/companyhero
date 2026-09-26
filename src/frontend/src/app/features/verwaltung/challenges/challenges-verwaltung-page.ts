// Verwaltung „Challenges“ (Challenges 4.1 bis 4.3, A-038): Kickoff-Vorbelegung, Wizard mit den Achsen des Durchstichs (Sammelziel,
// Häkchen oder Zahl, Zeitraum, ganze Firma), Vorschau als Pflicht vor dem Planen, vorzeitiges Ende mit Begründung. Eigener
// Lazy-Einstieg (K17, A-096); Berechtigung entscheidet das Backend (K14).
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatError, MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { HttpErrorResponse } from '@angular/common/http';
import { toProblem } from '../../../../libs/api-client/api-client';
import { ChallengesApi, type ChallengeCard as ChallengeCardData, type ChallengeDraftRequest } from '../../../../libs/data-access/challenges/challenges.api';
import { TextService } from '../../../../libs/theme/text.service';
import { ChallengeCard } from '../../../../libs/ui/challenge-card/challenge-card';

@Component({
  selector: 'ch-challenges-verwaltung-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatButton, MatFormField, MatLabel, MatError, MatInput, MatSelect, MatOption, ChallengeCard],
  templateUrl: './challenges-verwaltung-page.html',
  styleUrl: './challenges-verwaltung-page.scss',
})
export class ChallengesVerwaltungPage {
  protected readonly texts = inject(TextService);
  private readonly challenges = inject(ChallengesApi);
  private readonly destroyRef = inject(DestroyRef);

  readonly cards = signal<ChallengeCardData[] | null>(null);
  readonly vorschau = signal<ChallengeCardData | null>(null);
  readonly fehler = signal<string | null>(null);
  readonly busy = signal(false);
  readonly endeFuer = signal<string | null>(null);
  readonly begruendung = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(500)] });

  /** Achsen des Wizards (Challenges 2): Form und Aggregation sind im Durchstich gesetzt (Sammelziel, ganze Firma). */
  readonly form = new FormGroup({
    title: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(200)] }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(2000)] }),
    metric: new FormControl<'checkmark' | 'count'>('checkmark', { nonNullable: true }),
    target: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^\d+(\.\d{1,4})?$/)] }),
    startsAt: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    endsAt: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  constructor() {
    this.laden();
  }

  /** Zustandsbezeichnung aus dem Katalog mit ausdrücklichen Schlüsseln (A-080), ohne zusammengesetzte Schlüssel. */
  zustand(state: string): string {
    switch (state) {
      case 'draft':
        return this.texts.t('verwaltung.challenges.zustand.draft');
      case 'planned':
        return this.texts.t('verwaltung.challenges.zustand.planned');
      case 'running':
        return this.texts.t('verwaltung.challenges.zustand.running');
      case 'grace':
        return this.texts.t('verwaltung.challenges.zustand.grace');
      case 'ended':
        return this.texts.t('verwaltung.challenges.zustand.ended');
      case 'archived':
        return this.texts.t('verwaltung.challenges.zustand.archived');
      default:
        return state;
    }
  }

  laden(): void {
    this.challenges
      .listAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (cards) => this.cards.set(cards), error: (e: unknown) => this.melden(e) });
  }

  kickoff(): void {
    this.run(this.challenges.kickoff(), (card) => this.vorschauLaden(card.challengeId));
  }

  entwurfAnlegen(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.run(this.challenges.createDraft(this.draft()), (card) => this.vorschauLaden(card.challengeId));
  }

  vorschauLaden(challengeId: string): void {
    this.run(this.challenges.preview(challengeId), (card) => this.vorschau.set(card));
  }

  planen(challengeId: string): void {
    this.run(this.challenges.plan(challengeId), () => this.vorschau.set(null));
  }

  endeStarten(challengeId: string): void {
    this.endeFuer.set(challengeId);
    this.begruendung.reset('');
  }

  endeBestaetigen(): void {
    const id = this.endeFuer();
    if (!id || this.begruendung.invalid) {
      this.begruendung.markAsTouched();
      return;
    }
    this.run(this.challenges.endEarly(id, this.begruendung.value.trim()), () => this.endeFuer.set(null));
  }

  /** Ausdrückliche Abbildung Formular → DTO (K10): Dezimalstring, Zeitpunkte mit Offset, Sichtbarkeit ganze Firma. */
  private draft(): ChallengeDraftRequest {
    const v = this.form.getRawValue();
    return {
      title: v.title.trim(),
      description: v.description.trim() || null,
      metric: v.metric,
      target: v.target,
      startsAt: new Date(v.startsAt).toISOString(),
      endsAt: new Date(v.endsAt).toISOString(),
      visibility: 'company',
    };
  }

  private run<T>(call: import('rxjs').Observable<T>, next: (value: T) => void): void {
    this.busy.set(true);
    this.fehler.set(null);
    call.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (value) => {
        this.busy.set(false);
        next(value);
        this.laden();
      },
      error: (e: unknown) => {
        this.busy.set(false);
        this.melden(e);
      },
    });
  }

  private melden(error: unknown): void {
    if (error instanceof HttpErrorResponse) {
      const problem = toProblem(error.status, error.error);
      this.fehler.set(problem.detail === 'preview_required' ? 'verwaltung.challenges.vorschauPflicht' : error.status === 403 ? 'verwaltung.challenges.keineBerechtigung' : 'fehler.Unbekannt');
      return;
    }
    this.fehler.set('fehler.Unbekannt');
  }
}
