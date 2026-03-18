import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';

import { adminGuard } from './admin.guard';
import { AuthService } from '../services/auth.service';
import { UserInfo } from '../models';

describe('adminGuard', () => {
  let authService: { waitForUser: jest.Mock };
  let router: { createUrlTree: jest.Mock };
  const mockRoute = {} as ActivatedRouteSnapshot;
  const mockState = {} as RouterStateSnapshot;

  const mockUser: UserInfo = {
    avatarUrl: null, enabled: true, id: '123', isAdmin: false,
    managedWebhooks: [], profileName: 'Default', profileNo: 1,
    type: 'discord:user', username: 'TestUser',
  };

  const mockAdmin: UserInfo = { ...mockUser, isAdmin: true };

  beforeEach(() => {
    authService = { waitForUser: jest.fn() };
    router = {
      createUrlTree: jest.fn().mockReturnValue('dashboard-url-tree' as unknown as UrlTree),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: Router, useValue: router },
      ],
    });
  });

  it('should allow access for admin users', async () => {
    authService.waitForUser.mockResolvedValue(mockAdmin);

    const result = await TestBed.runInInjectionContext(() =>
      adminGuard(mockRoute, mockState),
    );

    expect(result).toBe(true);
  });

  it('should redirect non-admin users to dashboard', async () => {
    authService.waitForUser.mockResolvedValue(mockUser);

    const result = await TestBed.runInInjectionContext(() =>
      adminGuard(mockRoute, mockState),
    );

    expect(router.createUrlTree).toHaveBeenCalledWith(['/dashboard']);
    expect(result).toBe('dashboard-url-tree');
  });

  it('should redirect to dashboard when user is null', async () => {
    authService.waitForUser.mockResolvedValue(null);

    const result = await TestBed.runInInjectionContext(() =>
      adminGuard(mockRoute, mockState),
    );

    expect(router.createUrlTree).toHaveBeenCalledWith(['/dashboard']);
    expect(result).toBe('dashboard-url-tree');
  });
});
