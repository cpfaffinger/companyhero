// Zugang in der Profilansicht (Zugang 4, 6.2, 7, 8; A-016, A-019): Anmeldewege verknüpfen und trennen (ein Weg bleibt),
// Wiederherstellungscode erneuern (genau einmal sichtbar), Kiosk-Kennung mit QR-Code und PIN, Abmeldung aus allen Sitzungen,
// Austritt mit Bestätigung. Alle Fachregeln prüft das Backend; der Client zeigt seine Antworten.
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatError, MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { ActivatedRoute, Router } from '@angular/router';
import { OfflineFreigabe, freigabeBis } from '../../../../libs/data-access/zugang/offline-freigabe';
import { webAuthnVerfuegbar } from '../../../../libs/data-access/zugang/webauthn';
import { ZugangApi, type AccessOverview, type ZugangFehler } from '../../../../libs/data-access/zugang/zugang.api';
import { TextService } from '../../../../libs/theme/text.service';
import { QrCode } from '../../../../libs/ui/qr-code/qr-code';
import { AustrittDialog } from './austritt-dialog';

@Component({
  selector: 'ch-zugang-einstellungen',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatButton, MatFormField, MatLabel, MatError, MatInput, QrCode],
  templateUrl: './zugang-einstellungen.html',
  styleUrl: './zugang-einstellungen.scss',
})
export class ZugangEinstellungen {
  protected readonly texts = inject(TextService);
  private readonly zugang = inject(ZugangApi);
  private readonly freigabe = inject(OfflineFreigabe);
  private readonly dialog = inject(MatDialog);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly access = signal<AccessOverview | null>(null);
  readonly fehler = signal<ZugangFehler | null>(null);
  readonly busy = signal(false);
  /** Nach Wiederherstellungscode oder Übertragung: Einrichtung eines Weges ist Pflicht (Zugang 3.1, 6.4). */
  readonly einrichten = this.route.snapshot.queryParamMap.get('einrichten') === '1';
  /** Ergebnis einer Anbieterverknüpfung (Zugang 4: eine Identität gehört höchstens einer Person). */
  readonly anbieterHinweis = anbieterKey(this.route.snapshot.queryParamMap.get('anbieter'));
  readonly neuerCode = signal<string | null>(null);
  readonly passkeyMoeglich = webAuthnVerfuegbar();
  readonly pinGesetzt = signal(false);

  readonly geraetename = new FormControl('', { nonNullable: true, validators: [Validators.maxLength(60)] });
  readonly email = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email] });
  readonly pin = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^\d{4}$/)] });

  readonly kennungGruppiert = computed(() => {
    const id = this.access()?.kioskId ?? '';
    return id.length === 6 ? `${id.slice(0, 3)} ${id.slice(3)}` : id;
  });
  readonly freigabeBis = computed(() => {
    const f = this.freigabe.aktuell();
    return f ? freigabeBis(new Date(f.letzterKontakt)) : null;
  });
  /** Ein Weg muss bleiben (Zugang 4): Trennen nur, wenn ein zweiter Weg besteht. */
  readonly mehrereWege = computed(() => {
    const a = this.access();
    return a ? a.passkeys.length + (a.email ? 1 : 0) + a.providers.length > 1 : false;
  });

  constructor() {
    this.laden();
  }

  linkUrl(key: string): string {
    return this.zugang.oidcStartUrl(key, 'link', this.router.url.split('?')[0]);
  }

  passkeyHinzufuegen(): void {
    this.run(this.zugang.addPasskey(this.geraetename.value.trim() || this.texts.t('zugang.passkey.diesesGeraet')), () => this.geraetename.reset());
  }

  passkeyEntfernen(id: string): void {
    this.run(this.zugang.removePasskey(id));
  }

  emailSetzen(): void {
    if (this.email.invalid) {
      this.email.markAsTouched();
      return;
    }
    this.run(this.zugang.setEmail(this.email.value.trim()), () => this.email.reset());
  }

  emailEntfernen(): void {
    this.run(this.zugang.removeEmail());
  }

  anbieterTrennen(linkId: string): void {
    this.run(this.zugang.unlinkProvider(linkId));
  }

  codeErneuern(): void {
    this.busy.set(true);
    this.zugang
      .renewRecoveryCode()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (code) => {
          this.busy.set(false);
          this.neuerCode.set(code);
          this.laden();
        },
        error: (e: ZugangFehler) => this.abgelehnt(e),
      });
  }

  pinSetzen(): void {
    if (this.pin.invalid) {
      this.pin.markAsTouched();
      return;
    }
    this.run(this.zugang.setKioskPin(this.pin.value), () => {
      this.pin.reset();
      this.pinGesetzt.set(true);
    });
  }

  abmelden(): void {
    this.zugang
      .logout()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: () => void this.router.navigateByUrl('/zugang'), error: () => void this.router.navigateByUrl('/zugang') });
  }

  ueberallAbmelden(): void {
    this.zugang
      .logoutAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: () => void this.router.navigateByUrl('/zugang'), error: (e: ZugangFehler) => this.abgelehnt(e) });
  }

  austreten(): void {
    this.dialog
      .open(AustrittDialog)
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((confirmed) => {
        if (confirmed === true) {
          this.zugang
            .leave()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({ next: () => void this.router.navigateByUrl('/zugang'), error: (e: ZugangFehler) => this.abgelehnt(e) });
        }
      });
  }

  private run(call: ReturnType<ZugangApi['removeEmail']>, done?: () => void): void {
    this.busy.set(true);
    this.fehler.set(null);
    call.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.busy.set(false);
        done?.();
        this.laden();
      },
      error: (e: ZugangFehler) => this.abgelehnt(e),
    });
  }

  private laden(): void {
    this.zugang
      .access()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (a) => this.access.set(a), error: () => undefined });
  }

  private abgelehnt(e: ZugangFehler): void {
    this.busy.set(false);
    this.fehler.set(e);
  }
}

function anbieterKey(value: string | null): string | null {
  switch (value) {
    case 'verknuepft':
      return 'ich.zugang.anbieterVerknuepft';
    case 'identitaet_vergeben':
      return 'zugang.fehler.identity_taken';
    default:
      return null;
  }
}
