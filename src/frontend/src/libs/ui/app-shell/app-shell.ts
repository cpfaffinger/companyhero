// Schale der Mitglieder-App (Marke 2.4, 2.5 Struktur; K07): kollabierende Kopfzeile mit Glocke, untere Navigationsleiste
// unter 640 px, Navigations-Rail ab 640 px, Seitennavigation ab 1024 px mit Inhalt bis 1.280 px und Kontextspalte 320 px.
// Navigation im Durchstich: Start, Challenges, Ich (keine Platzhalter für nicht gebuchte Module, A-064). Marke aus dem
// Theme-Service: Tenant-Logo als Kennzeichen, Plattform-Bildzeichen in der Plattform-Schale (A-078).
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TextService } from '../../theme/text.service';
import { ThemeService } from '../../theme/theme.service';

export interface NavItem {
  path: string;
  icon: string;
  labelKey: string;
}

@Component({
  selector: 'ch-app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatIconButton, RouterLink, RouterLinkActive],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss',
})
export class AppShell {
  protected readonly texts = inject(TextService);
  protected readonly theme = inject(ThemeService);

  /** Basis der Navigationspfade, etwa /t/{tenant}. */
  readonly base = input.required<string>();
  readonly items = input<NavItem[]>([
    { path: 'start', icon: 'home', labelKey: 'nav.start' },
    { path: 'challenges', icon: 'flag', labelKey: 'nav.challenges' },
    { path: 'ich', icon: 'person', labelKey: 'nav.ich' },
  ]);
  readonly title = input<string>('');
  readonly hasContext = input(false);

  readonly initial = computed(() => this.theme.produktname().trim().charAt(0).toUpperCase() || 'C');
  readonly platform = computed(() => this.theme.context() === 'platform');
}
