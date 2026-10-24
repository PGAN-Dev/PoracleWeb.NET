import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { AlarmMoveService, failedOutcomes, moveRefusalText, summariseMoves } from './alarm-move.service';
import { ConfigService } from './config.service';
import { I18nService } from './i18n.service';
import { isMoveFailure } from '../../shared/utils/move-failure';

const API = 'http://test-api';
const i18n = {
  instant: (key: string, params?: Record<string, unknown>) => (params ? `${key} ${JSON.stringify(params)}` : key),
  plural: (key: string, count: number, params?: Record<string, unknown>) =>
    params ? `${key}:${count} ${JSON.stringify(params)}` : `${key}:${count}`,
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

  it('adds the kind to a bulk move only when one is given', async () => {
    const withKind = firstValueFrom(service.moveMany('invasion', [7], 'discord', 'event'));
    const req = http.expectOne(`${API}/api/alarm-move`);
    expect(req.request.body).toEqual({ uids: [7], destination: 'discord', type: 'invasion', kind: 'event' });
    req.flush({ results: [] });
    await withKind;

    const without = firstValueFrom(service.moveMany('pokemon', [1], 'discord'));
    const plain = http.expectOne(`${API}/api/alarm-move`);
    expect('kind' in plain.request.body).toBe(false);
    plain.flush({ results: [] });
    await without;
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

describe('a refused request with no text', () => {
  it('is refused, not failed, so it never reads as "may or may not have been moved"', () => {
    expect(failedOutcomes([4], { status: 403 })).toEqual([{ uid: 4, reason: null, status: 'refused' }]);
    expect(failedOutcomes([4], { error: {}, status: 404 })).toEqual([{ uid: 4, reason: null, status: 'refused' }]);
  });

  it('still reports a server error or no response as failed', () => {
    expect(failedOutcomes([4], { status: 500 })).toEqual([{ uid: 4, status: 'failed' }]);
    expect(failedOutcomes([4], { status: 0 })).toEqual([{ uid: 4, status: 'failed' }]);
  });

  it('is worded with the generic refusal', () => {
    const text = summariseMoves(failedOutcomes([4], { status: 403 }), 'telegram', i18n as unknown as I18nService);
    expect(text).toContain('ALARM.MOVE_REFUSED:1');
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

    expect(text).toContain('ALARM.MOVE_DONE:2');
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

describe('summariseMoves reads reason codes, never the server sentence', () => {
  const t = i18n as unknown as I18nService;

  it('counts an alarm already on the target as done, not as not moved', () => {
    const text = summariseMoves(
      [
        { uid: 1, status: 'moved' },
        { uid: 2, reason: 'That alarm is already there.', reasonCode: 'already_there', status: 'alreadyThere' },
      ],
      'telegram',
      t,
    );

    expect(text).toContain('ALARM.MOVE_DONE:1');
    expect(text).toContain('ALARM.MOVE_ALREADY_THERE:1');
    expect(text).not.toContain('MOVE_REFUSED');
    expect(text).not.toContain('That alarm is already there.');
  });

  it('reads alreadyThere in either casing', () => {
    expect(summariseMoves([{ uid: 2, status: 'AlreadyThere' as never }], 'discord', t)).toContain('ALARM.MOVE_ALREADY_THERE:1');
  });

  it('translates a refusal by its code and drops the English sentence', () => {
    const text = summariseMoves(
      [
        {
          uid: 4,
          reason: "The place 'Work' isn't saved on Telegram.",
          reasonApp: 'telegram',
          reasonCode: 'place_missing',
          reasonPlace: 'Work',
          status: 'refused',
        },
      ],
      'telegram',
      t,
    );

    expect(text).toContain('ALARM.MOVE_REASON_PLACE_MISSING');
    expect(text).toContain('Work');
    expect(text).not.toContain("isn't saved");
  });

  it('gives one line per refusal code', () => {
    const text = summariseMoves(
      [
        { uid: 4, reasonCode: 'different_profiles', status: 'refused' },
        { uid: 5, reasonCode: 'different_profiles', status: 'refused' },
        { uid: 6, reasonApp: 'telegram', reasonCode: 'invalid', status: 'refused' },
      ],
      'telegram',
      t,
    );

    expect(text).toContain('ALARM.MOVE_REFUSED:2');
    expect(text).toContain('ALARM.MOVE_REASON_DIFFERENT_PROFILES');
    expect(text).toContain('ALARM.MOVE_REFUSED:1');
    expect(text).toContain('ALARM.MOVE_REASON_INVALID');
  });

  it('words a refusal with an unknown code or only an English reason generically', () => {
    const text = summariseMoves([{ uid: 4, reason: 'Some server English.', reasonCode: 'brand_new', status: 'refused' }], 'telegram', t);

    expect(text).toContain('ALARM.MOVE_REFUSED_NO_REASON');
    expect(text).not.toContain('Some server English.');
  });

  it('carries the code of a refused request into each uid', () => {
    expect(failedOutcomes([4], { error: { code: 'not_linked', error: "Your accounts aren't linked." }, status: 400 })).toEqual([
      { uid: 4, reason: "Your accounts aren't linked.", reasonCode: 'not_linked', status: 'refused' },
    ]);
  });
});

describe('moveRefusalText', () => {
  const t = i18n as unknown as I18nService;

  it('translates a single edit refused with a code', () => {
    expect(moveRefusalText({ error: { code: 'different_profiles', error: 'English.' }, status: 409 }, t)).toContain(
      'ALARM.MOVE_REASON_DIFFERENT_PROFILES',
    );
  });

  it('answers null for an error that carries no move code, so the caller words it as before', () => {
    expect(moveRefusalText({ error: { error: 'Range' }, status: 400 }, t)).toBeNull();
    expect(moveRefusalText(null, t)).toBeNull();
  });
});
