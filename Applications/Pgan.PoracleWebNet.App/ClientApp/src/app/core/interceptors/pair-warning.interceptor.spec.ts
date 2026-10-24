import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { PAIR_WARNING_TOAST_MS, pairWarningInterceptor } from './pair-warning.interceptor';
import { I18nService } from '../services/i18n.service';
import { LinkedAccountService } from '../services/linked-account.service';
import { ToastService } from '../services/toast.service';

const STRINGS: Record<string, string> = {
  'DRIFT.PAIR_WARNING': 'Saved for {{saved}}, but not for {{failed}}: {{reason}}. Review the difference on Linked accounts.',
  'DRIFT.WARN_OUT_OF_STEP': "its profiles don't line up with {{saved}}'s",
  'DRIFT.WARN_REFUSED': 'Poracle refused it',
  'DRIFT.WARN_RENAME_REFUSED': 'Poracle refused the new name',
  'DRIFT.WARN_UNREACHABLE': "Poracle couldn't be reached",
  'LINKED.SIDE_DISCORD': 'Discord',
  'LINKED.SIDE_TELEGRAM': 'Telegram',
};

function instant(key: string, p?: Record<string, string>): string {
  return (STRINGS[key] ?? key).replace(/\{\{(\w+)\}\}/g, (_, name: string) => p?.[name] ?? '');
}

describe('pairWarningInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let toast: { pairWarning: jest.Mock };
  let refreshSoon: jest.Mock;

  beforeEach(() => {
    toast = { pairWarning: jest.fn() };
    refreshSoon = jest.fn();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([pairWarningInterceptor])),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toast },
        { provide: LinkedAccountService, useValue: { refreshSoon } },
        { provide: I18nService, useValue: { instant: jest.fn(instant) } },
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('keeps the warning up at least as long as an error toast, since it carries a reason to read', () => {
    expect(PAIR_WARNING_TOAST_MS).toBeGreaterThanOrEqual(5000);
  });

  const header = (body: object) => ({ headers: { 'X-Pair-Warning': JSON.stringify(body) } });

  it('words a known code in the display language, names both apps, and points at Linked accounts', () => {
    let body: unknown;
    http.put('/api/location', { a: 1 }).subscribe(b => (body = b));
    httpMock
      .expectOne('/api/location')
      .flush({ ok: true }, header({ side: 'telegram', code: 'unreachable', reason: "Poracle couldn't be reached." }));

    expect(body).toEqual({ ok: true });
    expect(toast.pairWarning).toHaveBeenCalledTimes(1);
    expect(toast.pairWarning).toHaveBeenCalledWith(
      "Saved for Discord, but not for Telegram: Poracle couldn't be reached. Review the difference on Linked accounts.",
    );
  });

  it('never shows the raw English reason for a coded warning', () => {
    http.post('/api/profiles', {}).subscribe();
    httpMock.expectOne('/api/profiles').flush({}, header({ side: 'discord', code: 'out_of_step', reason: 'profiles out of step' }));

    const text = toast.pairWarning.mock.calls[0][0] as string;
    expect(text).toBe(
      "Saved for Telegram, but not for Discord: its profiles don't line up with Telegram's. Review the difference on Linked accounts.",
    );
    expect(text).not.toContain('out of step');
  });

  it("passes PoracleNG's own sentence through, without doubling its full stop", () => {
    http.put('/api/areas', {}).subscribe();
    httpMock.expectOne('/api/areas').flush(null, {
      headers: { 'X-Pair-Warning': JSON.stringify({ side: 'telegram', code: 'refused', reason: 'no such place.' }) },
      status: 204,
      statusText: 'No Content',
    });

    expect(toast.pairWarning).toHaveBeenCalledWith(
      'Saved for Discord, but not for Telegram: no such place. Review the difference on Linked accounts.',
    );
  });

  it('says Poracle refused when a refusal carries no sentence', () => {
    http.put('/api/areas', {}).subscribe();
    httpMock.expectOne('/api/areas').flush({}, header({ side: 'telegram', code: 'refused', reason: '' }));

    expect(toast.pairWarning.mock.calls[0][0]).toContain('Poracle refused it.');
  });

  it('says nothing when there is no header', () => {
    http.put('/api/location', {}).subscribe();
    httpMock.expectOne('/api/location').flush({});

    expect(toast.pairWarning).not.toHaveBeenCalled();
  });

  it('ignores a header it cannot read, and a side it does not know', () => {
    http.get('/a').subscribe();
    httpMock.expectOne('/a').flush({}, { headers: { 'X-Pair-Warning': 'not json' } });
    http.get('/b').subscribe();
    httpMock.expectOne('/b').flush({}, header({ side: 'signal', reason: 'x' }));

    expect(toast.pairWarning).not.toHaveBeenCalled();
  });

  it('reads the drift again after any write, warned or not, so nothing shown is stale', () => {
    http.put('/api/profiles/2', {}).subscribe();
    httpMock.expectOne('/api/profiles/2').flush({});
    http.delete('/api/places/home').subscribe();
    httpMock.expectOne('/api/places/home').flush(null, { status: 204, statusText: 'No Content' });

    expect(refreshSoon).toHaveBeenCalledTimes(2);
  });

  it('does not read it again after a read, or after the link routes that read it themselves', () => {
    http.get('/api/profiles').subscribe();
    httpMock.expectOne('/api/profiles').flush([]);
    http.post('/api/account-link/resolve', {}).subscribe();
    httpMock.expectOne('/api/account-link/resolve').flush(null, { status: 204, statusText: 'No Content' });

    expect(refreshSoon).not.toHaveBeenCalled();
  });

  it('does not read it again after a failed write', () => {
    http.put('/api/location', {}).subscribe({ error: () => undefined });
    httpMock.expectOne('/api/location').flush({}, { status: 500, statusText: 'Error' });

    expect(refreshSoon).not.toHaveBeenCalled();
  });
});
