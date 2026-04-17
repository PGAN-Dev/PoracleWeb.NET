import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';

import { errorInterceptor } from './error.interceptor';
import { ToastService } from '../services/toast.service';

describe('errorInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let toast: { error: jest.Mock };
  let router: { navigate: jest.Mock };

  beforeEach(() => {
    localStorage.clear();
    toast = { error: jest.fn() };
    router = { navigate: jest.fn() };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toast },
        { provide: Router, useValue: router },
        { provide: TranslateService, useValue: { instant: jest.fn((key: string) => key) } },
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should remove token and redirect on 401', () => {
    localStorage.setItem('poracle_token', 'expired-token');

    http.get('/api/dashboard').subscribe({ error: () => {} });

    httpMock.expectOne('/api/dashboard').flush(null, {
      status: 401,
      statusText: 'Unauthorized',
    });

    expect(localStorage.getItem('poracle_token')).toBeNull();
    expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: {} });
    expect(toast.error).toHaveBeenCalledWith('ERROR.SESSION_EXPIRED');
  });

  it('should show permission toast for 403 without disableKey', () => {
    http.get('/api/admin/users').subscribe({ error: () => {} });

    httpMock.expectOne('/api/admin/users').flush(null, {
      status: 403,
      statusText: 'Forbidden',
    });

    expect(toast.error).toHaveBeenCalledWith('ERROR.PERMISSION_DENIED');
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('should show feature-disabled toast and redirect to /dashboard for 403 with disableKey', () => {
    // Backend tags "feature disabled" 403s by including disableKey in the body so the
    // SPA can distinguish them from generic permission denials and bounce the user off
    // the now-broken page. (#236)
    http.get('/api/monsters').subscribe({ error: () => {} });

    httpMock
      .expectOne('/api/monsters')
      .flush(
        { disableKey: 'disable_mons', error: 'This feature is disabled by the administrator.' },
        { status: 403, statusText: 'Forbidden' },
      );

    expect(toast.error).toHaveBeenCalledWith('ERROR.FEATURE_DISABLED');
    expect(router.navigate).toHaveBeenCalledWith(['/dashboard']);
  });

  it('should show not found toast for 404', () => {
    http.get('/api/monsters/999').subscribe({ error: () => {} });

    httpMock.expectOne('/api/monsters/999').flush(null, {
      status: 404,
      statusText: 'Not Found',
    });

    expect(toast.error).toHaveBeenCalledWith('ERROR.NOT_FOUND');
  });

  it('should show server error toast for 500', () => {
    http.get('/api/dashboard').subscribe({ error: () => {} });

    httpMock.expectOne('/api/dashboard').flush(null, {
      status: 500,
      statusText: 'Error',
    });

    expect(toast.error).toHaveBeenCalledWith('ERROR.GENERIC');
  });

  it('should show unavailable toast for 502/503/504', () => {
    for (const status of [502, 503, 504]) {
      jest.clearAllMocks();
      http.get('/api/dashboard').subscribe({ error: () => {} });

      httpMock.expectOne('/api/dashboard').flush(null, {
        status,
        statusText: 'Unavailable',
      });

      expect(toast.error).toHaveBeenCalledWith('ERROR.SERVER_UNAVAILABLE');
    }
  });

  it('should show network error toast for status 0', () => {
    http.get('/api/dashboard').subscribe({ error: () => {} });

    httpMock.expectOne('/api/dashboard').error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });

    expect(toast.error).toHaveBeenCalledWith('ERROR.NETWORK');
  });

  describe('silent endpoints', () => {
    it('should NOT show toast for /api/config errors', () => {
      http.get('/api/config/templates').subscribe({ error: () => {} });

      httpMock.expectOne('/api/config/templates').flush(null, {
        status: 500,
        statusText: 'Error',
      });

      expect(toast.error).not.toHaveBeenCalled();
    });

    it('should NOT show toast for /api/masterdata errors', () => {
      http.get('/api/masterdata/pokemon').subscribe({ error: () => {} });

      httpMock.expectOne('/api/masterdata/pokemon').flush(null, {
        status: 500,
        statusText: 'Error',
      });

      expect(toast.error).not.toHaveBeenCalled();
    });

    it('should NOT show toast for /api/auth/me errors but should still redirect on 401', () => {
      localStorage.setItem('poracle_token', 'token');
      http.get('/api/auth/me').subscribe({ error: () => {} });

      httpMock.expectOne('/api/auth/me').flush(null, {
        status: 401,
        statusText: 'Unauthorized',
      });

      expect(toast.error).not.toHaveBeenCalled();
      expect(localStorage.getItem('poracle_token')).toBeNull();
      expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: {} });
    });

    it('should NOT show toast for /api/admin/users/avatars errors', () => {
      http.post('/api/admin/users/avatars', ['123']).subscribe({ error: () => {} });

      httpMock.expectOne('/api/admin/users/avatars').flush(null, {
        status: 500,
        statusText: 'Error',
      });

      expect(toast.error).not.toHaveBeenCalled();
    });

    it('should NOT show toast for /api/settings errors', () => {
      http.get('/api/settings').subscribe({ error: () => {} });

      httpMock.expectOne('/api/settings').flush(null, {
        status: 401,
        statusText: 'Unauthorized',
      });

      expect(toast.error).not.toHaveBeenCalled();
    });
  });

  it('should re-throw the error for downstream handling', () => {
    let receivedError: any;

    http.get('/api/dashboard').subscribe({
      error: err => {
        receivedError = err;
      },
    });

    httpMock.expectOne('/api/dashboard').flush(null, {
      status: 500,
      statusText: 'Error',
    });

    expect(receivedError).toBeDefined();
    expect(receivedError.status).toBe(500);
  });
});
