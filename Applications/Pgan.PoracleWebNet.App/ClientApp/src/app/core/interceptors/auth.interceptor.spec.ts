import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should add Authorization header when token exists for relative URLs', () => {
    localStorage.setItem('poracle_token', 'my-jwt-token');

    http.get('/api/dashboard').subscribe();

    const req = httpMock.expectOne('/api/dashboard');
    expect(req.request.headers.get('Authorization')).toBe('Bearer my-jwt-token');
    req.flush({});
  });

  it('should not add Authorization header when no token exists', () => {
    http.get('/api/dashboard').subscribe();

    const req = httpMock.expectOne('/api/dashboard');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });

  it('should not add Authorization header for external URLs', () => {
    localStorage.setItem('poracle_token', 'my-jwt-token');

    http.get('https://raw.githubusercontent.com/some-file.json').subscribe();

    const req = httpMock.expectOne('https://raw.githubusercontent.com/some-file.json');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });

  it('should not add header for empty/whitespace token', () => {
    localStorage.setItem('poracle_token', '  ');

    http.get('/api/dashboard').subscribe();

    const req = httpMock.expectOne('/api/dashboard');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });
});
