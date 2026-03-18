import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';

import { AuthService } from './auth.service';
import { ConfigService } from './config.service';
import { UserInfo } from '../models';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;
  let router: Router;
  const API = 'http://test-api';

  const mockUser: UserInfo = {
    avatarUrl: 'https://example.com/avatar.png',
    enabled: true,
    id: '123456',
    isAdmin: false,
    managedWebhooks: [],
    profileName: 'Default',
    profileNo: 1,
    type: 'discord:user',
    username: 'TestUser',
  };

  const mockAdminUser: UserInfo = {
    ...mockUser,
    isAdmin: true,
    username: 'AdminUser',
  };

  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: API } },
        {
          provide: Router,
          useValue: { navigate: jest.fn(), createUrlTree: jest.fn() },
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
    it('should store token, load user, and navigate to dashboard', () => {
      service.handleTokenFromCallback('new-token');

      expect(localStorage.getItem('poracle_token')).toBe('new-token');
      expect(router.navigate).toHaveBeenCalledWith(['/dashboard']);

      const req = httpMock.expectOne(`${API}/api/auth/me`);
      req.flush(mockUser);
    });
  });

  describe('logout', () => {
    it('should clear tokens, reset user, and navigate to login', () => {
      localStorage.setItem('poracle_token', 'some-token');
      localStorage.setItem('poracle_admin_token', 'admin-token');

      service.logout();

      expect(localStorage.getItem('poracle_token')).toBeNull();
      expect(localStorage.getItem('poracle_admin_token')).toBeNull();
      expect(service.isLoggedIn()).toBe(false);
      expect(service.isImpersonating()).toBe(false);
      expect(router.navigate).toHaveBeenCalledWith(['/login']);
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

    it('should clear token and user on 401 error', async () => {
      localStorage.setItem('poracle_token', 'bad-token');
      const promise = service.loadCurrentUser();

      const req = httpMock.expectOne(`${API}/api/auth/me`);
      req.flush(null, { status: 401, statusText: 'Unauthorized' });

      const result = await promise;
      expect(result).toBeNull();
      expect(localStorage.getItem('poracle_token')).toBeNull();
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

    it('should do nothing when no admin token exists', async () => {
      await service.stopImpersonating();
      expect(router.navigate).not.toHaveBeenCalled();
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
      const telegramData = { id: '123', first_name: 'Test', hash: 'abc' };
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
});
