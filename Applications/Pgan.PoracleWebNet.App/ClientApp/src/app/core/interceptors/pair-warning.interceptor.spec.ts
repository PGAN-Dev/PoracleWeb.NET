import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { PAIR_WARNING_TOAST_MS, pairWarningInterceptor } from './pair-warning.interceptor';
import { I18nService } from '../services/i18n.service';
import { ToastService } from '../services/toast.service';

describe('pairWarningInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let toast: { info: jest.Mock };

  beforeEach(() => {
    toast = { info: jest.fn() };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([pairWarningInterceptor])),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toast },
        {
          provide: I18nService,
          useValue: {
            instant: jest.fn((key: string, p?: Record<string, string>) =>
              key === 'DRIFT.PAIR_WARNING'
                ? `Saved for ${p?.['saved']}. ${p?.['failed']} didn't take it: ${p?.['reason']}.`
                : key === 'LINKED.SIDE_DISCORD'
                  ? 'Discord'
                  : 'Telegram',
            ),
          },
        },
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('keeps the warning up at least as long as an error toast, since it carries a reason to read', () => {
    expect(PAIR_WARNING_TOAST_MS).toBeGreaterThanOrEqual(5000);
  });

  const warning = JSON.stringify({ side: 'telegram', reason: 'it was not registered' });

  it('says once which side saved and which did not, and leaves the body alone', () => {
    let body: unknown;
    http.put('/api/location', { a: 1 }).subscribe(b => (body = b));
    httpMock.expectOne('/api/location').flush({ ok: true }, { headers: { 'X-Pair-Warning': warning } });

    expect(body).toEqual({ ok: true });
    expect(toast.info).toHaveBeenCalledTimes(1);
    expect(toast.info).toHaveBeenCalledWith("Saved for Discord. Telegram didn't take it: it was not registered.", PAIR_WARNING_TOAST_MS);
  });

  it('names Telegram as the one that saved when the warning is about Discord', () => {
    http.put('/api/areas', {}).subscribe();
    httpMock.expectOne('/api/areas').flush(null, {
      headers: { 'X-Pair-Warning': JSON.stringify({ side: 'discord', reason: 'x' }) },
      status: 204,
      statusText: 'No Content',
    });

    expect(toast.info).toHaveBeenCalledWith("Saved for Telegram. Discord didn't take it: x.", PAIR_WARNING_TOAST_MS);
  });

  it('says nothing when there is no header', () => {
    http.put('/api/location', {}).subscribe();
    httpMock.expectOne('/api/location').flush({});

    expect(toast.info).not.toHaveBeenCalled();
  });

  it('ignores a header it cannot read, and a side it does not know', () => {
    http.get('/a').subscribe();
    httpMock.expectOne('/a').flush({}, { headers: { 'X-Pair-Warning': 'not json' } });
    http.get('/b').subscribe();
    httpMock.expectOne('/b').flush({}, { headers: { 'X-Pair-Warning': JSON.stringify({ side: 'signal', reason: 'x' }) } });

    expect(toast.info).not.toHaveBeenCalled();
  });
});
