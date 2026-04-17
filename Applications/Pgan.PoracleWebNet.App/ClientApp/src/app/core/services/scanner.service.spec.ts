import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';

import { ScannerService } from './scanner.service';

describe('ScannerService', () => {
  let service: ScannerService;
  let httpMock: HttpTestingController;
  let snackBar: jest.Mocked<MatSnackBar>;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: MatSnackBar, useValue: { open: jest.fn() } }],
    });
    service = TestBed.inject(ScannerService);
    httpMock = TestBed.inject(HttpTestingController);
    snackBar = TestBed.inject(MatSnackBar) as jest.Mocked<MatSnackBar>;
  });

  afterEach(() => httpMock.verify());

  it('searchGyms returns empty without hitting HTTP when search < 2 chars', () => {
    let result: unknown;
    service.searchGyms('a').subscribe(r => (result = r));
    httpMock.expectNone('/api/scanner/gyms');
    expect(result).toEqual([]);
  });

  it('searchGyms forwards search + limit to the API', () => {
    let result: unknown;
    service.searchGyms('abc', 15).subscribe(r => (result = r));
    const req = httpMock.expectOne(r => r.url === '/api/scanner/gyms');
    expect(req.request.params.get('search')).toBe('abc');
    expect(req.request.params.get('limit')).toBe('15');
    req.flush([]);
    expect(result).toEqual([]);
  });

  it('searchGyms shows snackbar and returns empty on 429', () => {
    let result: unknown;
    service.searchGyms('abc').subscribe(r => (result = r));
    const req = httpMock.expectOne(r => r.url === '/api/scanner/gyms');
    req.flush('rate limited', { status: 429, statusText: 'Too Many Requests' });
    expect(snackBar.open).toHaveBeenCalledWith(expect.stringContaining('Too many scanner requests'), 'OK', { duration: 4000 });
    expect(result).toEqual([]);
  });

  it('searchGyms returns empty silently on 500 (no snackbar)', () => {
    let result: unknown;
    service.searchGyms('abc').subscribe(r => (result = r));
    const req = httpMock.expectOne(r => r.url === '/api/scanner/gyms');
    req.flush('boom', { status: 500, statusText: 'Server Error' });
    expect(snackBar.open).not.toHaveBeenCalled();
    expect(result).toEqual([]);
  });

  it('getGymById encodes the id in the URL and returns null on 404', () => {
    let result: unknown;
    service.getGymById('gym/with slash').subscribe(r => (result = r));
    const req = httpMock.expectOne(`/api/scanner/gyms/${encodeURIComponent('gym/with slash')}`);
    req.flush('not found', { status: 404, statusText: 'Not Found' });
    expect(result).toBeNull();
  });

  it('getGymById shows snackbar on 429', () => {
    service.getGymById('abc').subscribe();
    const req = httpMock.expectOne('/api/scanner/gyms/abc');
    req.flush('rate limited', { status: 429, statusText: 'Too Many Requests' });
    expect(snackBar.open).toHaveBeenCalledTimes(1);
  });

  it('getMaxBattlePokemonIds returns [] on 500 without snackbar', () => {
    let result: unknown;
    service.getMaxBattlePokemonIds().subscribe(r => (result = r));
    const req = httpMock.expectOne('/api/scanner/max-battle-pokemon');
    req.flush('boom', { status: 500, statusText: 'Server Error' });
    expect(snackBar.open).not.toHaveBeenCalled();
    expect(result).toEqual([]);
  });

  it('getMaxBattlePokemonIds shows snackbar on 429', () => {
    service.getMaxBattlePokemonIds().subscribe();
    const req = httpMock.expectOne('/api/scanner/max-battle-pokemon');
    req.flush('rate limited', { status: 429, statusText: 'Too Many Requests' });
    expect(snackBar.open).toHaveBeenCalledTimes(1);
  });
});
