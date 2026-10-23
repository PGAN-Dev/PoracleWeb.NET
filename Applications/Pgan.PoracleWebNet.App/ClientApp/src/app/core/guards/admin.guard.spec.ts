import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';

import { ADMIN_RETURN_STATE } from './admin-return';
import { adminGuard } from './admin.guard';
import { UserInfo } from '../models';
import { AuthService } from '../services/auth.service';
import { I18nService } from '../services/i18n.service';
import { ToastService } from '../services/toast.service';

describe('adminGuard', () => {
  let authService: { waitForUser: jest.Mock };
  let router: { createUrlTree: jest.Mock; currentNavigation: jest.Mock };
  let toast: { error: jest.Mock };
  const mockRoute = {} as ActivatedRouteSnapshot;
  const mockState = {} as RouterStateSnapshot;

  const mockUser: UserInfo = {
    id: '123',
    username: 'TestUser',
    avatarUrl: null,
    enabled: true,
    isAdmin: false,
    managedWebhooks: [],
    profileName: 'Default',
    profileNo: 1,
    type: 'discord:user',
  };

  const mockAdmin: UserInfo = { ...mockUser, isAdmin: true };

  beforeEach(() => {
    authService = { waitForUser: jest.fn() };
    toast = { error: jest.fn() };
    router = {
      createUrlTree: jest.fn().mockReturnValue('dashboard-url-tree' as unknown as UrlTree),
      currentNavigation: jest.fn().mockReturnValue(null),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: Router, useValue: router },
        { provide: ToastService, useValue: toast },
        { provide: I18nService, useValue: { instant: (key: string) => key } },
      ],
    });
  });

  it('should allow access for admin users', async () => {
    authService.waitForUser.mockResolvedValue(mockAdmin);

    const result = await TestBed.runInInjectionContext(() => adminGuard(mockRoute, mockState));

    expect(result).toBe(true);
  });

  it('should redirect non-admin users to dashboard', async () => {
    authService.waitForUser.mockResolvedValue(mockUser);

    const result = await TestBed.runInInjectionContext(() => adminGuard(mockRoute, mockState));

    expect(router.createUrlTree).toHaveBeenCalledWith(['/dashboard']);
    expect(result).toBe('dashboard-url-tree');
  });

  /** A delegate following an admin link was bounced with no word, and took the link for broken. */
  it('tells a signed-in non-admin why they were sent to the dashboard', async () => {
    authService.waitForUser.mockResolvedValue(mockUser);

    await TestBed.runInInjectionContext(() => adminGuard(mockRoute, mockState));

    expect(toast.error).toHaveBeenCalledWith('ERROR.ADMIN_ONLY');
  });

  /** Ending an impersonation goes back to /admin, and a delegate who started it is no admin. */
  it('stays quiet when the app itself is returning from an impersonation', async () => {
    authService.waitForUser.mockResolvedValue(mockUser);
    router.currentNavigation.mockReturnValue({ extras: { state: ADMIN_RETURN_STATE } });

    const result = await TestBed.runInInjectionContext(() => adminGuard(mockRoute, mockState));

    expect(result).toBe('dashboard-url-tree');
    expect(toast.error).not.toHaveBeenCalled();
  });

  it('lets an admin through without a toast', async () => {
    authService.waitForUser.mockResolvedValue(mockAdmin);

    await TestBed.runInInjectionContext(() => adminGuard(mockRoute, mockState));

    expect(toast.error).not.toHaveBeenCalled();
  });

  it('should redirect to dashboard when user is null', async () => {
    authService.waitForUser.mockResolvedValue(null);

    const result = await TestBed.runInInjectionContext(() => adminGuard(mockRoute, mockState));

    expect(router.createUrlTree).toHaveBeenCalledWith(['/dashboard']);
    expect(result).toBe('dashboard-url-tree');
    // No user means the sign-in failed or ended; the session's own messages cover that.
    expect(toast.error).not.toHaveBeenCalled();
  });
});
