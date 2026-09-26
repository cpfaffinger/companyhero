import { formatDezimal, formatEuro, formatMenge, formatPeriode, formatProzent } from './geld.format';

describe('Geldformate (K13, A-013-Folge): Dezimalstrings ohne Gleitkomma im Landesformat', () => {
  it('formatiert Beträge mit Tausenderpunkt und Dezimalkomma', () => {
    expect(formatEuro('49.00')).toBe('49,00 €');
    expect(formatEuro('1234.5')).toBe('1.234,50 €');
    expect(formatEuro('-5.96')).toBe('-5,96 €');
    expect(formatEuro('0.00')).toBe('0,00 €');
    expect(formatDezimal('1234567.8901')).toBe('1.234.567,8901');
  });

  it('zeigt Mengen ohne unnötige Nachkommastellen, Bruchteile aber vollständig', () => {
    expect(formatMenge('3.0000')).toBe('3');
    expect(formatMenge('0.5000')).toBe('0,5000');
    expect(formatMenge('1250.0000')).toBe('1.250');
  });

  it('formatiert Periode und Steuersatz', () => {
    expect(formatPeriode('2026-09')).toBe('09/2026');
    expect(formatProzent('0.2000')).toBe('20');
    expect(formatProzent('0.1000')).toBe('10');
    expect(formatProzent('0.0750')).toBe('7,5');
  });

  it('verändert den numerischen Wert nie (kein Runden durch Gleitkomma)', () => {
    expect(formatDezimal('0.1')).toBe('0,1');
    expect(formatDezimal('9007199254740993.00')).toBe('9.007.199.254.740.993,00');
  });
});
