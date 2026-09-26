import { Routes } from '@angular/router';

// Feature-Einstiege aus einer Codebasis (A-002, K17): Mitglieder-App unter /t/{tenant}, Kiosk unter /kiosk; Verwaltung
// lazy innerhalb der Mitglieder-Schale, nie im ersten Ladepaket. Der Platzhalter „_“ wird auf den Tenant der Sitzung umgeleitet.
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 't/_/start' },
  { path: 'kiosk', loadComponent: () => import('./features/kiosk/kiosk-page').then((m) => m.KioskPage) },
  { path: 't/:tenant', loadChildren: () => import('./features/mitglieder/mitglieder.routes').then((m) => m.MITGLIEDER_ROUTES) },
];
