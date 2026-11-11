import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';

import { authGuard } from './auth.guard';
import { UserInfo } from '../models';
import { AuthService } from '../services/auth.service';
import { I18nService } from '../services/i18n.service';
import { ToastService } from '../services/toast.service';

describe('authGuard', () => {
  let authService: {
    isAuthenticated: jest.Mock;
    retrySessionInBackground: jest.Mock;
    sessionUnavailable: jest.Mock;
    waitForUserWithRetry: jest.Mock;
  };
  let toast: { error: jest.Mock };
  let router: { createUrlTree: jest.Mock };
  const mockRoute = {} as ActivatedRouteSnapshot;
  const mockState = {} as RouterStateSnapshot;

  const mockUser: UserInfo = {
    id: '123',
    username: 'TestUser',
    avatarUrl: null,
    enabled: true,
    isAdmin: false,
    managedDestinations: [],
    profileName: 'Default',
    profileNo: 1,
    type: 'discord:user',
  };

  beforeEach(() => {
    authService = {
      isAuthenticated: jest.fn(),
      retrySessionInBackground: jest.fn(),
      sessionUnavailable: jest.fn().mockReturnValue(false),
      waitForUserWithRetry: jest.fn(),
    };
    toast = { error: jest.fn() };
    router = {
      createUrlTree: jest.fn().mockReturnValue('login-url-tree' as unknown as UrlTree),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: Router, useValue: router },
        { provide: ToastService, useValue: toast },
        { provide: I18nService, useValue: { instant: (k: string) => k } },
      ],
    });
  });

  it('should allow access when user is authenticated and loaded', async () => {
    authService.isAuthenticated.mockReturnValue(true);
    authService.waitForUserWithRetry.mockResolvedValue(mockUser);

    const result = await TestBed.runInInjectionContext(() => authGuard(mockRoute, mockState));

    expect(result).toBe(true);
  });

  it('should redirect to login when not authenticated', async () => {
    authService.isAuthenticated.mockReturnValue(false);

    const result = await TestBed.runInInjectionContext(() => authGuard(mockRoute, mockState));

    expect(router.createUrlTree).toHaveBeenCalledWith(['/login']);
    expect(result).toBe('login-url-tree');
  });

  it('should redirect to login when authenticated but user fails to load', async () => {
    authService.isAuthenticated.mockReturnValue(true);
    authService.waitForUserWithRetry.mockResolvedValue(null);

    const result = await TestBed.runInInjectionContext(() => authGuard(mockRoute, mockState));

    expect(router.createUrlTree).toHaveBeenCalledWith(['/login']);
    expect(result).toBe('login-url-tree');
  });

  it('should redirect to login on a 401 (user null, session not unavailable)', async () => {
    authService.isAuthenticated.mockReturnValue(true);
    authService.waitForUserWithRetry.mockResolvedValue(null);
    authService.sessionUnavailable.mockReturnValue(false);

    const result = await TestBed.runInInjectionContext(() => authGuard(mockRoute, mockState));

    expect(result).toBe('login-url-tree');
    expect(toast.error).not.toHaveBeenCalled();
  });

  it('should stay on the route with a message when /api/auth/me is only unavailable', async () => {
    authService.isAuthenticated.mockReturnValue(true);
    authService.waitForUserWithRetry.mockResolvedValue(null);
    authService.sessionUnavailable.mockReturnValue(true);

    const result = await TestBed.runInInjectionContext(() => authGuard(mockRoute, mockState));

    expect(result).toBe(true);
    expect(router.createUrlTree).not.toHaveBeenCalled();
    expect(toast.error).toHaveBeenCalledWith('ERROR.SESSION_UNAVAILABLE');
    expect(authService.retrySessionInBackground).toHaveBeenCalled();
  });
});
