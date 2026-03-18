import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { InvasionService } from './invasion.service';
import { Invasion, InvasionCreate } from '../models';

describe('InvasionService', () => {
  let service: InvasionService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  const mockInvasion: Invasion = {
    clean: 0, distance: 0, gender: 0, gruntType: 'Water',
    id: '1', ping: null, profileNo: 1, template: null, uid: 1,
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: API } },
      ],
    });
    service = TestBed.inject(InvasionService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should fetch all invasions', () => {
    service.getAll().subscribe(invasions => {
      expect(invasions).toHaveLength(1);
      expect(invasions[0].gruntType).toBe('Water');
    });

    const req = httpMock.expectOne(`${API}/api/invasions`);
    expect(req.request.method).toBe('GET');
    req.flush([mockInvasion]);
  });

  it('should create an invasion', () => {
    const payload: InvasionCreate = {
      clean: 0, distance: 0, gender: 1, gruntType: 'Fire',
      ping: null, profileNo: 1, template: null,
    };

    service.create(payload).subscribe(result => {
      expect(result.uid).toBe(1);
    });

    const req = httpMock.expectOne(`${API}/api/invasions`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.gruntType).toBe('Fire');
    req.flush(mockInvasion);
  });

  it('should update an invasion', () => {
    service.update(1, { gender: 2 }).subscribe();

    const req = httpMock.expectOne(`${API}/api/invasions/1`);
    expect(req.request.method).toBe('PUT');
    req.flush(null);
  });

  it('should delete an invasion', () => {
    service.delete(7).subscribe();

    const req = httpMock.expectOne(`${API}/api/invasions/7`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should delete all invasions', () => {
    service.deleteAll().subscribe();

    const req = httpMock.expectOne(`${API}/api/invasions`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should update all distances', () => {
    service.updateAllDistance(1000).subscribe();

    const req = httpMock.expectOne(`${API}/api/invasions/distance`);
    expect(req.request.method).toBe('PUT');
    req.flush(null);
  });
});
