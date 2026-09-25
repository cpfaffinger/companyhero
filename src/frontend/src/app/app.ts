import { ChangeDetectionStrategy, Component, signal } from '@angular/core';

@Component({
  selector: 'ch-root',
  templateUrl: './app.html',
  styleUrl: './app.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  protected readonly title = signal('CompanyHero');
}
