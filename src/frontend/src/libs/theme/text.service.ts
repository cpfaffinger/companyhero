// Texte aus dem Textkatalog des Operators in Tonalität und Anrede des Tenants (A-080, Marke 6). Das Frontend schreibt keine
// Systemtexte; es setzt nur Platzhalter für Bezüge ein und formatiert Zahlen im Landesformat (de-AT).
import { computed, inject, Injectable } from '@angular/core';
import { ThemeService } from './theme.service';

@Injectable({ providedIn: 'root' })
export class TextService {
  private readonly theme = inject(ThemeService);
  private readonly numberFormat = new Intl.NumberFormat('de-AT', { useGrouping: true });

  readonly anrede = computed(() => this.theme.anrede());

  /** Text zum Schlüssel; fehlt er, erscheint der Schlüssel selbst, damit Lücken in der Prüfliste des Operators auffallen. */
  t(key: string, params?: Record<string, string | number>): string {
    const text = this.theme.texte()[key] ?? key;
    if (!params) {
      return text;
    }
    return text.replace(/\{(\w+)\}/g, (match, name: string) => {
      const value = params[name];
      return value === undefined ? match : typeof value === 'number' ? this.formatNumber(value) : value;
    });
  }

  /** Landesformat de-AT laut Marke 2.3: Tausenderpunkt und Dezimalkomma, unabhängig von der ICU-Version der Laufzeit. */
  formatNumber(value: number): string {
    return this.numberFormat
      .formatToParts(value)
      .map((part) => (part.type === 'group' ? '.' : part.type === 'decimal' ? ',' : part.value))
      .join('');
  }

  has(key: string): boolean {
    return key in this.theme.texte();
  }
}
