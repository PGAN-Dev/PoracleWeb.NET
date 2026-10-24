import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { AlarmMoveService, summariseMoves } from './alarm-move.service';
import { ConfigService } from './config.service';
import { I18nService } from './i18n.service';
import { isMoveFailure } from '../../shared/utils/move-failure';

const API = 'http://test-api';
const i18n = {
  instant: (key: string, params?: Record<string, unknown>) => (params ? `${key} ${JSON.stringify(params)}` : key),
  plural: (key: string, count: number) => `${key}:${count}`,
};

const tick = async () => {
  for (let i = 0; i < 5; i++) await Promise.resolve();
};

describe('AlarmMoveService', () => {
  let http: HttpTestingController;
  let service: AlarmMoveService;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: API } }],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(AlarmMoveService);
  });

  it('reads the X-Move-Status header off a PUT that moves an alarm', async () => {
    const result = firstValueFrom(service.edit('lures', 7, { lureId: 501 }, 'telegram'));
    const req = http.expectOne(`${API}/api/lures/7`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ destination: 'telegram', lureId: 501 });
    req.flush({ uid: 9 }, { headers: { 'X-Move-Status': 'source_remains' } });

    await expect(result).resolves.toEqual({ destination: 'telegram', status: 'source_remains' });
  });

  it('marks a failed move so its 502 sentence is shown instead of the generic one', async () => {
    const result = firstValueFrom(service.edit('lures', 7, {}, 'discord'));
    http.expectOne(`${API}/api/lures/7`).flush({ error: 'Check both apps.' }, { status: 502, statusText: 'Bad Gateway' });

    const err = await result.catch((e: unknown) => e);
    expect(isMoveFailure(err)).toBe(true);
  });

  it('posts a bulk move with the type, the uids and the side', async () => {
    const result = firstValueFrom(service.moveMany('pokemon', [1, 2], 'discord'));
    const req = http.expectOne(`${API}/api/alarm-move`);
    expect(req.request.body).toEqual({ uids: [1, 2], destination: 'discord', type: 'pokemon' });
    req.flush({
      results: [
        { uid: 1, status: 'moved' },
        { uid: 2, status: 'moved' },
      ],
    });

    expect((await result).results).toHaveLength(2);
  });

  it('splits more than 500 uids into requests of at most 500, one after another', async () => {
    const uids = Array.from({ length: 1001 }, (_, i) => i + 1);
    const result = firstValueFrom(service.moveMany('pokemon', uids, 'discord'));

    const first = http.match(`${API}/api/alarm-move`);
    expect(first).toHaveLength(1);
    expect(first[0].request.body.uids).toHaveLength(500);
    first[0].flush({ results: [{ uid: 1, status: 'moved' }] });
    await tick();

    const second = http.match(`${API}/api/alarm-move`);
    expect(second[0].request.body.uids).toHaveLength(500);
    second[0].flush({ results: [{ uid: 501, status: 'moved' }] });
    await tick();

    const third = http.match(`${API}/api/alarm-move`);
    expect(third[0].request.body.uids).toHaveLength(1);
    third[0].flush({ results: [{ uid: 1001, status: 'moved' }] });

    expect((await result).results.map(r => r.uid)).toEqual([1, 501, 1001]);
  });
});

describe('moveMany keeps what it already knows', () => {
  let http: HttpTestingController;
  let service: AlarmMoveService;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: API } }],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(AlarmMoveService);
  });

  it("a later chunk that fails comes back as failed for its uids, beside the earlier chunk's results", async () => {
    const uids = Array.from({ length: 501 }, (_, i) => i + 1);
    const result = firstValueFrom(service.moveMany('pokemon', uids, 'discord'));

    http.expectOne(`${API}/api/alarm-move`).flush({ results: [{ uid: 1, status: 'moved' }] });
    await tick();
    http.expectOne(`${API}/api/alarm-move`).flush({ error: 'down' }, { status: 502, statusText: 'Bad Gateway' });

    const { results } = await result;
    expect(results).toEqual([
      { uid: 1, status: 'moved' },
      { uid: 501, status: 'failed' },
    ]);
  });

  it('a refused request (4xx) is refused with the server reason, not "may or may not have arrived"', async () => {
    const result = firstValueFrom(service.moveMany('pokemon', [4], 'discord'));
    http.expectOne(`${API}/api/alarm-move`).flush({ error: "Your accounts aren't linked." }, { status: 400, statusText: 'Bad Request' });

    expect((await result).results).toEqual([{ uid: 4, reason: "Your accounts aren't linked.", status: 'refused' }]);
  });
});

describe('summariseMoves', () => {
  const t = i18n as unknown as I18nService;

  it('says nothing for no results, so no empty snackbar opens', () => {
    expect(summariseMoves([], 'telegram', t)).toBe('');
  });

  it('reads the enum in either casing', () => {
    const text = summariseMoves([{ uid: 2, status: 'SourceRemains' as never }], 'telegram', t);

    expect(text).toContain('ALARM.MOVE_SOURCE_REMAINS');
  });

  it('says all moved in one line', () => {
    const text = summariseMoves(
      [
        { uid: 1, status: 'moved' },
        { uid: 2, status: 'moved' },
      ],
      'telegram',
      t,
    );

    expect(text).toContain('ALARM.MOVE_DONE');
    expect(text).toContain('"count":2');
  });

  it('names the leftovers per outcome, including the copy that is still there', () => {
    const text = summariseMoves(
      [
        { uid: 1, status: 'moved' },
        { uid: 2, status: 'sourceRemains' },
        { existingUid: 40, uid: 3, status: 'conflict' },
        { uid: 4, reason: 'No Telegram account.', status: 'refused' },
        { uid: 5, status: 'failed' },
      ],
      'telegram',
      t,
    );

    expect(text).toContain('ALARM.MOVE_SOURCE_REMAINS');
    expect(text).toContain('ALARM.MOVE_CONFLICT');
    expect(text).toContain('ALARM.MOVE_REFUSED');
    expect(text).toContain('ALARM.MOVE_FAILED');
  });
});
