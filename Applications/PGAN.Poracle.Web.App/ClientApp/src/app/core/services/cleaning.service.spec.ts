import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { CleaningService } from './cleaning.service';
import { ConfigService } from './config.service';

describe('CleaningService', () => {
  let service: CleaningService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: API } }],
    });
    service = TestBed.inject(CleaningService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should enable clean for all alarms', () => {
    service.toggleAll(true).subscribe(result => {
      expect(result.updated).toBe(10);
    });

    const req = httpMock.expectOne(`${API}/api/cleaning/all/1`);
    expect(req.request.method).toBe('PUT');
    req.flush({ updated: 10 });
  });

  it('should enable clean for a specific type', () => {
    service.toggleClean('monsters', true).subscribe(result => {
      expect(result.updated).toBe(5);
    });

    const req = httpMock.expectOne(`${API}/api/cleaning/monsters/1`);
    expect(req.request.method).toBe('PUT');
    req.flush({ updated: 5 });
  });

  it('should disable clean for a specific type', () => {
    service.toggleClean('raids', false).subscribe(result => {
      expect(result.updated).toBe(3);
    });

    const req = httpMock.expectOne(`${API}/api/cleaning/raids/0`);
    expect(req.request.method).toBe('PUT');
    req.flush({ updated: 3 });
  });

  it('should work with all alarm types', () => {
    const types = ['monsters', 'raids', 'eggs', 'quests', 'invasions', 'lures', 'nests', 'gyms'] as const;

    for (const type of types) {
      service.toggleClean(type, true).subscribe();
      httpMock.expectOne(`${API}/api/cleaning/${type}/1`).flush({ updated: 1 });
    }
  });
});
