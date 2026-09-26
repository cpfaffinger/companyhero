// Kiosk (Zugang 6, A-005, A-018, A-081): immer im Großflächenmodus. Ohne Gerätesitzung nur die Registrierung mit dem Code der
// Verwaltung; mit Gerätesitzung Marke, Kollektivstand, Beitritt, PIN-Neusetzung und die Anmeldemaske (Kennung, PIN, kein
// Namensfeld). Die Personensitzung zeigt einen sichtbaren Countdown, den jede bestätigte Eingabe zurücksetzt; am Ende werden
// Anzeige und Zustand geleert (Personenwechsel ohne Rest). Beiträge laufen über die serverseitig reservierte Vorgangskennung.
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatRadioButton, MatRadioGroup } from '@angular/material/radio';
import { switchMap } from 'rxjs';
import { BrandingApi } from '../../../libs/data-access/branding/branding.api';
import { ChallengesApi, type ChallengeCard, type ContributionError } from '../../../libs/data-access/challenges/challenges.api';
import { KioskApi, restsekunden, verlaengert, type JoinResponse, type KioskPerson, type Transfer } from '../../../libs/data-access/zugang/kiosk.api';
import type { Visibility, ZugangFehler } from '../../../libs/data-access/zugang/zugang.api';
import { TextService } from '../../../libs/theme/text.service';
import { ThemeService } from '../../../libs/theme/theme.service';
import { CollectiveBar } from '../../../libs/ui/collective-bar/collective-bar';
import { KioskAnmeldemaske, type KioskAnmeldung } from '../../../libs/ui/kiosk-anmeldemaske/kiosk-anmeldemaske';
import { QrCode } from '../../../libs/ui/qr-code/qr-code';

type Ansicht = 'laden' | 'registrieren' | 'anmelden' | 'person' | 'beitritt' | 'pinReset';

/** Unter dieser Restzeit weist die Oberfläche auf den Verlust unbestätigter Eingaben hin (Zugang 6.5). */
export const WARNUNG_AB_SEKUNDEN = 10;

@Component({
  selector: 'ch-kiosk-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatButton, MatFormField, MatLabel, MatError, MatHint, MatInput, MatRadioGroup, MatRadioButton, KioskAnmeldemaske, CollectiveBar, QrCode],
  templateUrl: './kiosk-page.html',
  styleUrl: './kiosk-page.scss',
})
export class KioskPage {
  protected readonly texts = inject(TextService);
  protected readonly theme = inject(ThemeService);
  private readonly branding = inject(BrandingApi);
  private readonly challenges = inject(ChallengesApi);
  private readonly kiosk = inject(KioskApi);
  private readonly destroyRef = inject(DestroyRef);

  readonly ansicht = signal<Ansicht>('laden');
  readonly hinweis = signal<string | null>(null);
  readonly fehler = signal<string | null>(null);
  readonly busy = signal(false);
  readonly cards = signal<ChallengeCard[] | null>(null);
  readonly person = signal<KioskPerson | null>(null);
  readonly now = signal(new Date());
  readonly transfer = signal<Transfer | null>(null);
  readonly beitrittErgebnis = signal<JoinResponse | null>(null);
  readonly beigetragen = signal<Set<string>>(new Set());
  readonly device = this.kiosk.device;

  readonly rest = computed(() => {
    const p = this.person();
    return p ? restsekunden(p, this.now()) : 0;
  });
  readonly warnung = computed(() => this.person() !== null && this.rest() <= WARNUNG_AB_SEKUNDEN);
  readonly transferRest = computed(() => {
    const t = this.transfer();
    return t ? Math.max(0, Math.ceil((new Date(t.expiresAt).getTime() - this.now().getTime()) / 1000)) : 0;
  });

  readonly registrierung = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(8)] });
  readonly beitritt = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(8)] }),
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(2), Validators.maxLength(80)] }),
    visibility: new FormControl<Visibility | null>(null, { validators: [Validators.required] }),
    pin: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^\d{4}$/)] }),
  });
  readonly pinReset = new FormGroup({
    kioskId: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^\d{6}$/)] }),
    recoveryCode: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(12)] }),
    pin: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^\d{4}$/)] }),
  });
  readonly neuePin = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/^\d{4}$/)] });
  readonly visibilities: { value: Visibility; key: string }[] = [
    { value: 'only_me', key: 'sichtbarkeit.nurIch' },
    { value: 'team', key: 'sichtbarkeit.team' },
    { value: 'company', key: 'sichtbarkeit.firma' },
  ];

  constructor() {
    this.theme.context.set('tenant');
    this.theme.forcedScale.set('gross');
    const timer = setInterval(() => {
      this.now.set(new Date());
      if (this.person() && this.rest() === 0) {
        // Ende der Personensitzung: Sitzungsspeicher und Anzeige werden geleert (Zugang 6.5, A-005).
        this.personBeenden(false);
      }
      if (this.transfer() && this.transferRest() === 0) {
        this.transfer.set(null);
      }
    }, 1000);
    this.destroyRef.onDestroy(() => clearInterval(timer));
    this.geraetLaden();
  }

  private geraetLaden(): void {
    this.kiosk
      .loadDevice()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (device) => {
          if (device) {
            this.geraetBereit();
          } else {
            this.ohneGeraet();
          }
        },
        error: () => this.ohneGeraet(),
      });
  }

  /** Ohne Gerätesitzung gibt es keinen Tenant: Plattformmarke und Plattformtexte, nur die Registrierung (Zugang 6.1). */
  private ohneGeraet(): void {
    this.theme.context.set('platform');
    this.branding.platformTheme().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => undefined });
    this.ansicht.set('registrieren');
  }

  private geraetBereit(): void {
    this.theme.context.set('tenant');
    this.ansicht.set('anmelden');
    this.branding.tenantTheme().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => undefined });
    this.challenges
      .refresh()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (cards) => this.cards.set(cards), error: () => this.cards.set([]) });
  }

  registrieren(): void {
    if (this.registrierung.invalid) {
      this.registrierung.markAsTouched();
      return;
    }
    this.start();
    this.kiosk
      .register(this.registrierung.value.trim().toUpperCase())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.geraetBereit();
        },
        error: (e: ZugangFehler) => this.abgelehnt(e.key),
      });
  }

  anmelden(anmeldung: KioskAnmeldung): void {
    this.start();
    this.kiosk
      .login(anmeldung.kennung, anmeldung.pin)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (person) => {
          this.busy.set(false);
          this.person.set(person);
          this.beigetragen.set(new Set());
          this.transfer.set(null);
          this.ansicht.set('person');
        },
        error: (e: ZugangFehler) => this.abgelehnt(e.key),
      });
  }

  /** Häkchen-Beitrag am Kiosk (Zugang 6.3, A-005): Vorgangskennung reservieren, dann eintragen; jede Bestätigung verlängert. */
  beitragen(card: ChallengeCard): void {
    this.start();
    this.challenges
      .reserveOperation(card.challengeId)
      .pipe(
        switchMap((op) => this.challenges.submit(card.challengeId, this.challenges.toKioskRequest(op.operationId, new Date()))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => {
          this.busy.set(false);
          this.eingabeBestaetigt();
          // Ausgänge des Vertrags: recorded oder already_recorded (gleiche Vorgangskennung, A-005); Ablehnungen kommen als 422.
          void result.outcome;
          this.beigetragen.update((set) => new Set(set).add(card.challengeId));
          this.hinweis.set(this.texts.t('kiosk.beitragErfasst'));
        },
        error: (e: ContributionError) => this.abgelehnt(e.key),
      });
  }

  uebertragen(): void {
    this.start();
    this.kiosk
      .transfer()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (transfer) => {
          this.busy.set(false);
          this.eingabeBestaetigt();
          this.transfer.set(transfer);
        },
        error: (e: ZugangFehler) => this.abgelehnt(e.key),
      });
  }

  pinAendern(): void {
    if (this.neuePin.invalid) {
      this.neuePin.markAsTouched();
      return;
    }
    this.start();
    this.kiosk
      .changePin(this.neuePin.value)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.eingabeBestaetigt();
          this.neuePin.reset();
          this.hinweis.set(this.texts.t('ich.zugang.pinGespeichert'));
        },
        error: (e: ZugangFehler) => this.abgelehnt(e.key),
      });
  }

  abmelden(): void {
    this.personBeenden(true);
  }

  zumBeitritt(): void {
    this.fehler.set(null);
    this.hinweis.set(null);
    this.beitrittErgebnis.set(null);
    this.ansicht.set('beitritt');
  }

  zurPinNeusetzung(): void {
    this.fehler.set(null);
    this.hinweis.set(null);
    this.ansicht.set('pinReset');
  }

  zurAnmeldung(): void {
    this.fehler.set(null);
    this.hinweis.set(null);
    this.beitritt.reset({ code: '', name: '', visibility: null, pin: '' });
    this.pinReset.reset({ kioskId: '', recoveryCode: '', pin: '' });
    this.beitrittErgebnis.set(null);
    this.ansicht.set('anmelden');
  }

  beitreten(): void {
    if (this.beitritt.invalid) {
      this.beitritt.markAllAsTouched();
      return;
    }
    const v = this.beitritt.getRawValue();
    this.start();
    this.kiosk
      .joinAtKiosk(v.code.trim().toUpperCase(), { displayName: v.name.trim(), visibility: v.visibility!, pin: v.pin, groups: null })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.busy.set(false);
          this.beitrittErgebnis.set(result);
        },
        error: (e: ZugangFehler) => this.abgelehnt(e.key),
      });
  }

  pinNeuSetzen(): void {
    if (this.pinReset.invalid) {
      this.pinReset.markAllAsTouched();
      return;
    }
    const v = this.pinReset.getRawValue();
    this.start();
    this.kiosk
      .resetPin(v.kioskId, v.recoveryCode.trim().toUpperCase(), v.pin)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.zurAnmeldung();
          this.hinweis.set(this.texts.t('kiosk.pinNeuGesetzt'));
        },
        error: (e: ZugangFehler) => this.abgelehnt(e.key),
      });
  }

  private eingabeBestaetigt(): void {
    const p = this.person();
    if (p) {
      this.person.set(verlaengert(p, new Date()));
    }
  }

  private personBeenden(explizit: boolean): void {
    this.person.set(null);
    this.transfer.set(null);
    this.beigetragen.set(new Set());
    this.neuePin.reset();
    this.hinweis.set(explizit ? null : this.texts.t('kiosk.sitzungBeendet'));
    this.fehler.set(null);
    this.ansicht.set('anmelden');
    this.kiosk
      .logout()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ error: () => undefined });
  }

  private start(): void {
    this.busy.set(true);
    this.fehler.set(null);
    this.hinweis.set(null);
  }

  private abgelehnt(key: string): void {
    this.busy.set(false);
    this.fehler.set(this.texts.t(key));
  }
}
