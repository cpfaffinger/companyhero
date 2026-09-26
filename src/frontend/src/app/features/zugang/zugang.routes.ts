import { Routes } from '@angular/router';
import { ZugangShell } from './zugang-shell';

// Zugang (Zugang 2 bis 4, 6.4; A-078): Anmeldeseite vor Tenant-Zuordnung in der Plattformmarke, Beitritt mit Code, Rollencode,
// Magic-Link und Übertragung vom Kiosk. Eigener Lazy-Einstieg, nie im ersten Ladepaket der Mitglieder-App (K17).
export const ZUGANG_ROUTES: Routes = [
  {
    path: '',
    component: ZugangShell,
    children: [
      { path: '', pathMatch: 'full', loadComponent: () => import('./anmelden/anmelden-page').then((m) => m.AnmeldenPage) },
      { path: 'beitritt', loadComponent: () => import('./beitritt/beitritt-page').then((m) => m.BeitrittPage) },
      { path: 'beitritt/:code', loadComponent: () => import('./beitritt/beitritt-page').then((m) => m.BeitrittPage) },
      { path: 'rolle/:code', loadComponent: () => import('./beitritt/rolle-page').then((m) => m.RollePage) },
      { path: 'magic', loadComponent: () => import('./link/link-page').then((m) => m.LinkPage), data: { art: 'magic' } },
      { path: 'transfer', loadComponent: () => import('./link/link-page').then((m) => m.LinkPage), data: { art: 'transfer' } },
    ],
  },
];
