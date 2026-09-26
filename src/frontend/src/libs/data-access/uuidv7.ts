// A-009, K15: Idempotenzschlüssel für offline erfasste Beiträge als zeitlich sortierbare eindeutige Kennung (UUIDv7).
// Erzeugt der Client; bleibt bei Wiederholungen unverändert. Der Server dedupliziert im selben Namensraum wie Vorgangskennungen.
export function uuidv7(now: number = Date.now(), random: (bytes: Uint8Array<ArrayBuffer>) => void = fillRandom): string {
  const bytes = new Uint8Array(new ArrayBuffer(16));
  random(bytes);
  const ms = BigInt(now);
  for (let i = 0; i < 6; i++) {
    bytes[i] = Number((ms >> BigInt(8 * (5 - i))) & 0xffn);
  }
  bytes[6] = (bytes[6] & 0x0f) | 0x70;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

function fillRandom(bytes: Uint8Array<ArrayBuffer>): void {
  globalThis.crypto.getRandomValues(bytes);
}
