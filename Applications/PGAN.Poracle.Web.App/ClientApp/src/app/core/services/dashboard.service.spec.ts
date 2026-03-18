import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { DashboardService } from './dashboard.service';

describe('DashboardService', () => {
  let service: DashboardService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: 'http://test-api' } }],
    });
    service = TestBed.inject(DashboardService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should fetch dashboard counts', () => {
    const mockCounts = {
      raids: 7,
      eggs: 1,
      gyms: 2,
      invasions: 3,
      lures: 4,
      nests: 5,
      pokemon: 10,
      quests: 6,
    };

    service.getCounts().subscribe(counts => {
      expect(counts).toEqual(mockCounts);
    });

    const req = httpMock.expectOne('http://test-api/api/dashboard');
    expect(req.request.method).toBe('GET');
    req.flush(mockCounts);
  });
});
