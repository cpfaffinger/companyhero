import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiClient, expand, toProblem } from './api-client';

describe('ApiClient (generierter Vertrag über HttpClient)', () => {
  let client: ApiClient;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    client = TestBed.inject(ApiClient);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('setzt Pfadparameter aus dem Vertrag ein und sendet mit Sitzungscookie', () => {
    let result: { total: string } | undefined;
    client.get('/api/challenges/{challengeId}/collective', { path: { challengeId: '01a0dc9f-5e22-7257-b448-4cde50f98097' } }).subscribe((r) => (result = r));
    const request = http.expectOne('/api/challenges/01a0dc9f-5e22-7257-b448-4cde50f98097/collective');
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBe(true);
    request.flush({ total: '3.0000', contributionCount: 3, contributorCount: 2, updatedAt: '2026-09-26T07:30:27+00:00', percent: 12 });
    expect(result?.total).toBe('3.0000');
  });

  it('überträgt den Anfragekörper unverändert als JSON mit Dezimalstring', () => {
    client
      .post('/api/challenges/{challengeId}/contributions', { value: '1', recordedAt: '2026-09-26T08:00:00+02:00', channel: 'mobile', idempotencyKey: '01a0dc9f-0000-7000-8000-000000000000', operationId: null }, { path: { challengeId: 'abc' } })
      .subscribe();
    const request = http.expectOne('/api/challenges/abc/contributions');
    expect(request.request.body).toEqual({ value: '1', recordedAt: '2026-09-26T08:00:00+02:00', channel: 'mobile', idempotencyKey: '01a0dc9f-0000-7000-8000-000000000000', operationId: null });
    request.flush({ contributionId: 'x', outcome: 'recorded' }, { status: 201, statusText: 'Created' });
  });

  it('kodiert Pfadwerte und lehnt fehlende Parameter ab', () => {
    expect(expand('/api/x/{id}', { id: 'a b' })).toBe('/api/x/a%20b');
    expect(() => expand('/api/x/{id}', {})).toThrowError(/Pfadparameter id fehlt/);
  });

  it('liest Problem Details mit Grund und Feldfehlern', () => {
    const problem = toProblem(422, { title: 'Beitrag abgelehnt', detail: 'InFuture' });
    expect(problem).toEqual({ status: 422, title: 'Beitrag abgelehnt', detail: 'InFuture', errors: undefined });
    expect(toProblem(500, 'kaputt')).toEqual({ status: 500 });
  });
});
