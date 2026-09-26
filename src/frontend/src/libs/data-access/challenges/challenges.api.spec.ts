import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChallengesApi, toOffsetIso, type ContributionError } from './challenges.api';
import { uuidv7 } from '../uuidv7';
import challenges from '../../../../e2e/fixtures/api/challenges.json';
import rejected from '../../../../e2e/fixtures/api/contribution-rejected.json';

describe('ChallengesApi (K10, K11, K13, A-009)', () => {
  let api: ChallengesApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ChallengesApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('mehrere Verbraucher derselben Daten lösen genau einen Request aus', () => {
    const seen: number[] = [];
    api.listRunning().subscribe((cards) => seen.push(cards.length));
    api.listRunning().subscribe((cards) => seen.push(cards.length));
    const request = http.expectOne('/api/challenges');
    request.flush(challenges.body);
    api.listRunning().subscribe((cards) => seen.push(cards.length));
    http.expectNone('/api/challenges');
    expect(seen).toEqual([1, 1, 1]);
    expect(api.running()?.[0].title).toBe('Rad oder Fuß zur Arbeit');
  });

  it('bildet das Formular ausdrücklich auf das DTO ab: Dezimalstring, Offset-Zeitpunkt, Kanal mobile, Schlüssel bleibt gleich', () => {
    const now = new Date(2026, 8, 26, 9, 30, 0);
    const first = api.toRequest({ challengeId: 'c', daysAgo: 2, value: '1', note: 'bleibt lokal' }, now);
    const again = api.toRequest({ challengeId: 'c', daysAgo: 2, value: '1' }, now, first.idempotencyKey ?? undefined);

    expect(first.value).toBe('1');
    expect(typeof first.value).toBe('string');
    expect(first.channel).toBe('mobile');
    expect(first.operationId).toBeNull();
    expect(first.recordedAt).toBe(toOffsetIso(new Date(2026, 8, 24, 9, 30, 0)));
    expect(first.recordedAt).toMatch(/[+-]\d{2}:\d{2}$/);
    expect(first.idempotencyKey).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    expect(again.idempotencyKey).toBe(first.idempotencyKey);
    expect('note' in first).toBe(false);
  });

  it('überträgt genau das DTO und liefert die Serverantwort', () => {
    let result: string | undefined;
    const request = api.toRequest({ challengeId: 'c', daysAgo: 0, value: '1' }, new Date());
    api.submit('c', request).subscribe((r) => (result = r.outcome));
    const call = http.expectOne('/api/challenges/c/contributions');
    expect(call.request.body).toEqual(request);
    call.flush({ contributionId: '01a0dc9f-0000-7000-8000-000000000000', outcome: 'recorded' }, { status: 201, statusText: 'Created' });
    expect(result).toBe('recorded');
  });

  it('macht Serverfehler als stabile Textschlüssel sichtbar (422 mit Grund, 409, keine Verbindung)', () => {
    const errors: ContributionError[] = [];
    api.submit('c', api.toRequest({ challengeId: 'c', daysAgo: 0, value: '1' }, new Date())).subscribe({ error: (e: ContributionError) => errors.push(e) });
    http.expectOne('/api/challenges/c/contributions').flush(rejected.body, { status: 422, statusText: 'Unprocessable Entity' });
    api.submit('c', api.toRequest({ challengeId: 'c', daysAgo: 0, value: '1' }, new Date())).subscribe({ error: (e: ContributionError) => errors.push(e) });
    http.expectOne('/api/challenges/c/contributions').flush({ title: 'Gleiche Kennung mit abweichendem Inhalt', status: 409 }, { status: 409, statusText: 'Conflict' });
    api.submit('c', api.toRequest({ challengeId: 'c', daysAgo: 0, value: '1' }, new Date())).subscribe({ error: (e: ContributionError) => errors.push(e) });
    http.expectOne('/api/challenges/c/contributions').error(new ProgressEvent('error'), { status: 0 });
    expect(errors.map((e) => e.key)).toEqual(['fehler.InFuture', 'fehler.Konflikt', 'fehler.Verbindung']);
  });

  it('UUIDv7 ist zeitlich sortierbar', () => {
    const a = uuidv7(1_700_000_000_000);
    const b = uuidv7(1_700_000_000_001);
    expect(a < b).toBe(true);
    expect(uuidv7()).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
  });
});
