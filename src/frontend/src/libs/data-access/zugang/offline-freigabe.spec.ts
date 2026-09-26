import { TestBed } from '@angular/core/testing';
import { FREIGABE_TAGE, freigabeBis, gehoertZu, gilt, OfflineFreigabe, type Freigabe } from './offline-freigabe';

describe('Offline-Freigabe (Zugang 7, A-019)', () => {
  const kontakt = new Date('2026-09-26T08:00:00+02:00');
  const freigabe: Freigabe = { tenantId: 't1', personId: 'p1', letzterKontakt: kontakt.toISOString() };

  beforeEach(() => localStorage.clear());

  it('gilt sieben Tage ab dem letzten Serverkontakt und läuft danach aus', () => {
    expect(FREIGABE_TAGE).toBe(7);
    expect(freigabeBis(kontakt).toISOString()).toBe('2026-10-03T06:00:00.000Z');
    expect(gilt(freigabe, new Date('2026-10-03T05:59:59+00:00'))).toBe(true);
    expect(gilt(freigabe, new Date('2026-10-03T06:00:00+00:00'))).toBe(false);
    expect(gilt(null, kontakt)).toBe(false);
  });

  it('wartende Beiträge gehören zu derselben Person im selben Tenant, nie zu einer anderen', () => {
    expect(gehoertZu(freigabe, 't1', 'p1')).toBe(true);
    expect(gehoertZu(freigabe, 't1', 'p2')).toBe(false);
    expect(gehoertZu(freigabe, 't2', 'p1')).toBe(false);
  });

  it('jeder Serverkontakt verlängert, der Austritt beendet; gespeichert wird nur ein Zeitpunkt, kein Geheimnis', () => {
    const service = TestBed.inject(OfflineFreigabe);
    expect(service.gilt(kontakt)).toBe(false);
    service.verlaengern('t1', 'p1', kontakt);
    expect(service.gilt(new Date(kontakt.getTime() + 6 * 86_400_000))).toBe(true);
    const gespeichert = JSON.parse(localStorage.getItem('ch.zugang.freigabe')!) as Record<string, unknown>;
    expect(Object.keys(gespeichert).sort()).toEqual(['letzterKontakt', 'personId', 'tenantId']);
    service.verlaengern('t1', 'p1', new Date(kontakt.getTime() + 6 * 86_400_000));
    expect(service.gilt(new Date(kontakt.getTime() + 12 * 86_400_000))).toBe(true);
    service.beenden();
    expect(service.gilt(kontakt)).toBe(false);
    expect(localStorage.getItem('ch.zugang.freigabe')).toBeNull();
  });
});
