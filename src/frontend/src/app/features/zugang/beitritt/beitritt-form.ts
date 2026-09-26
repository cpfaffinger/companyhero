// Gemeinsames Formular des Beitritts (Zugang 2.2; K10): Anzeigename oder Klarname, Sichtbarkeit ausdrücklich ohne Voreinstellung
// (A-022), Weg zur Sicherung des Zugangs; die Abbildung auf das DTO ist ausdrücklich.
import { FormControl, FormGroup, Validators } from '@angular/forms';
import type { Visibility } from '../../../../libs/data-access/zugang/zugang.api';

export type Weg = 'passkey' | 'email' | 'extern';

export function beitrittForm() {
  return new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(2), Validators.maxLength(80)] }),
    visibility: new FormControl<Visibility | null>(null, { validators: [Validators.required] }),
    weg: new FormControl<Weg>('passkey', { nonNullable: true }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.email] }),
    kioskPin: new FormControl('', { nonNullable: true, validators: [Validators.pattern(/^\d{4}$/)] }),
  });
}

export type BeitrittForm = ReturnType<typeof beitrittForm>;

export const VISIBILITIES: { value: Visibility; key: string }[] = [
  { value: 'only_me', key: 'sichtbarkeit.nurIch' },
  { value: 'team', key: 'sichtbarkeit.team' },
  { value: 'company', key: 'sichtbarkeit.firma' },
];
