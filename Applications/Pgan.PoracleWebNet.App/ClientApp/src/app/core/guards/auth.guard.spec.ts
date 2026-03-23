import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';

import { authGuard } from './auth.guard';
import { UserInfo } from '../models';
import { AuthService } from '../services/auth.service';

describe('authGuard', () => {
  let authService: {
    isAuthenticated: jest.Mock;
    waitForUser: jest.Mock;
  };
  let router: { createUrlTree: jest.Mock };
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

  beforeEach(() => {
    authService = {
      isAuthenticated: jest.fn(),
      waitForUser: jest.fn(),
    };
    router = {
      createUrlTree: jest.fn().mockReturnValue('login-url-tree' as unknown as UrlTree),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: Router, useValue: router },
      ],
    });
  });

  it('should allow access when user is authenticated and loaded', async () => {
    authService.isAuthenticated.mockReturnValue(true);
    authService.waitForUser.mockResolvedValue(mockUser);

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
    authService.waitForUser.mockResolvedValue(null);

    const result = await TestBed.runInInjectionContext(() => authGuard(mockRoute, mockState));

    expect(router.createUrlTree).toHaveBeenCalledWith(['/login']);
    expect(result).toBe('login-url-tree');
  });
});
