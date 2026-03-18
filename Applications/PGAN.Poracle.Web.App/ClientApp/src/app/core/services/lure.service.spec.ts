import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { LureService } from './lure.service';
import { Lure, LureCreate } from '../models';

describe('LureService', () => {
  let service: LureService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  const mockLure: Lure = {
    clean: 0, distance: 0, id: '1', lureId: 502,
    ping: null, profileNo: 1, template: null, uid: 1,
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
    service = TestBed.inject(LureService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should fetch all lures', () => {
    service.getAll().subscribe(lures => {
      expect(lures).toHaveLength(1);
      expect(lures[0].lureId).toBe(502);
    });

    httpMock.expectOne(`${API}/api/lures`).flush([mockLure]);
  });

  it('should create a lure', () => {
    const payload: LureCreate = {
      clean: 0, distance: 0, lureId: 503,
      ping: null, profileNo: 1, template: null,
    };

    service.create(payload).subscribe(result => {
      expect(result.uid).toBe(1);
    });

    const req = httpMock.expectOne(`${API}/api/lures`);
    expect(req.request.method).toBe('POST');
    req.flush(mockLure);
  });

  it('should update a lure', () => {
    service.update(1, { lureId: 504 }).subscribe();
    const req = httpMock.expectOne(`${API}/api/lures/1`);
    expect(req.request.method).toBe('PUT');
    req.flush(null);
  });

  it('should delete a lure', () => {
    service.delete(1).subscribe();
    httpMock.expectOne(`${API}/api/lures/1`).flush(null);
  });

  it('should delete all lures', () => {
    service.deleteAll().subscribe();
    httpMock.expectOne(`${API}/api/lures`).flush(null);
  });

  it('should update all distances', () => {
    service.updateAllDistance(500).subscribe();
    const req = httpMock.expectOne(`${API}/api/lures/distance`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toBe(500);
    req.flush(null);
  });
});
