// Leerzustand mit Handlungsvorschlag (Marke 2.5; Onboarding 1: Nullwerte sind verboten, Leerzustände zeigen die nächste Handlung).
import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'ch-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="ch-empty__text">{{ text() }}</p>
    <ng-content />
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--ch-space-3);
      padding: var(--ch-space-6);
      border-radius: var(--ch-radius-sheet);
      border: 1px solid var(--ch-outline-variant);
    }

    .ch-empty__text {
      color: var(--ch-on-surface-variant);
    }
  `,
})
export class EmptyState {
  readonly text = input.required<string>();
}
