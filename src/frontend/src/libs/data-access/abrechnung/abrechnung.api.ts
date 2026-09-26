// Datenzugriff der Domäne Metering und Abrechnung (Metering 5, 6; K11, K12, K13): Kostenvorschau, Simulation vor Buchung,
// Verbrauch als Tagesaggregate, Rechnungsentwürfe, Metrikdefinitionen. Beträge und Mengen bleiben Strings aus dem Vertrag; der
// Client rechnet nie mit Gleitkommazahlen und formatiert nur die Darstellung (A-013-Folge zur Dezimalpräzision).
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from '../../api-client/api-client';
import type { components } from '../../api-client/generated/api';

export type CostPreview = components['schemas']['CostPreviewResponse'];
export type InvoiceLine = components['schemas']['InvoiceLineResponse'];
export type Simulation = components['schemas']['SimulationResponse'];
export type Usage = components['schemas']['UsageResponse'];
export type UsageMetric = components['schemas']['UsageMetricResponse'];
export type InvoiceSummary = components['schemas']['InvoiceSummaryResponse'];
export type InvoiceDraft = components['schemas']['InvoiceDraftResponse'];
export type MetricDefinition = components['schemas']['MetricDefinitionResponse'];

@Injectable({ providedIn: 'root' })
export class AbrechnungApi {
  private readonly api = inject(ApiClient);

  preview(): Observable<CostPreview> {
    return this.api.get('/api/billing/preview');
  }

  /** Auswirkung einer Buchung vor dem Klick (Entitlements 4.2 Nr. 2, Metering 5). */
  simulate(module: string): Observable<Simulation> {
    return this.api.get('/api/billing/preview/simulate', { query: { module } });
  }

  usage(period: string): Observable<Usage> {
    return this.api.get('/api/billing/usage', { query: { period } });
  }

  /** Export als CSV liefert dieselben Tagesaggregate (A-023); der Browser lädt ihn direkt. */
  exportUrl(period: string): string {
    return `/api/billing/usage/export?period=${encodeURIComponent(period)}`;
  }

  invoices(): Observable<InvoiceSummary[]> {
    return this.api.get('/api/billing/invoices');
  }

  invoice(invoiceId: string): Observable<InvoiceDraft> {
    return this.api.get('/api/billing/invoices/{invoiceId}', { path: { invoiceId } });
  }
}
