// Kennzahlenkachel (Marke 2.5): Zahl mit Tabellenziffern in Inter Tight, Beschriftung darunter. Keine Nullwerte:
// die aufrufende Seite zeigt die Kachel nur mit vorhandenem Wert (Onboarding 1).
import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'ch-kennzahl',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ch-kennzahl__wert">{{ wert() }}</div>
    <div class="ch-kennzahl__label">{{ label() }}</div>
  `,
  styles: `
    :host {
      display: block;
      padding: var(--ch-space-3);
      border-radius: var(--ch-radius-input);
      background: var(--ch-surface-container);
    }

    .ch-kennzahl__wert {
      font: 700 var(--ch-type-metric-l) / var(--ch-line-metric-l) var(--ch-font-display);
      font-variant-numeric: tabular-nums;
    }

    .ch-kennzahl__label {
      font-size: var(--ch-type-label-m);
      line-height: var(--ch-line-label-m);
      color: var(--ch-on-surface-variant);
    }
  `,
})
export class Kennzahl {
  readonly wert = input.required<string>();
  readonly label = input.required<string>();
}
