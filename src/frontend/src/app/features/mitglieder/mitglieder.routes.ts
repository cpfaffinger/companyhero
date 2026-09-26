import { Routes } from '@angular/router';
import { ChallengesKontext } from './challenges/challenges-kontext';
import { MitgliederShell } from './mitglieder-shell';

// Seiten der Mitglieder-App; Verwaltung nur als eigener Lazy-Einstieg, nie im ersten Ladepaket (K17, A-096).
export const MITGLIEDER_ROUTES: Routes = [
  {
    path: '',
    component: MitgliederShell,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'start' },
      { path: 'start', loadComponent: () => import('./start/start-page').then((m) => m.StartPage), data: { titleKey: 'nav.start' } },
      { path: 'challenges', loadComponent: () => import('./challenges/challenges-page').then((m) => m.ChallengesPage), data: { titleKey: 'nav.challenges', kontext: ChallengesKontext } },
      { path: 'ich', loadComponent: () => import('./ich/ich-page').then((m) => m.IchPage), data: { titleKey: 'nav.ich' } },
      { path: 'benachrichtigungen', loadComponent: () => import('./benachrichtigungen/benachrichtigungen-page').then((m) => m.BenachrichtigungenPage), data: { titleKey: 'kopf.benachrichtigungen' } },
      { path: 'verwaltung/marke', loadComponent: () => import('../verwaltung/marke/marke-page').then((m) => m.MarkePage), data: { titleKey: 'verwaltung.marke' } },
      { path: 'verwaltung/challenges', loadComponent: () => import('../verwaltung/challenges/challenges-verwaltung-page').then((m) => m.ChallengesVerwaltungPage), data: { titleKey: 'verwaltung.challenges' } },
      { path: 'verwaltung/abrechnung', loadComponent: () => import('../verwaltung/abrechnung/abrechnung-page').then((m) => m.AbrechnungPage), data: { titleKey: 'verwaltung.abrechnung' } },
    ],
  },
];
