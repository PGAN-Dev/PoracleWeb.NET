import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { authInterceptor } from './auth.interceptor';
import { errorInterceptor } from './error.interceptor';
import { oidcRefreshInterceptor } from './oidc-refresh.interceptor';
import { ConfigService } from '../services/config.service';
import { ToastService } from '../services/toast.service';
import { TokenStoreService } from '../services/token-store.service';

/**
 * A page load sends about nine requests at once, so when the session behind them dies they all come
 * back 401 together. The interceptor used to judge each one against whatever was in storage *when it
 * arrived*: the first restored the stashed admin (or the delegate's own) token, the second found no
 * stash and cleared everything -- signing out someone who had just been returned to their own account.
 * Every 401 after the first also toasted "session expired" again.
 *
 * The chain here is the production one, so the bearer each request carried is the one authInterceptor
 * really attached.
 */
describe('a burst of 401s', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let toast: { error: jest.Mock };
  let router: { navigate: jest.Mock };
  let tokenStore: TokenStoreService;
  let finishNavigation: (ok: boolean) => void;

  const unauthorized = (req: TestRequest) => req.flush(null, { status: 401, statusText: 'Unauthorized' });

  const sendPageLoad = (urls: string[]) => {
    for (const url of urls) {
      http.get(url).subscribe({ error: () => {} });
    }
    return urls.map(url => httpMock.expectOne(url));
  };

  beforeEach(() => {
    localStorage.clear();
    toast = { error: jest.fn() };
    router = {
      navigate: jest.fn(() => new Promise<boolean>(resolve => (finishNavigation = resolve))),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor, errorInterceptor, oidcRefreshInterceptor])),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: '' } },
        { provide: ToastService, useValue: toast },
        { provide: Router, useValue: router },
        { provide: TranslateService, useValue: { instant: jest.fn((key: string) => key) } },
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    tokenStore = TestBed.inject(TokenStoreService);
  });

  afterEach(() => httpMock.verify());

  it('returns a revoked delegate to their own account however many requests fail together', () => {
    localStorage.setItem('poracle_token', 'webhook-token');
    localStorage.setItem('poracle_admin_token', 'own-token');

    const reqs = sendPageLoad(['/api/monsters', '/api/raids', '/api/quests', '/api/dashboard/counts', '/api/mutes']);
    reqs.forEach(unauthorized);

    expect(localStorage.getItem('poracle_token')).toBe('own-token');
    expect(router.navigate).toHaveBeenCalledTimes(1);
    expect(router.navigate).toHaveBeenCalledWith(['/admin']);
    expect(toast.error).toHaveBeenCalledTimes(1);
    expect(toast.error).toHaveBeenCalledWith('HTTP_ERROR.INSPECTION_ENDED');
  });

  it('ends an ordinary session once, with one toast, however many requests fail together', () => {
    localStorage.setItem('poracle_token', 'expired-token');

    const reqs = sendPageLoad(['/api/monsters', '/api/raids', '/api/quests']);
    reqs.forEach(unauthorized);

    expect(localStorage.getItem('poracle_token')).toBeNull();
    expect(router.navigate).toHaveBeenCalledTimes(1);
    expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: {} });
    expect(toast.error).toHaveBeenCalledTimes(1);
    expect(toast.error).toHaveBeenCalledWith('HTTP_ERROR.UNAUTHORIZED');
  });

  it('says the session expired even when the request that found out is one of the silent ones', () => {
    // Which request of the burst lands first is up to the network. When it was /api/mutes or
    // /api/settings the user used to arrive on the login page with no word of why.
    localStorage.setItem('poracle_token', 'expired-token');

    sendPageLoad(['/api/mutes', '/api/settings']).forEach(unauthorized);

    expect(toast.error).toHaveBeenCalledTimes(1);
    expect(toast.error).toHaveBeenCalledWith('HTTP_ERROR.UNAUTHORIZED');
  });

  it('still ends the session when the restored token is itself refused', () => {
    // Legitimate case: the stale-request rule must not protect a token that is genuinely dead. A request
    // sent with the restored token and refused is about *that* session, so it ends.
    localStorage.setItem('poracle_token', 'webhook-token');
    localStorage.setItem('poracle_admin_token', 'dead-own-token');

    sendPageLoad(['/api/monsters']).forEach(unauthorized);
    expect(localStorage.getItem('poracle_token')).toBe('dead-own-token');

    sendPageLoad(['/api/auth/me']).forEach(unauthorized);

    expect(localStorage.getItem('poracle_token')).toBeNull();
    expect(router.navigate).toHaveBeenLastCalledWith(['/login'], { queryParams: {} });
  });

  it('keeps the signed-in shell until the page has been left, but stops sending the token at once', async () => {
    // Clearing the user swaps the shell's layout; doing it while the router is still on the signed-in
    // page makes the other outlet build that page again, without a token. Logout was fixed this way in
    // #916 and the 401 path is the same shape.
    localStorage.setItem('poracle_token', 'expired-token');
    const cleared = jest.fn();
    tokenStore.sessionCleared$.subscribe(cleared);

    sendPageLoad(['/api/monsters']).forEach(unauthorized);

    expect(localStorage.getItem('poracle_token')).toBeNull();
    expect(cleared).not.toHaveBeenCalled();

    finishNavigation(true);
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();

    expect(cleared).toHaveBeenCalledTimes(1);
  });

  it('ignores a request the page sent after the tokens were dropped', () => {
    localStorage.setItem('poracle_token', 'expired-token');
    sendPageLoad(['/api/monsters']).forEach(unauthorized);
    toast.error.mockClear();
    router.navigate.mockClear();

    // Sent with no bearer at all, because there no longer is one.
    const [late] = sendPageLoad(['/api/raids']);
    expect(late.request.headers.has('Authorization')).toBe(false);
    unauthorized(late);

    expect(toast.error).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('words the toast once the language has loaded when a session ends on page load', () => {
    // A 401 on a reload lands before the translations do, and instant() then answers the key itself:
    // "HTTP_ERROR.UNAUTHORIZED" was the whole message.
    const translate = TestBed.inject(TranslateService) as unknown as { get: jest.Mock; instant: jest.Mock };
    translate.instant.mockImplementation((key: string) => key);
    translate.get = jest.fn(() => of('Your session has expired.'));
    localStorage.setItem('poracle_token', 'expired-token');

    sendPageLoad(['/api/auth/me']).forEach(unauthorized);

    expect(toast.error).toHaveBeenCalledTimes(1);
    expect(toast.error).toHaveBeenCalledWith('Your session has expired.');
  });

  it('ends a refresh-backed session whose retry with the fresh token is refused too', () => {
    // The retry carries a bearer the original request never had; judging it by the original's header
    // would call it stale and leave a session that cannot work.
    localStorage.setItem('poracle_token', 'old-jwt');
    localStorage.setItem('poracle_refresh_token', 'rt-1');
    localStorage.setItem('poracle_token_expires_at', String(Date.now() + 600_000));

    http.get('/api/monsters').subscribe({ error: () => {} });
    unauthorized(httpMock.expectOne('/api/monsters'));
    httpMock.expectOne('/api/auth/oidc/refresh').flush({ expiresIn: 1800, refreshToken: 'rt-2', token: 'new-jwt' });
    const retry = httpMock.expectOne('/api/monsters');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer new-jwt');
    unauthorized(retry);

    expect(localStorage.getItem('poracle_token')).toBeNull();
    expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: {} });
  });
});
