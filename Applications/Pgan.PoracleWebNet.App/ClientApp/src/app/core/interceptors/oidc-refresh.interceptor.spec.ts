import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { oidcRefreshInterceptor } from './oidc-refresh.interceptor';
import { ConfigService } from '../services/config.service';

describe('oidcRefreshInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  const REFRESH_URL = '/api/auth/oidc/refresh';

  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([oidcRefreshInterceptor])),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: '' } },
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('passes through when there is no refresh token', () => {
    http.get('/api/areas').subscribe();
    const req = httpMock.expectOne('/api/areas');
    req.flush({});
  });

  it('passes through the refresh endpoint itself (no recursion)', () => {
    localStorage.setItem('poracle_refresh_token', 'rt-1');
    http.post(REFRESH_URL, { refreshToken: 'rt-1' }).subscribe();
    const req = httpMock.expectOne(REFRESH_URL);
    req.flush({ expiresIn: 1800, refreshToken: 'rt-2', token: 't' });
  });

  it('refreshes and retries once on a 401', () => {
    localStorage.setItem('poracle_refresh_token', 'rt-1');
    // Not expiring soon, so the proactive path is skipped.
    localStorage.setItem('poracle_token_expires_at', String(Date.now() + 600_000));

    let result: unknown;
    http.get('/api/areas').subscribe(r => (result = r));

    // Original request 401s.
    httpMock.expectOne('/api/areas').flush({}, { status: 401, statusText: 'Unauthorized' });

    // Interceptor refreshes...
    httpMock.expectOne(REFRESH_URL).flush({ expiresIn: 1800, refreshToken: 'rt-2', token: 'new-jwt' });

    // ...then retries the original with the fresh bearer.
    const retry = httpMock.expectOne('/api/areas');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer new-jwt');
    retry.flush({ ok: true });

    expect(result).toEqual({ ok: true });
  });

  it('proactively refreshes before sending when the token is expiring soon', () => {
    localStorage.setItem('poracle_refresh_token', 'rt-1');
    localStorage.setItem('poracle_token_expires_at', String(Date.now() + 10_000)); // within skew

    http.get('/api/areas').subscribe();

    // Refresh fires first.
    httpMock.expectOne(REFRESH_URL).flush({ expiresIn: 1800, refreshToken: 'rt-2', token: 'fresh-jwt' });

    // Then the original request goes out with the fresh bearer.
    const req = httpMock.expectOne('/api/areas');
    expect(req.request.headers.get('Authorization')).toBe('Bearer fresh-jwt');
    req.flush({});
  });

  it('collapses two concurrent 401s into a single refresh', () => {
    localStorage.setItem('poracle_refresh_token', 'rt-1');
    localStorage.setItem('poracle_token_expires_at', String(Date.now() + 600_000));

    http.get('/api/a').subscribe();
    http.get('/api/b').subscribe();

    httpMock.expectOne('/api/a').flush({}, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectOne('/api/b').flush({}, { status: 401, statusText: 'Unauthorized' });

    // Only ONE refresh despite two 401s.
    httpMock.expectOne(REFRESH_URL).flush({ expiresIn: 1800, refreshToken: 'rt-2', token: 'new-jwt' });

    httpMock.expectOne('/api/a').flush({});
    httpMock.expectOne('/api/b').flush({});
  });

  it('does not loop when the refresh itself 401s', () => {
    localStorage.setItem('poracle_refresh_token', 'rt-1');
    localStorage.setItem('poracle_token_expires_at', String(Date.now() + 600_000));

    let errored = false;
    http.get('/api/areas').subscribe({ error: () => (errored = true) });

    httpMock.expectOne('/api/areas').flush({}, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectOne(REFRESH_URL).flush({ error: 'invalid_grant' }, { status: 401, statusText: 'Unauthorized' });

    expect(errored).toBe(true);
    // No second retry of the original request.
    httpMock.expectNone('/api/areas');
  });
});
