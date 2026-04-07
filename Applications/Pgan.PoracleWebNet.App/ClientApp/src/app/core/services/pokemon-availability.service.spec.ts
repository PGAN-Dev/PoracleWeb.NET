import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { PokemonAvailabilityService } from './pokemon-availability.service';

describe('PokemonAvailabilityService', () => {
  let service: PokemonAvailabilityService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: API } }],
    });
    service = TestBed.inject(PokemonAvailabilityService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should fetch availability data on load', () => {
    service.load();

    const req = httpMock.expectOne(`${API}/api/pokemon-availability`);
    expect(req.request.method).toBe('GET');
    req.flush({ available: [1, 25, 150], enabled: true });

    expect(service.enabled()).toBe(true);
    expect(service.availableIds()).toEqual(new Set([1, 25, 150]));
  });

  it('isAvailable should return true for IDs in the available set', () => {
    service.load();

    httpMock.expectOne(`${API}/api/pokemon-availability`).flush({ available: [1, 4, 7], enabled: true });

    expect(service.isAvailable(4)).toBe(true);
  });

  it('isAvailable should return false for IDs not in the available set', () => {
    service.load();

    httpMock.expectOne(`${API}/api/pokemon-availability`).flush({ available: [1, 4, 7], enabled: true });

    expect(service.isAvailable(999)).toBe(false);
  });

  it('should handle API error gracefully', () => {
    service.load();

    httpMock.expectOne(`${API}/api/pokemon-availability`).flush('Server error', { status: 500, statusText: 'Internal Server Error' });

    expect(service.enabled()).toBe(false);
    expect(service.availableIds()).toEqual(new Set());
  });

  it('load should be idempotent', () => {
    service.load();
    service.load();

    const requests = httpMock.match(`${API}/api/pokemon-availability`);
    expect(requests.length).toBe(1);
    requests[0].flush({ available: [1], enabled: true });
  });

  it('getAvailableCount should return the set size', () => {
    service.load();

    httpMock.expectOne(`${API}/api/pokemon-availability`).flush({ available: [10, 20, 30], enabled: true });

    expect(service.getAvailableCount()).toBe(3);
  });

  it('should set loading to true during fetch and false after', () => {
    expect(service.loading()).toBe(false);

    service.load();
    expect(service.loading()).toBe(true);

    httpMock.expectOne(`${API}/api/pokemon-availability`).flush({ available: [], enabled: false });

    expect(service.loading()).toBe(false);
  });

  it('should preserve enabled state on error after initial load', () => {
    service.load();

    httpMock.expectOne(`${API}/api/pokemon-availability`).flush({ available: [1, 2], enabled: true });
    expect(service.enabled()).toBe(true);
  });
});
