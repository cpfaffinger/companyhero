// Fortschrittsring und der eine Erfolgsmoment (Marke 2.4, 2.5; A-076): der Ring füllt sich in 320 ms, ein kurzer warmer
// Aufleuchtimpuls in progress; kein Konfetti, kein Ton. Bei prefers-reduced-motion entfällt beides (Design-System).
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

const RADIUS = 20;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;

@Component({
  selector: 'ch-progress-ring',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { role: 'img', '[attr.aria-label]': 'label()', '[class.ch-ring--celebrate]': 'celebrate()' },
  template: `
    <svg viewBox="0 0 48 48" aria-hidden="true">
      <circle class="ch-ring__track" cx="24" cy="24" [attr.r]="radius" />
      <circle class="ch-ring__fill" cx="24" cy="24" [attr.r]="radius" [attr.stroke-dasharray]="circumference" [attr.stroke-dashoffset]="offset()" />
    </svg>
  `,
  styleUrl: './progress-ring.scss',
})
export class ProgressRing {
  readonly percent = input.required<number>();
  readonly label = input.required<string>();
  readonly celebrate = input(false);
  readonly radius = RADIUS;
  readonly circumference = CIRCUMFERENCE;
  readonly offset = computed(() => CIRCUMFERENCE * (1 - Math.max(0, Math.min(100, this.percent())) / 100));
}
