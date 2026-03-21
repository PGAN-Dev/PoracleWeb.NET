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
      { id: 1, displayName: 'My Park', geofenceName: 'my_park', groupName: 'downtown', humanId: '123', parentId: 1, polygonJson: '[]', profileNo: 1, createdAt: '', updatedAt: '' },
      { id: 2, displayName: 'Route 2', geofenceName: 'route_2', groupName: 'suburbs', humanId: '123', parentId: 2, polygonJson: '[]', profileNo: 1, createdAt: '', updatedAt: '' },
    ];

    service.getCustomGeofences().subscribe(result => {
      expect(result).toHaveLength(2);
      expect(result[0].displayName).toBe('My Park');
      expect(result[1].id).toBe(2);
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
      id: 3,
      displayName: 'New Fence',
      geofenceName: 'new_fence',
      groupName: 'downtown',
      humanId: '123',
      parentId: 1,
      polygonJson: '[[40,-74],[41,-74],[41,-73],[40,-73]]',
      profileNo: 1,
      createdAt: '2026-01-01',
      updatedAt: '2026-01-01',
    };

    service.createGeofence(createData).subscribe(result => {
      expect(result.id).toBe(3);
      expect(result.displayName).toBe('New Fence');
    });

    const req = httpMock.expectOne(`${API}/api/geofences/custom`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(createData);
    req.flush(created);
  });

  it('should update a geofence', () => {
    const updateData = {
      displayName: 'Updated Fence',
      groupName: 'suburbs',
      parentId: 2,
      polygon: [[42, -75], [43, -75], [43, -74], [42, -74]] as [number, number][],
    };

    const updated = {
      id: 5,
      displayName: 'Updated Fence',
      geofenceName: 'updated_fence',
      groupName: 'suburbs',
      humanId: '123',
      parentId: 2,
      polygonJson: '[]',
      profileNo: 1,
      createdAt: '2026-01-01',
      updatedAt: '2026-01-02',
    };

    service.updateGeofence(5, updateData).subscribe(result => {
      expect(result.displayName).toBe('Updated Fence');
    });

    const req = httpMock.expectOne(`${API}/api/geofences/custom/5`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(updateData);
    req.flush(updated);
  });

  it('should delete a geofence', () => {
    service.deleteGeofence(7).subscribe();

    const req = httpMock.expectOne(`${API}/api/geofences/custom/7`);
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
});
