// K12 / A-009: Der einzige Weg zur API. Die Typen kommen ausschließlich aus dem generierten Vertrag
// (generated/api.ts, erzeugt mit npm run api:generate, nie von Hand geändert). Diese Datei ist ein generischer,
// typisierter Transport über Angular HttpClient (K11: HTTP in der Datenzugriffsschicht mit RxJS); sie kennt keine
// einzelnen Endpunkte und dupliziert keine DTOs. Dezimalwerte und Kennungen bleiben Strings (K13).
import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import type { paths } from './generated/api';

type HttpMethod = 'get' | 'post' | 'put' | 'delete';

/** Pfade, die eine Operation mit der Methode M haben. */
export type PathsWith<M extends HttpMethod> = {
  [P in keyof paths]: paths[P][M] extends undefined ? never : P;
}[keyof paths];

type Operation<P extends keyof paths, M extends HttpMethod> = NonNullable<paths[P][M]>;

type JsonContent<C> = C extends { content: { 'application/json': infer B } } ? B : never;

/** Antwortkörper der Erfolgsantworten (200, 201) einer Operation. */
export type SuccessBody<P extends keyof paths, M extends HttpMethod> =
  Operation<P, M> extends { responses: infer R }
    ? JsonContent<R[Extract<keyof R, 200 | 201>]>
    : never;

/** Anfragekörper einer Operation, sofern sie einen hat. */
export type RequestBody<P extends keyof paths, M extends HttpMethod> =
  Operation<P, M> extends { requestBody: { content: { 'application/json': infer B } } } ? B : never;

/** Pfadparameter einer Operation, sofern sie welche hat. */
export type PathParams<P extends keyof paths, M extends HttpMethod> =
  Operation<P, M> extends { parameters: { path: infer Q } } ? (Q extends undefined ? never : Q) : never;

type ParamsArg<P extends keyof paths, M extends HttpMethod> = [PathParams<P, M>] extends [never]
  ? { path?: undefined; query?: Record<string, string> }
  : { path: PathParams<P, M>; query?: Record<string, string> };

/** Fehlerkennung und -grund aus einer Problem-Details-Antwort (RFC 9457), wie sie die API liefert. */
export interface ApiProblem {
  status: number;
  title?: string | null;
  detail?: string | null;
  errors?: Record<string, string[]>;
}

@Injectable({ providedIn: 'root' })
export class ApiClient {
  private readonly http = inject(HttpClient);

  get<P extends PathsWith<'get'>>(path: P, params?: ParamsArg<P, 'get'>): Observable<SuccessBody<P, 'get'>> {
    return this.http.get<SuccessBody<P, 'get'>>(expand(path, params?.path), this.options(params?.query));
  }

  post<P extends PathsWith<'post'>>(path: P, body: RequestBody<P, 'post'> | null, params?: ParamsArg<P, 'post'>): Observable<SuccessBody<P, 'post'>> {
    return this.http.post<SuccessBody<P, 'post'>>(expand(path, params?.path), body, this.options(params?.query));
  }

  put<P extends PathsWith<'put'>>(path: P, body: RequestBody<P, 'put'>, params?: ParamsArg<P, 'put'>): Observable<SuccessBody<P, 'put'>> {
    return this.http.put<SuccessBody<P, 'put'>>(expand(path, params?.path), body, this.options(params?.query));
  }

  delete<P extends PathsWith<'delete'>>(path: P, params?: ParamsArg<P, 'delete'>): Observable<SuccessBody<P, 'delete'>> {
    return this.http.delete<SuccessBody<P, 'delete'>>(expand(path, params?.path), this.options(params?.query));
  }

  private options(query?: Record<string, string>) {
    return {
      withCredentials: true,
      context: new HttpContext(),
      params: query ? new HttpParams({ fromObject: query }) : undefined,
    };
  }
}

/** Setzt Pfadparameter ein; Werte werden URL-kodiert, Kennungen sind Strings (K13). */
export function expand(path: string, params?: unknown): string {
  if (!params || typeof params !== 'object') {
    return path;
  }
  return path.replace(/\{([^}]+)\}/g, (_, name: string) => {
    const value = (params as Record<string, unknown>)[name];
    if (value === undefined || value === null) {
      throw new Error(`Pfadparameter ${name} fehlt für ${path}`);
    }
    return encodeURIComponent(String(value));
  });
}

/** Liest Problem Details aus einem Fehlerkörper; unbekannte Körper ergeben nur den Status. */
export function toProblem(status: number, body: unknown): ApiProblem {
  if (body && typeof body === 'object') {
    const b = body as Partial<ApiProblem>;
    return { status, title: b.title ?? null, detail: b.detail ?? null, errors: b.errors };
  }
  return { status };
}
