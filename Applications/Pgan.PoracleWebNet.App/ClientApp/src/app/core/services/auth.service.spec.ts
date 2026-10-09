import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';

import { AuthService, disabledAccountFragment } from './auth.service';
import { ConfigService } from './config.service';
import { TokenStoreService } from './token-store.service';
import { UserInfo } from '../models';

/**
 * Pinning the exact one-encode contract without touching window.location (jsdom no-ops that
 * assignment) or the Router (handleTokenFromCallback deliberately bypasses it -- see its own tests).
 * Caught live, in a real browser, that router.navigate({ fragment }) re-escapes a literal '%', so an
 * already-percent-encoded support_url came out double-encoded and the rendered link was broken.
 */
describe('disabledAccountFragment', () => {
  it('encodes a support URL exactly once', () => {
    expect(disabledAccountFragment('https://example.com/support')).toBe(
      'error=account_disabled&support_url=https%3A%2F%2Fexample.com%2Fsupport',
    );
  });

  it('omits support_url entirely when there is none', () => {
    expect(disabledAccountFragment(null)).toBe('error=account_disabled');
  });
});

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;
  let router: Router;
  const API = 'http://test-api';

  const mockUser: UserInfo = {
    id: '123456',
    username: 'TestUser',
    avatarUrl: 'https://example.com/avatar.png',
    enabled: true,
    isAdmin: false,
    managedWebhooks: [],
    profileName: 'Default',
    profileNo: 1,
    type: 'discord:user',
  };

  const mockAdminUser: UserInfo = {
    ...mockUser,
    username: 'AdminUser',
    isAdmin: true,
  };

  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // AuthService reaches TranslateService transitively, via AlertLanguageService -> I18nService,
        // since it reconciles the alert language once a token exists (#775).
        provideTranslateService(),
        { provide: ConfigService, useValue: { apiHost: API } },
        {
          provide: Router,
          useValue: { createUrlTree: jest.fn(), navigate: jest.fn() },
        },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    service = TestBed.inject(AuthService);
  });

  afterEach(() => {
    httpMock.verify();
  });

  describe('constructor', () => {
    it('should attempt to load user when token exists in localStorage', () => {
      localStorage.setItem('poracle_token', 'existing-token');
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        providers: [
          provideHttpClient(),
          provideHttpClientTesting(),
          provideTranslateService(),
          { provide: ConfigService, useValue: { apiHost: API } },
          { provide: Router, useValue: { navigate: jest.fn() } },
        ],
      });
      const newHttpMock = TestBed.inject(HttpTestingController);
      TestBed.inject(AuthService);

      const req = newHttpMock.expectOne(`${API}/api/auth/me`);
      expect(req.request.method).toBe('GET');
      req.flush(mockUser);
    });

    it('should not call API when no token exists', () => {
      // Service already created in beforeEach with no token
      httpMock.expectNone(`${API}/api/auth/me`);
    });
  });

  describe('getToken', () => {
    it('should return null when no token is stored', () => {
      expect(service.getToken()).toBeNull();
    });

    it('should return stored token', () => {
      localStorage.setItem('poracle_token', 'my-token');
      expect(service.getToken()).toBe('my-token');
    });
  });

  describe('isAuthenticated', () => {
    it('should return false when no token exists', () => {
      expect(service.isAuthenticated()).toBe(false);
    });

    it('should return true when token exists', () => {
      localStorage.setItem('poracle_token', 'my-token');
      expect(service.isAuthenticated()).toBe(true);
    });
  });

  describe('handleTokenFromCallback', () => {
    it('should store token, load user, load settings, then navigate to dashboard', async () => {
      const promise = service.handleTokenFromCallback('new-token');

      expect(localStorage.getItem('poracle_token')).toBe('new-token');

      // Navigation should NOT happen until loadCurrentUser resolves
      expect(router.navigate).not.toHaveBeenCalled();

      const req = httpMock.expectOne(`${API}/api/auth/me`);
      req.flush(mockUser);
      await promise;

      // Settings are loaded after token is stored (fixes title not showing after OAuth redirect)
      const settingsReq = httpMock.expectOne(`${API}/api/settings`);
      settingsReq.flush([]);
      // getAll() also asks which disable_* keys Poracle forces off upstream (#769), and whether
      // Poracle can store a costume filter (#804).
      httpMock.expectOne(`${API}/api/settings/upstream-disabled`).flush([]);
      httpMock.expectOne(`${API}/api/settings/costume-capability`).flush({ raid: true, pokemon: true });
      // The alert language is reconciled here too: App.ngOnInit skips it while signed out (#775), so a
      // login completed inside one page session would otherwise never pick up humans.language.
      httpMock.expectOne(`${API}/api/location/language`).flush({ language: 'de' });

      // Now navigation should have happened
      expect(router.navigate).toHaveBeenCalledWith(['/dashboard']);
      expect(service.user()).toEqual(mockUser);
    });

    /**
     * A disabled account's /me 401 is silent and fires on the callback route, so without this it would
     * fall through to the dashboard with a dead token -- whose own requests then 401 outside the callback
     * route, ending the session with a generic "session expired" toast and no word of why. See #911.
     *
     * This is a real `window.location.href` navigation, not `router.navigate({ fragment })` -- confirmed
     * live in a browser that the Router's own fragment serialization re-escapes a literal '%', so an
     * already-percent-encoded support_url came out double-encoded and the rendered link was broken.
     * jsdom no-ops the navigation itself (same as the SSO logout path below), so the distinguishing,
     * observable behaviour here is that the in-app router is NOT used and the session is cleared first.
     */
    it('clears the session and sends a disabled account to the login page, not the dashboard, without the in-app router', async () => {
      const promise = service.handleTokenFromCallback('new-token');

      const req = httpMock.expectOne(`${API}/api/auth/me`);
      req.flush(
        {
          code: 'account_disabled',
          error: 'This account has been blocked by an administrator.',
          supportUrl: 'https://example.com/support',
        },
        { status: 401, statusText: 'Unauthorized' },
      );
      await promise;

      expect(localStorage.getItem('poracle_token')).toBeNull();
      expect(router.navigate).not.toHaveBeenCalled();
      httpMock.expectNone(`${API}/api/settings`);
    });

    it('omits support_url from the redirect when the operator has not set one', async () => {
      const promise = service.handleTokenFromCallback('new-token');

      httpMock
        .expectOne(`${API}/api/auth/me`)
        .flush({ code: 'account_disabled', error: 'blocked', supportUrl: null }, { status: 401, statusText: 'Unauthorized' });
      await promise;

      expect(localStorage.getItem('poracle_token')).toBeNull();
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('still goes to the dashboard for a 401 with no disabled-account code', async () => {
      const promise = service.handleTokenFromCallback('new-token');

      httpMock
        .expectOne(`${API}/api/auth/me`)
        .flush({ error: 'This account no longer exists.' }, { status: 401, statusText: 'Unauthorized' });
      await promise;

      const settingsReq = httpMock.expectOne(`${API}/api/settings`);
      settingsReq.flush([]);
      httpMock.expectOne(`${API}/api/settings/upstream-disabled`).flush([]);
      httpMock.expectOne(`${API}/api/settings/costume-capability`).flush({ raid: true, pokemon: true });
      httpMock.expectOne(`${API}/api/location/language`).flush({ language: 'de' });

      expect(router.navigate).toHaveBeenCalledWith(['/dashboard']);
    });
  });

  describe('logout', () => {
    it('should clear tokens, reset user, and navigate to the signed-out login page', async () => {
      localStorage.setItem('poracle_token', 'some-token');
      localStorage.setItem('poracle_admin_token', 'admin-token');

      service.logout();
      await Promise.resolve();

      expect(localStorage.getItem('poracle_token')).toBeNull();
      expect(localStorage.getItem('poracle_admin_token')).toBeNull();
      expect(service.isLoggedIn()).toBe(false);
      expect(service.isImpersonating()).toBe(false);
      // ?loggedout=1 shows the signed-out panel and suppresses the OIDC auto-redirect.
      expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: { loggedout: 1 } });
    });

    /**
     * The app shell has one router outlet in its signed-in layout and another in its signed-out one.
     * Clearing the user swapped layouts while the router was still on the page being left, so the new
     * outlet built that page again -- the dashboard reloaded all its data and quiet periods with no token,
     * and four 401s each put up "Your session has expired". The user now goes once the page has.
     */
    it('keeps the signed-in shell until the navigation away has finished, but drops the tokens at once', async () => {
      localStorage.setItem('poracle_token', 'some-token');
      service['currentUser'].set(mockUser);
      let finish!: (value: boolean) => void;
      (router.navigate as jest.Mock).mockReturnValue(new Promise<boolean>(resolve => (finish = resolve)));

      service.logout();

      expect(localStorage.getItem('poracle_token')).toBeNull();
      expect(service.isLoggedIn()).toBe(true);

      finish(true);
      await Promise.resolve();
      await Promise.resolve();

      expect(service.isLoggedIn()).toBe(false);
    });

    it('still signs out when the navigation is refused', async () => {
      service['currentUser'].set(mockUser);
      (router.navigate as jest.Mock).mockReturnValue(Promise.resolve(false));

      service.logout();
      await Promise.resolve();
      await Promise.resolve();

      expect(service.isLoggedIn()).toBe(false);
    });

    it('should perform single logout (no in-app navigation) when sso=true', () => {
      localStorage.setItem('poracle_token', 'some-token');

      // sso:true takes the window.location bounce to /api/auth/oidc/logout (jsdom no-ops the
      // assignment); the distinguishing, observable behaviour is that it does NOT use the
      // in-app router (unlike the default RP logout, which navigates to /login?loggedout=1).
      service.logout({ sso: true });

      expect(localStorage.getItem('poracle_token')).toBeNull();
      expect(service.isLoggedIn()).toBe(false);
      expect(router.navigate).not.toHaveBeenCalled();
    });
  });

  describe('loadCurrentUser', () => {
    it('should set currentUser on successful response', async () => {
      const promise = service.loadCurrentUser();

      const req = httpMock.expectOne(`${API}/api/auth/me`);
      req.flush(mockUser);

      const result = await promise;
      expect(result).toEqual(mockUser);
      expect(service.user()).toEqual(mockUser);
      expect(service.isLoggedIn()).toBe(true);
    });

    it('should forget the user on 401 error, leaving the token to the interceptor', async () => {
      // Removing poracle_token here as well as in the interceptor deleted the admin token the
      // impersonation fallback had just restored, one line after it was written. The interceptor owns
      // 401 token handling -- clearAll(), or the fallback -- and this only resets the user. See #706.
      localStorage.setItem('poracle_token', 'bad-token');
      const promise = service.loadCurrentUser();

      const req = httpMock.expectOne(`${API}/api/auth/me`);
      req.flush(null, { status: 401, statusText: 'Unauthorized' });

      const result = await promise;
      expect(result).toBeNull();
      expect(service.user()).toBeNull();
    });

    it('should resolve null on non-401 errors without clearing token', async () => {
      localStorage.setItem('poracle_token', 'some-token');
      const promise = service.loadCurrentUser();

      const req = httpMock.expectOne(`${API}/api/auth/me`);
      req.flush(null, { status: 500, statusText: 'Server Error' });

      const result = await promise;
      expect(result).toBeNull();
      // token should NOT be cleared for non-401 errors
      expect(localStorage.getItem('poracle_token')).toBe('some-token');
    });

    it('should store refreshed token and signal resync when backend detects mismatch', async () => {
      localStorage.setItem('poracle_token', 'old-token');
      const promise = service.loadCurrentUser();

      const req = httpMock.expectOne(`${API}/api/auth/me`);
      req.flush({ ...mockUser, profileNo: 3, token: 'refreshed-jwt' });

      const result = await promise;
      expect(result?.profileNo).toBe(3);
      expect(localStorage.getItem('poracle_token')).toBe('refreshed-jwt');
      expect(service.profileResynced()).toBe(true);
    });

    it('should not call setToken and should clear resync signal when no token in response', async () => {
      localStorage.setItem('poracle_token', 'original-token');
      const promise = service.loadCurrentUser();

      const req = httpMock.expectOne(`${API}/api/auth/me`);
      req.flush(mockUser);

      await promise;
      expect(localStorage.getItem('poracle_token')).toBe('original-token');
      expect(service.profileResynced()).toBe(false);
    });
  });

  describe('computed signals', () => {
    it('isAdmin should reflect user admin status', async () => {
      expect(service.isAdmin()).toBe(false);

      const promise = service.loadCurrentUser();
      httpMock.expectOne(`${API}/api/auth/me`).flush(mockAdminUser);
      await promise;

      expect(service.isAdmin()).toBe(true);
    });

    it('hasManagedWebhooks should reflect webhook list', async () => {
      expect(service.hasManagedWebhooks()).toBe(false);

      const userWithWebhooks = { ...mockUser, managedWebhooks: ['hook1'] };
      const promise = service.loadCurrentUser();
      httpMock.expectOne(`${API}/api/auth/me`).flush(userWithWebhooks);
      await promise;

      expect(service.hasManagedWebhooks()).toBe(true);
      expect(service.managedWebhooks()).toEqual(['hook1']);
    });

    it('managedWebhooks should return empty array when user has no webhooks', () => {
      expect(service.managedWebhooks()).toEqual([]);
    });
  });

  describe('impersonate', () => {
    it('should save admin token, set impersonation token, and navigate', () => {
      localStorage.setItem('poracle_token', 'admin-jwt');
      service.impersonate('user-jwt');

      expect(localStorage.getItem('poracle_admin_token')).toBe('admin-jwt');
      expect(localStorage.getItem('poracle_token')).toBe('user-jwt');
      expect(service.isImpersonating()).toBe(true);
      expect(router.navigate).toHaveBeenCalledWith(['/dashboard']);

      httpMock.expectOne(`${API}/api/auth/me`).flush(mockUser);
    });

    it('should handle impersonate when no admin token exists', () => {
      service.impersonate('user-jwt');

      expect(localStorage.getItem('poracle_admin_token')).toBeNull();
      expect(localStorage.getItem('poracle_token')).toBe('user-jwt');
      expect(service.isImpersonating()).toBe(true);

      httpMock.expectOne(`${API}/api/auth/me`).flush(mockUser);
    });
  });

  describe('stopImpersonating', () => {
    it('should restore admin token and navigate to admin page', async () => {
      localStorage.setItem('poracle_admin_token', 'admin-jwt');
      localStorage.setItem('poracle_token', 'impersonated-jwt');

      const promise = service.stopImpersonating();

      const req = httpMock.expectOne(`${API}/api/auth/me`);
      req.flush(mockAdminUser);
      await promise;

      expect(localStorage.getItem('poracle_token')).toBe('admin-jwt');
      expect(localStorage.getItem('poracle_admin_token')).toBeNull();
      expect(service.isImpersonating()).toBe(false);
      expect(router.navigate).toHaveBeenCalledWith(['/admin']);
    });

    it('logs out when there is no admin token to go back to', async () => {
      // Returning silently left a visible Stop impersonating button that did nothing at all -- reachable
      // whenever a 401 discarded the admin token while the banner was still up. See #627.
      await service.stopImpersonating();

      expect(service.isImpersonating()).toBe(false);
      expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: { loggedout: 1 } });
    });
  });

  describe('getTelegramConfig', () => {
    it('should fetch telegram config from API', () => {
      const mockConfig = { botUsername: 'testbot', enabled: true };

      service.getTelegramConfig().subscribe(config => {
        expect(config).toEqual(mockConfig);
      });

      const req = httpMock.expectOne(`${API}/api/auth/telegram/config`);
      expect(req.request.method).toBe('GET');
      req.flush(mockConfig);
    });
  });

  describe('loginWithTelegram', () => {
    it('should post telegram data and store token on success', () => {
      const telegramData = { first_name: 'Test', id: '123', hash: 'abc' };
      const loginResponse = { token: 'new-jwt', user: mockUser };

      service.loginWithTelegram(telegramData).subscribe(res => {
        expect(res).toEqual(loginResponse);
        expect(localStorage.getItem('poracle_token')).toBe('new-jwt');
      });

      const req = httpMock.expectOne(`${API}/api/auth/telegram/verify`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(telegramData);
      req.flush(loginResponse);
    });
  });

  describe('toggleAlerts', () => {
    it('should post to toggle alerts endpoint', () => {
      service.toggleAlerts().subscribe(res => {
        expect(res.enabled).toBe(true);
      });

      const req = httpMock.expectOne(`${API}/api/auth/alerts/toggle`);
      expect(req.request.method).toBe('POST');
      req.flush({ enabled: true });
    });
  });

  describe('waitForUser', () => {
    it('should resolve with null when no token exists', async () => {
      const result = await service.waitForUser();
      expect(result).toBeNull();
    });
  });

  describe('a session discarded by a 401', () => {
    it('forgets the user and the impersonation state, not just the tokens', () => {
      // Left set, currentUser rendered the login page inside the signed-in shell and bounced the user
      // to /dashboard on the next navigation; _isImpersonating kept a banner whose button did nothing.
      // See #627, #628.
      localStorage.setItem('poracle_admin_token', 'admin-jwt');

      service.clearSession();

      expect(service.isLoggedIn()).toBe(false);
      expect(service.isAuthenticated()).toBe(false);
      expect(service.isImpersonating()).toBe(false);
      expect(localStorage.getItem('poracle_admin_token')).toBeNull();
    });
  });

  describe('an inspection ended by a 401', () => {
    it('drops the impersonation state and reloads the admin behind the restored token', () => {
      // The interceptor puts the admin's own token back rather than ending the session; without picking
      // the user back up, the banner kept naming the inspected account and the nav kept its rights.
      // See #706.
      const tokenStore = TestBed.inject(TokenStoreService);
      localStorage.setItem('poracle_token', 'impersonation-jwt');
      localStorage.setItem('poracle_admin_token', 'admin-jwt');
      service.impersonate('impersonation-jwt');
      httpMock.expectOne(`${API}/api/auth/me`).flush(mockUser);
      expect(service.isImpersonating()).toBe(true);

      tokenStore.tryRestoreAdminSession();

      expect(service.isImpersonating()).toBe(false);
      httpMock.expectOne(`${API}/api/auth/me`).flush({ ...mockUser, id: 'admin-1', username: 'admin' });
      expect(service.user()?.username).toBe('admin');
    });
  });

  describe('leaving a session without a burst of 401s', () => {
    it('does not let a stale /me 401 sign out the account the session was just returned to', async () => {
      // The /me sent with a revoked impersonation token can come back after the restored token's own
      // /me has loaded the delegate. Forgetting the user then swapped the shell to its signed-out layout
      // while a perfectly good token sat in storage.
      localStorage.setItem('poracle_token', 'webhook-token');
      const stale = service.loadCurrentUser();
      const staleReq = httpMock.expectOne(`${API}/api/auth/me`);

      localStorage.setItem('poracle_token', 'own-token');
      const fresh = service.loadCurrentUser();
      httpMock.expectOne(`${API}/api/auth/me`).flush({ ...mockUser, username: 'Delegate' });
      await fresh;

      staleReq.flush(null, { status: 401, statusText: 'Unauthorized' });
      await stale;

      expect(service.user()?.username).toBe('Delegate');
      expect(await service.waitForUser()).toEqual(expect.objectContaining({ username: 'Delegate' }));
    });

    it('reports that Stop impersonating had nothing to return to, and sends nothing', async () => {
      // Without the admin token the button logs out. The shell then called loadCounts() regardless, with
      // no token: three 401s and a "session expired" toast on the way to the login page.
      service['currentUser'].set(mockUser);
      (router.navigate as jest.Mock).mockReturnValue(Promise.resolve(true));

      const restored = await service.stopImpersonating();

      expect(restored).toBe(false);
      expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: { loggedout: 1 } });
      httpMock.expectNone(() => true);
    });

    it('reports that Stop impersonating did return to the admin', async () => {
      localStorage.setItem('poracle_admin_token', 'admin-jwt');
      localStorage.setItem('poracle_token', 'impersonated-jwt');

      const promise = service.stopImpersonating();
      httpMock.expectOne(`${API}/api/auth/me`).flush(mockAdminUser);

      expect(await promise).toBe(true);
      expect(service.user()?.username).toBe('AdminUser');
      expect(router.navigate).toHaveBeenCalledWith(['/admin']);
    });

    it('keeps the signed-in shell while "Sign out everywhere" leaves for the provider', () => {
      // Clearing the user swapped layouts before the browser had left the page, and the rebuilt page
      // fired its loads with no token. The page is being unloaded; there is nothing to redraw.
      localStorage.setItem('poracle_token', 'some-token');
      localStorage.setItem('poracle_admin_token', 'admin-token');
      service['currentUser'].set(mockUser);

      service.logout({ sso: true });

      expect(localStorage.getItem('poracle_token')).toBeNull();
      expect(localStorage.getItem('poracle_admin_token')).toBeNull();
      expect(service.isLoggedIn()).toBe(true);
      expect(router.navigate).not.toHaveBeenCalled();
    });
  });
});
