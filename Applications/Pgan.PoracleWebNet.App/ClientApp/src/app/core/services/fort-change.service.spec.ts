import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { FortChangeService } from './fort-change.service';
import { FortChange } from '../models';

describe('FortChangeService', () => {
  let service: FortChangeService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  const mockFortChange: FortChange = {
    id: '1',
    uid: 1,
    changeTypes: ['name', 'location'],
    clean: 0,
    distance: 0,
    fortType: 'pokestop',
    includeEmpty: 0,
    ping: null,
    profileNo: 1,
    template: null,
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: API } }],
    });
    service = TestBed.inject(FortChangeService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should fetch all fort changes', () => {
    service.getAll().subscribe(items => {
      expect(items).toHaveLength(1);
      expect(items[0].fortType).toBe('pokestop');
    });
    httpMock.expectOne(`${API}/api/fort-changes`).flush([mockFortChange]);
  });

  it('should create a fort change', () => {
    const { id, uid, profileNo, ...create } = mockFortChange;
    service.create(create).subscribe(result => {
      expect(result.uid).toBe(1);
    });
    const req = httpMock.expectOne(`${API}/api/fort-changes`);
    expect(req.request.method).toBe('POST');
    req.flush(mockFortChange);
  });

  it('should update a fort change', () => {
    service.update(1, { fortType: 'gym' }).subscribe();
    const req = httpMock.expectOne(`${API}/api/fort-changes/1`);
    expect(req.request.method).toBe('PUT');
    req.flush(null);
  });

  it('should delete a fort change', () => {
    service.delete(1).subscribe();
    const req = httpMock.expectOne(`${API}/api/fort-changes/1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should delete all fort changes', () => {
    service.deleteAll().subscribe();
    const req = httpMock.expectOne(`${API}/api/fort-changes`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should update all distances', () => {
    service.updateAllDistance(5000).subscribe();
    const req = httpMock.expectOne(`${API}/api/fort-changes/distance`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toBe(5000);
    req.flush(null);
  });

  it('should update bulk distances', () => {
    service.updateBulkDistance([1, 2], 3000).subscribe();
    const req = httpMock.expectOne(`${API}/api/fort-changes/distance/bulk`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ uids: [1, 2], distance: 3000 });
    req.flush(null);
  });
});
