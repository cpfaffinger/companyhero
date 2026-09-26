// Kiosk-Anmeldemaske (Marke 2.5 Spezifisch; Zugang 6.2, A-018): Kennung (sechs Ziffern), PIN (vier Ziffern) als
// Punkte, Ziffernblock; keine Namensliste, keine Namenssuche. Immer im Großflächenmodus (A-081). Die Anmeldung selbst
// (Gerätesitzung, Personensitzung, Drosselung) liefert Stufe 4; die Maske meldet nur Kennung und PIN nach außen.
import { ChangeDetectionStrategy, Component, computed, inject, output, signal } from '@angular/core';
import { TextService } from '../../theme/text.service';

export interface KioskAnmeldung {
  kennung: string;
  pin: string;
}

@Component({
  selector: 'ch-kiosk-anmeldemaske',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './kiosk-anmeldemaske.html',
  styleUrl: './kiosk-anmeldemaske.scss',
})
export class KioskAnmeldemaske {
  protected readonly texts = inject(TextService);
  readonly anmelden = output<KioskAnmeldung>();

  readonly kennung = signal('');
  readonly pin = signal('');
  readonly keys = ['1', '2', '3', '4', '5', '6', '7', '8', '9'];
  readonly pinSlots = [0, 1, 2, 3];

  /** Erst die Kennung (6 Ziffern), dann die PIN (4 Ziffern). */
  readonly stage = computed<'kennung' | 'pin'>(() => (this.kennung().length < 6 ? 'kennung' : 'pin'));
  readonly complete = computed(() => this.kennung().length === 6 && this.pin().length === 4);

  digit(value: string): void {
    if (this.stage() === 'kennung') {
      this.kennung.update((k) => (k + value).slice(0, 6));
    } else {
      this.pin.update((p) => (p + value).slice(0, 4));
    }
  }

  erase(): void {
    if (this.pin().length > 0) {
      this.pin.update((p) => p.slice(0, -1));
    } else {
      this.kennung.update((k) => k.slice(0, -1));
    }
  }

  onKennungInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value.replace(/\D/g, '').slice(0, 6);
    this.kennung.set(value);
    (event.target as HTMLInputElement).value = value;
  }

  submit(): void {
    if (this.complete()) {
      this.anmelden.emit({ kennung: this.kennung(), pin: this.pin() });
    }
  }
}
