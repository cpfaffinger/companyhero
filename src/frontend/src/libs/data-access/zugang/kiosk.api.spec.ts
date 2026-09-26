import { restsekunden, verlaengert, type KioskPerson } from './kiosk.api';

describe('Kiosk-Personensitzung im Client (Zugang 6.5, A-005)', () => {
  const start = new Date('2026-09-26T10:00:00Z');
  const person: KioskPerson = {
    personId: 'p',
    displayName: 'Anna',
    idleSeconds: 60,
    idleUntil: new Date(start.getTime() + 60_000),
    absoluteUntil: new Date(start.getTime() + 10 * 60_000),
  };

  it('zählt sichtbar herunter und endet bei null, nie negativ', () => {
    expect(restsekunden(person, start)).toBe(60);
    expect(restsekunden(person, new Date(start.getTime() + 59_500))).toBe(1);
    expect(restsekunden(person, new Date(start.getTime() + 61_000))).toBe(0);
  });

  it('jede Eingabe setzt den Countdown zurück, nie über das absolute Ende von zehn Minuten hinaus', () => {
    const later = verlaengert(person, new Date(start.getTime() + 30_000));
    expect(restsekunden(later, new Date(start.getTime() + 30_000))).toBe(60);
    const nearEnd = verlaengert(person, new Date(start.getTime() + 9 * 60_000 + 30_000));
    expect(restsekunden(nearEnd, new Date(start.getTime() + 9 * 60_000 + 30_000))).toBe(30);
  });
});
