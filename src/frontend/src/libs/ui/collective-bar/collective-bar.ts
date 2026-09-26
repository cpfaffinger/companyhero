// Kollektivbalken mit Meilensteinen (Marke 2.5 Fortschritt; Challenges 6.2): Prozentwert vom Backend, Meilensteine bei
// 25, 50, 75 und 100 Prozent, Textentsprechung für Screenreader (Marke 7). Füllt sich in 320 ms; ohne Bewegung bei
// prefers-reduced-motion (Design-System). Farben nur über --ch-* Rollen.
import { ChangeDetectionStrategy, Component, computed, effect, ElementRef, inject, input, Renderer2, RendererStyleFlags2 } from '@angular/core';

export const MILESTONES = [25, 50, 75, 100] as const;

@Component({
  selector: 'ch-collective-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { role: 'img', '[attr.aria-label]': 'label()' },
  template: `
    <div class="ch-bar__track">
      <div class="ch-bar__fill"></div>
      @for (milestone of milestones; track milestone) {
        <div class="ch-bar__milestone" [class.ch-bar__milestone--reached]="percent() >= milestone" [class.ch-bar__milestone--next]="milestone === nextMilestone()" [attr.data-milestone]="milestone"></div>
      }
    </div>
    <div class="ch-bar__labels" aria-hidden="true">
      <span>{{ startLabel() }}</span>
      @for (milestone of milestones; track milestone) {
        <span [class.ch-bar__label--next]="milestone === nextMilestone()">{{ milestone === 100 ? goalLabel() : milestone + ' %' }}</span>
      }
    </div>
  `,
  styleUrl: './collective-bar.scss',
})
export class CollectiveBar {
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly renderer = inject(Renderer2);

  readonly percent = input.required<number>();
  readonly label = input.required<string>();
  readonly startLabel = input('Start');
  readonly goalLabel = input('Ziel');
  readonly milestones = MILESTONES;

  readonly nextMilestone = computed(() => this.milestones.find((m) => m > this.percent()) ?? 100);

  constructor() {
    effect(() => {
      const clamped = Math.max(0, Math.min(100, this.percent()));
      this.renderer.setStyle(this.element.nativeElement, '--ch-fill', `${clamped}%`, RendererStyleFlags2.DashCase);
    });
  }
}
