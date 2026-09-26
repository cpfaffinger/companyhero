// Verwaltung „Module und Kosten“ (Entitlements 4.2; Metering 5; A-064 bis A-066, A-073): Kostenvorschau mit Prognose und
// Testphasen-Hinweis, Modulkatalog mit Datenschutzhinweis und Kostenauswirkung vor dem Klick, Bündelvorschlag statt Ablehnung,
// Verbrauch als Tagesaggregate (personennahe Metriken nur als Summe), Rechnungsentwürfe, Definitionen der Metriken. Eigener
// Lazy-Einstieg (K17, A-096); Berechtigung und Fachregeln entscheidet das Backend (K14). Beträge bleiben Strings (K13).
import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButton } from '@angular/material/button';
import { Observable } from 'rxjs';
import { toProblem } from '../../../../libs/api-client/api-client';
import { AbrechnungApi, type CostPreview, type InvoiceDraft, type InvoiceSummary, type Simulation, type Usage } from '../../../../libs/data-access/abrechnung/abrechnung.api';
import { formatEuro, formatMenge, formatPeriode, formatProzent } from '../../../../libs/data-access/abrechnung/geld.format';
import { EntitlementsApi, type ModuleStatus } from '../../../../libs/data-access/entitlements/entitlements.api';
import { TextService } from '../../../../libs/theme/text.service';
import { Kennzahl } from '../../../../libs/ui/kennzahl/kennzahl';

/** Buchungsabsicht mit Simulation vor dem Klick (Entitlements 4.2 Nr. 2). */
interface Absicht {
  module: string;
  simulation: Simulation;
}

@Component({
  selector: 'ch-abrechnung-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButton, Kennzahl],
  templateUrl: './abrechnung-page.html',
  styleUrl: './abrechnung-page.scss',
})
export class AbrechnungPage {
  protected readonly texts = inject(TextService);
  private readonly abrechnung = inject(AbrechnungApi);
  private readonly entitlements = inject(EntitlementsApi);
  private readonly destroyRef = inject(DestroyRef);

  readonly preview = signal<CostPreview | null>(null);
  readonly modules = signal<ModuleStatus[] | null>(null);
  readonly usage = signal<Usage | null>(null);
  readonly invoices = signal<InvoiceSummary[] | null>(null);
  readonly invoice = signal<InvoiceDraft | null>(null);
  readonly absicht = signal<Absicht | null>(null);
  readonly buendel = signal<string[] | null>(null);
  readonly meldung = signal<string | null>(null);
  readonly fehler = signal<string | null>(null);
  readonly busy = signal(false);
  readonly offeneTage = signal<string | null>(null);

  readonly formatEuro = formatEuro;
  readonly formatMenge = formatMenge;
  readonly formatPeriode = formatPeriode;
  readonly formatProzent = formatProzent;

  readonly bewertetePositionen = computed(() => this.preview()?.lines.filter((l) => l.model !== 'revenue_share') ?? []);
  readonly hatTestphase = computed(() => (this.preview()?.wouldHaveBeen ?? '0.00') !== '0.00');
  readonly aktiveModule = computed(() => new Set((this.modules() ?? []).filter((m) => m.state === 'active' || m.state === 'trial' || m.state === 'expiring').map((m) => m.module)));

  constructor() {
    this.laden();
  }

  laden(): void {
    this.fehler.set(null);
    this.abrechnung.preview().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (p) => {
        this.preview.set(p);
        this.abrechnung.usage(p.period).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (u) => this.usage.set(u), error: (e: unknown) => this.melden(e) });
      },
      error: (e: unknown) => this.melden(e),
    });
    this.entitlements.modules().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (m) => this.modules.set(m), error: (e: unknown) => this.melden(e) });
    this.abrechnung.invoices().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (i) => this.invoices.set(i), error: (e: unknown) => this.melden(e) });
  }

  /** Klartextdefinition der Metrik aus dem Katalog (Metering 5): ausdrückliche Schlüssel je Metrik, keine zusammengesetzten Schlüssel im Template (A-080). */
  metrikText(metric: string): string {
    return this.texts.t(`metrik.${metric.replace('.', '_')}`);
  }

  statusText(status: string): string {
    return status === 'draft' ? this.texts.t('verwaltung.abrechnung.rechnung.status.draft') : status;
  }

  modulName(code: string): string {
    return this.texts.t(`modul.${code.toLowerCase()}`);
  }

  modulNamen(codes: readonly string[]): string {
    return codes.map((c) => this.modulName(c)).join(', ');
  }

  zustand(state: ModuleStatus['state']): string {
    switch (state) {
      case 'trial':
        return this.texts.t('verwaltung.abrechnung.modul.zustand.trial');
      case 'active':
        return this.texts.t('verwaltung.abrechnung.modul.zustand.active');
      case 'expiring':
        return this.texts.t('verwaltung.abrechnung.modul.zustand.expiring');
      default:
        return this.texts.t('verwaltung.abrechnung.modul.zustand.inactive');
    }
  }

  datum(value: string | null | undefined): string {
    return value ? new Intl.DateTimeFormat('de-AT', { dateStyle: 'medium', timeZone: 'Europe/Vienna' }).format(new Date(value)) : '';
  }

  exportUrl(): string {
    return this.abrechnung.exportUrl(this.preview()?.period ?? '');
  }

  /** Kosten vor dem Klick: erst die Simulation, dann die Bestätigung (Entitlements 4.2 Nr. 2). */
  buchenVorbereiten(module: string): void {
    this.buendel.set(null);
    this.run(this.abrechnung.simulate(module), (s) => this.absicht.set({ module, simulation: s }), false);
  }

  buchen(): void {
    const absicht = this.absicht();
    if (!absicht) {
      return;
    }
    this.run(this.entitlements.book(absicht.module), (b) => {
      this.absicht.set(null);
      if (b.outcome === 'bundle_suggested') {
        // Bündelvorschlag statt Ablehnung (Entitlements 4.2 Nr. 3).
        this.buendel.set([...b.suggestion]);
        return;
      }
      this.meldung.set(this.texts.t('verwaltung.abrechnung.gebucht', { module: this.modulNamen(b.booked.length ? b.booked : [absicht.module]) }));
    });
  }

  buendelBuchen(): void {
    const module = this.buendel();
    if (!module) {
      return;
    }
    this.run(this.entitlements.bookBundle(module), (b) => {
      this.buendel.set(null);
      this.meldung.set(this.texts.t('verwaltung.abrechnung.gebucht', { module: this.modulNamen(b.booked) }));
    });
  }

  abbrechen(): void {
    this.absicht.set(null);
    this.buendel.set(null);
  }

  testphase(module: string): void {
    this.run(this.entitlements.startTrial(module), (t) => {
      this.meldung.set(t.outcome === 'started' ? this.texts.t('verwaltung.abrechnung.testphaseGestartet', { datum: this.datum(t.trialUntil) }) : this.texts.t('verwaltung.abrechnung.testphaseVerbraucht'));
    });
  }

  kuendigen(module: string): void {
    this.run(this.entitlements.cancel(module), (c) => this.meldung.set(this.texts.t('verwaltung.abrechnung.gekuendigt', { datum: this.datum(c.activeUntil), module: this.modulNamen(c.affected) })));
  }

  ruecknahme(module: string): void {
    this.run(this.entitlements.revokeCancellation(module), (c) => this.meldung.set(this.texts.t('verwaltung.abrechnung.wiederhergestellt', { module: this.modulNamen(c.affected) })));
  }

  rechnungOeffnen(invoiceId: string): void {
    this.run(this.abrechnung.invoice(invoiceId), (i) => this.invoice.set(i), false);
  }

  tageUmschalten(metric: string): void {
    this.offeneTage.set(this.offeneTage() === metric ? null : metric);
  }

  private run<T>(call: Observable<T>, next: (value: T) => void, reload = true): void {
    this.busy.set(true);
    this.fehler.set(null);
    this.meldung.set(null);
    call.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (value) => {
        this.busy.set(false);
        next(value);
        if (reload) {
          this.laden();
        }
      },
      error: (e: unknown) => {
        this.busy.set(false);
        this.melden(e);
      },
    });
  }

  private melden(error: unknown): void {
    if (error instanceof HttpErrorResponse) {
      const problem = toProblem(error.status, error.error);
      this.fehler.set(problem.detail === 'fresh_login_required' ? 'verwaltung.abrechnung.frischeAnmeldung' : error.status === 403 ? 'verwaltung.abrechnung.keineBerechtigung' : 'fehler.Unbekannt');
      return;
    }
    this.fehler.set('fehler.Unbekannt');
  }
}
