// Darstellung von Dezimalstrings des Vertrags im Landesformat de-AT (Marke 2.3, K13): reine Zeichenkettenarbeit, keine
// Umwandlung in binäre Gleitkommazahlen. „49.00“ → „49,00“, „1234.5678“ → „1.234,5678“, „-5.96“ → „-5,96“.
export function formatDezimal(value: string, nachkommastellen?: number): string {
  const negativ = value.startsWith('-');
  const [ganz, bruch = ''] = (negativ ? value.slice(1) : value).split('.');
  const gruppen = ganz.replace(/\B(?=(\d{3})+(?!\d))/g, '.');
  const dezimal = nachkommastellen === undefined ? bruch : bruch.padEnd(nachkommastellen, '0').slice(0, nachkommastellen);
  return `${negativ ? '-' : ''}${gruppen}${dezimal ? ',' + dezimal : ''}`;
}

/** Betrag mit zwei Nachkommastellen und Euro-Zeichen. */
export function formatEuro(value: string): string {
  return `${formatDezimal(value, 2)} €`;
}

/** Menge mit vier Nachkommastellen, wie im Ledger geführt; ganze Mengen ohne Nachkommastellen. */
export function formatMenge(value: string): string {
  const [ganz, bruch = ''] = value.split('.');
  return /^0*$/.test(bruch) ? formatDezimal(ganz, 0) : formatDezimal(value);
}

/** Periode „YYYY-MM“ als „MM/YYYY“. */
export function formatPeriode(period: string): string {
  const [jahr, monat] = period.split('-');
  return `${monat}/${jahr}`;
}

/** Steuersatz „0.2000“ als „20“. */
export function formatProzent(rate: string): string {
  const [ganz, bruch = ''] = rate.split('.');
  const hundertstel = `${ganz}${bruch.padEnd(4, '0').slice(0, 4)}`;
  const prozent = hundertstel.replace(/^0+/, '').padStart(3, '0');
  const vorKomma = prozent.slice(0, -2).replace(/^0+(?=\d)/, '');
  const nachKomma = prozent.slice(-2).replace(/0+$/, '');
  return nachKomma ? `${vorKomma},${nachKomma}` : vorKomma;
}
