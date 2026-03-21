import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { UserGeofenceService } from './user-geofence.service';
import { ConfigService } from './config.service';

describe('UserGeofenceService', () => {
  let service: UserGeofenceService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: API } }],
    });
    service = TestBed.inject(UserGeofenceService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should fetch custom geofences', () => {
    const geofences = [
      { kojiId: 1, displayName: 'My Park', geofenceName: 'pweb_123_my_park', groupName: 'downtown', parentId: 1, polygon: [] },
      { kojiId: 2, displayName: 'Route 2', geofenceName: 'pweb_123_route_2', groupName: 'suburbs', parentId: 2, polygon: [] },
    ];

    service.getCustomGeofences().subscribe(result => {
      expect(result).toHaveLength(2);
      expect(result[0].displayName).toBe('My Park');
      expect(result[1].kojiId).toBe(2);
    });

    httpMock.expectOne(`${API}/api/geofences/custom`).flush(geofences);
  });

  it('should create a geofence', () => {
    const createData = {
      displayName: 'New Fence',
      groupName: 'downtown',
      parentId: 1,
      polygon: [[40, -74], [41, -74], [41, -73], [40, -73]] as [number, number][],
    };

    const created = {
      kojiId: 0,
      displayName: 'New Fence',
      geofenceName: 'pweb_123_new_fence',
      groupName: 'downtown',
      parentId: 1,
      polygon: [[40, -74], [41, -74], [41, -73], [40, -73]],
    };

    service.createGeofence(createData).subscribe(result => {
      expect(result.geofenceName).toBe('pweb_123_new_fence');
      expect(result.displayName).toBe('New Fence');
    });

    const req = httpMock.expectOne(`${API}/api/geofences/custom`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(createData);
    req.flush(created);
  });

  it('should delete a geofence by id', () => {
    const geofenceId = 42;
    service.deleteGeofence(geofenceId).subscribe();

    const req = httpMock.expectOne(`${API}/api/geofences/custom/${geofenceId}`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should fetch regions', () => {
    const regions = [
      { id: 1, name: 'downtown', displayName: 'Downtown' },
      { id: 2, name: 'suburbs', displayName: 'Suburbs' },
    ];

    service.getRegions().subscribe(result => {
      expect(result).toHaveLength(2);
      expect(result[0].name).toBe('downtown');
      expect(result[1].displayName).toBe('Suburbs');
    });

    httpMock.expectOne(`${API}/api/geofences/regions`).flush(regions);
  });

  it('should submit a geofence for review', () => {
    const kojiName = 'pweb_123_my_park';
    const submitted = {
      id: 1,
      kojiName,
      displayName: 'My Park',
      status: 'pending_review',
      submittedAt: '2026-03-21T00:00:00Z',
    };

    service.submitForReview(kojiName).subscribe(result => {
      expect(result.status).toBe('pending_review');
      expect(result.submittedAt).toBeTruthy();
    });

    const req = httpMock.expectOne(`${API}/api/geofences/custom/${encodeURIComponent(kojiName)}/submit`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({});
    req.flush(submitted);
  });
});
