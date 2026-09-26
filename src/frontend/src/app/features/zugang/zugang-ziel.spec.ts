import { zielNachAnmeldung } from './zugang-ziel';

describe('Ziel nach der Anmeldung (Zugang 3.1, 3.2, 6.4)', () => {
  const login = { tenantId: 't1', personId: 'p1', kind: 'member' as const, newRecoveryCode: null, mustSetUpAccess: false };

  it('führt nur zu lokalen Zielen der Mitglieder-App zurück, sonst zum Start des Tenants', () => {
    expect(zielNachAnmeldung(login, '/t/t1/challenges')).toBe('/t/t1/challenges');
    expect(zielNachAnmeldung(login, 'https://boese.example/t/x')).toBe('/t/t1/start');
    expect(zielNachAnmeldung(login, '//boese.example')).toBe('/t/t1/start');
    expect(zielNachAnmeldung(login, null)).toBe('/t/t1/start');
  });

  it('nach Wiederherstellungscode oder Übertragung folgt sofort die Einrichtung eines Weges', () => {
    expect(zielNachAnmeldung({ ...login, mustSetUpAccess: true }, '/t/t1/challenges')).toBe('/t/t1/ich?einrichten=1');
  });
});
