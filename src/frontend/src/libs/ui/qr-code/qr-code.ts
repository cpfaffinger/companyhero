// QR-Code (Zugang 2.1, 6.2, 6.4): Beitrittslink, persönliche Kiosk-Kennung, Übertragungslink. Gerendert im Client aus dem
// übergebenen Wert; der Wert selbst steht als Text daneben, damit er auch ohne Kamera nutzbar bleibt.
import { ChangeDetectionStrategy, Component, computed, input, resource } from '@angular/core';

@Component({
  selector: 'ch-qr-code',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (bild.value(); as src) {
      <img class="ch-qr__bild" [src]="src" [alt]="alt()" width="240" height="240" />
    }
    @if (zeigeWert()) {
      <code class="ch-qr__wert">{{ value() }}</code>
    }
  `,
  styles: `
    :host {
      display: inline-flex;
      flex-direction: column;
      align-items: center;
      gap: var(--ch-space-2);
    }

    .ch-qr__bild {
      border-radius: var(--ch-radius-card);
      background: var(--ch-surface);
      padding: var(--ch-space-2);
    }

    .ch-qr__wert {
      font: 600 var(--ch-type-body-m) / var(--ch-line-body-m) var(--ch-font-text);
      letter-spacing: 0.08em;
      word-break: break-all;
      text-align: center;
    }
  `,
})
export class QrCode {
  readonly value = input.required<string>();
  readonly alt = input<string>('');
  readonly zeigeWert = input(true);

  readonly bild = resource({
    params: () => this.value(),
    loader: async ({ params }) => {
      // qrcode ist CommonJS: je nach Interop liegt die Funktion am Namensraum oder am default-Export.
      const mod = (await import('qrcode')) as typeof import('qrcode') & { default?: typeof import('qrcode') };
      const toDataURL = mod.toDataURL ?? mod.default!.toDataURL;
      return toDataURL(params, { errorCorrectionLevel: 'M', margin: 1, width: 240 });
    },
  });

  readonly leer = computed(() => this.value().length === 0);
}
