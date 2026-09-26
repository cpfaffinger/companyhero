// Weiterleitung nach einer Anmeldung: nur lokale Ziele innerhalb der Mitglieder-App (Zugang 3.2), sonst der Start des Tenants;
// nach Wiederherstellungscode oder Übertragung führt der Weg sofort zur Einrichtung (Zugang 3.1, 6.4).
import type { LoginResponse } from '../../../libs/data-access/zugang/zugang.api';

export function zielNachAnmeldung(login: LoginResponse, zurueck: string | null): string {
  if (login.mustSetUpAccess) {
    return `/t/${login.tenantId}/ich?einrichten=1`;
  }
  if (zurueck && /^\/t\/[^/]+(\/|$)/.test(zurueck) && !zurueck.startsWith('//')) {
    return zurueck;
  }
  return `/t/${login.tenantId}/start`;
}
