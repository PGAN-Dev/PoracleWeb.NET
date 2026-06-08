import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { TokenStoreService } from './token-store.service';

describe('TokenStoreService', () => {
  let service: TokenStoreService;
  let httpMock: HttpTestingController;

  const REFRESH_URL = '/api/auth/oidc/refresh';

  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: '' } }],
    });
    service = TestBed.inject(TokenStoreService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('reports hasRefreshToken based on storage', () => {
    expect(service.hasRefreshToken()).toBe(false);
    localStorage.setItem('poracle_refresh_token', 'rt');
    expect(service.hasRefreshToken()).toBe(true);
  });

  it('storeTokens persists token, refresh token and computed expiry', () => {
    service.storeTokens('jwt', 'rt', 1800);
    expect(localStorage.getItem('poracle_token')).toBe('jwt');
    expect(localStorage.getItem('poracle_refresh_token')).toBe('rt');
    const exp = Number(localStorage.getItem('poracle_token_expires_at'));
    expect(exp).toBeGreaterThan(Date.now());
  });

  it('isExpiringSoon is true within the skew window and false outside it', () => {
    localStorage.setItem('poracle_token_expires_at', String(Date.now() + 30_000));
    expect(service.isExpiringSoon()).toBe(true);

    localStorage.setItem('poracle_token_expires_at', String(Date.now() + 120_000));
    expect(service.isExpiringSoon()).toBe(false);
  });

  it('single-flights concurrent refresh() calls into one HTTP request', () => {
    localStorage.setItem('poracle_refresh_token', 'rt-1');

    const tokens: string[] = [];
    service.refresh().subscribe(t => tokens.push(t));
    service.refresh().subscribe(t => tokens.push(t));

    const req = httpMock.expectOne(REFRESH_URL);
    expect(req.request.body).toEqual({ refreshToken: 'rt-1' });
    req.flush({ expiresIn: 1800, refreshToken: 'rt-2', token: 'new-jwt' });

    expect(tokens).toEqual(['new-jwt', 'new-jwt']);
    expect(localStorage.getItem('poracle_token')).toBe('new-jwt');
    expect(localStorage.getItem('poracle_refresh_token')).toBe('rt-2');
  });

  it('clears tokens and emits forceLogout$ on refresh failure', () => {
    localStorage.setItem('poracle_refresh_token', 'rt-1');

    let forced = false;
    service.forceLogout$.subscribe(() => (forced = true));

    service.refresh().subscribe({ error: () => undefined });
    httpMock.expectOne(REFRESH_URL).flush({ error: 'invalid_grant' }, { status: 401, statusText: 'Unauthorized' });

    expect(forced).toBe(true);
    expect(localStorage.getItem('poracle_refresh_token')).toBeNull();
    expect(localStorage.getItem('poracle_token_expires_at')).toBeNull();
  });

  it('emits forceLogout$ immediately when refresh is called with no refresh token', () => {
    let forced = false;
    service.forceLogout$.subscribe(() => (forced = true));

    service.refresh().subscribe({ error: () => undefined });

    expect(forced).toBe(true);
    httpMock.expectNone(REFRESH_URL);
  });

  it('revoke() posts to the revoke endpoint when a refresh token exists', () => {
    localStorage.setItem('poracle_refresh_token', 'rt-1');
    service.revoke();
    const req = httpMock.expectOne('/api/auth/oidc/refresh/revoke');
    expect(req.request.body).toEqual({ refreshToken: 'rt-1' });
    req.flush({});
  });

  it('revoke() does nothing without a refresh token', () => {
    service.revoke();
    httpMock.expectNone('/api/auth/oidc/refresh/revoke');
  });
});
