// Datenzugriff des Feeds (Feed 2, 3.2; K11, K12): Kopfkarte, Check-in-Karte, Stream mit Karten aus dem Backend; der Client
// verdichtet nichts und filtert nichts nach Sichtbarkeit, das tut die zentrale Leseregel im Backend (A-022). ETag: unveränderter
// Feed liefert 304 ohne Nutzdaten (Feed 2.6), der Client zeigt dann keinen Hinweis auf neue Einträge.
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { catchError, map, Observable, of, throwError } from 'rxjs';
import { ApiClient, toProblem, type ApiProblem } from '../../api-client/api-client';
import type { components } from '../../api-client/generated/api';

export type FeedResponse = components['schemas']['FeedResponse'];
export type FeedCard = components['schemas']['FeedCardResponse'];
export type HeadCard = components['schemas']['HeadCardResponse'];

/** Stabile Fehlerkennung für die Oberfläche: Textschlüssel im Katalog. */
export interface FeedFehler {
  key: string;
  status: number;
}

@Injectable({ providedIn: 'root' })
export class FeedApi {
  private readonly api = inject(ApiClient);
  private readonly http = inject(HttpClient);
  private etag: string | null = null;

  readonly feed = signal<FeedResponse | null>(null);
  /** Der Feed hat sich seit der letzten Anzeige geändert (Hinweis „Neue Einträge“, Feed 2.6). */
  readonly neu = signal(false);

  /** Erste Ladung oder manuelles Ziehen: ersetzt den Stand. */
  load(): Observable<FeedResponse> {
    return this.http.get<FeedResponse>('/api/feed', { withCredentials: true, observe: 'response' }).pipe(
      map((response) => {
        this.etag = response.headers.get('ETag');
        this.neu.set(false);
        this.feed.set(response.body!);
        return response.body!;
      }),
    );
  }

  /** Abfrage im Vordergrund mit ETag: 304 bedeutet unverändert; sonst wird nur der Hinweis gesetzt, der Stand bleibt bis zum Ziehen. */
  pruefen(): Observable<boolean> {
    const headers = this.etag ? new HttpHeaders({ 'If-None-Match': this.etag }) : undefined;
    return this.http.get<FeedResponse>('/api/feed', { withCredentials: true, observe: 'response', headers }).pipe(
      map((response) => {
        const changed = response.status !== 304 && response.headers.get('ETag') !== this.etag;
        if (changed) {
          this.neu.set(true);
        }
        return changed;
      }),
      catchError((error: unknown) => (error instanceof HttpErrorResponse && error.status === 304 ? of(false) : throwError(() => error))),
    );
  }

  /** Mitglieder-Beitrag (Feed 3.2): Text bis 1.000 Zeichen; „Nur für mich“ lehnt das Backend mit 422 ab. */
  post(body: string): Observable<string> {
    return this.api.post('/api/feed/posts', { body }).pipe(
      map((r) => r.id),
      catchError((error: unknown) => throwError(() => toFeedFehler(error))),
    );
  }

  remove(entryId: string): Observable<void> {
    return this.api.delete('/api/feed/posts/{entryId}', { path: { entryId } }).pipe(map(() => undefined));
  }
}

function toFeedFehler(error: unknown): FeedFehler {
  if (error instanceof HttpErrorResponse) {
    const problem: ApiProblem = toProblem(error.status, error.error);
    if (error.status === 422 && problem.detail === 'visibility_only_me') {
      return { key: 'feed.nurFuerMich', status: 422 };
    }
    if (error.status === 429) {
      return { key: 'feed.tageslimit', status: 429 };
    }
    return { key: error.status === 0 ? 'fehler.Verbindung' : 'fehler.Unbekannt', status: error.status };
  }
  return { key: 'fehler.Unbekannt', status: 0 };
}
