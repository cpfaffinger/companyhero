import { Routes } from '@angular/router';

// Feature-Einstiege aus einer Codebasis (A-002, K17): Mitglieder-App unter /t/{tenant}, Kiosk unter /kiosk, Zugang unter
// /zugang (Anmeldung, Beitritt, Rollencode, Magic-Link, Übertragung) als eigener Lazy-Einstieg; Verwaltung lazy innerhalb der
// Mitglieder-Schale, nie im ersten Ladepaket. Der Platzhalter „_“ wird auf den Tenant der Sitzung umgeleitet. Der QR-Code eines
// Beitrittscodes enthält https://<Origin>/join/<Code> (Zugang 2.1) und führt in den Beitritt.
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 't/_/start' },
  { path: 'kiosk', loadComponent: () => import('./features/kiosk/kiosk-page').then((m) => m.KioskPage) },
  { path: 'join/:code', redirectTo: 'zugang/beitritt/:code' },
  { path: 'zugang', loadChildren: () => import('./features/zugang/zugang.routes').then((m) => m.ZUGANG_ROUTES) },
  { path: 't/:tenant', loadChildren: () => import('./features/mitglieder/mitglieder.routes').then((m) => m.MITGLIEDER_ROUTES) },
];
