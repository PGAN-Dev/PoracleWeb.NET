import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AreaService } from './area.service';
import { ConfigService } from './config.service';

describe('AreaService', () => {
  let service: AreaService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: API } }],
    });
    service = TestBed.inject(AreaService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should fetch available areas', () => {
    const areas = [
      { name: 'downtown', group: 'city', userSelectable: true },
      { name: 'suburbs', group: 'outer', userSelectable: false },
    ];

    service.getAvailable().subscribe(result => {
      expect(result).toHaveLength(2);
      expect(result[0].name).toBe('downtown');
      expect(result[1].userSelectable).toBe(false);
    });

    httpMock.expectOne(`${API}/api/areas/available`).flush(areas);
  });

  it('should fetch selected areas', () => {
    service.getSelected().subscribe(result => {
      expect(result).toEqual(['west end', 'downtown']);
    });

    httpMock.expectOne(`${API}/api/areas`).flush(['west end', 'downtown']);
  });

  it('should update selected areas', () => {
    service.update(['west end']).subscribe();

    const req = httpMock.expectOne(`${API}/api/areas`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ areas: ['west end'] });
    req.flush(null);
  });

  describe('getGeofencePolygons', () => {
    it('should extract geofence array from response', () => {
      const geofences = [
        {
          id: 1,
          name: 'Area1',
          path: [
            [1, 2],
            [3, 4],
          ] as [number, number][],
        },
      ];

      service.getGeofencePolygons().subscribe(result => {
        expect(result).toHaveLength(1);
        expect(result[0].name).toBe('Area1');
      });

      httpMock.expectOne(`${API}/api/areas/geofence`).flush({
        geofence: geofences,
        status: 'ok',
      });
    });

    it('should return empty array when geofence is missing in response', () => {
      service.getGeofencePolygons().subscribe(result => {
        expect(result).toEqual([]);
      });

      httpMock.expectOne(`${API}/api/areas/geofence`).flush({
        status: 'ok',
      });
    });

    it('should return empty array on HTTP error', () => {
      service.getGeofencePolygons().subscribe(result => {
        expect(result).toEqual([]);
      });

      httpMock.expectOne(`${API}/api/areas/geofence`).flush(null, {
        status: 500,
        statusText: 'Error',
      });
    });
  });

  describe('getMapUrl', () => {
    it('should fetch map URL and cache it', () => {
      service.getMapUrl('downtown').subscribe(url => {
        expect(url).toBe('https://maps.example.com/downtown.png');
      });

      httpMock.expectOne(`${API}/api/areas/map/downtown`).flush({
        url: 'https://maps.example.com/downtown.png',
      });

      // Second call should use cache - no HTTP request
      service.getMapUrl('downtown').subscribe(url => {
        expect(url).toBe('https://maps.example.com/downtown.png');
      });

      httpMock.expectNone(`${API}/api/areas/map/downtown`);
    });

    it('should return null on error', () => {
      service.getMapUrl('unknown').subscribe(url => {
        expect(url).toBeNull();
      });

      httpMock.expectOne(`${API}/api/areas/map/unknown`).flush(null, {
        status: 404,
        statusText: 'Not Found',
      });
    });

    it('should encode area names with special characters', () => {
      service.getMapUrl('west end').subscribe();

      httpMock.expectOne(`${API}/api/areas/map/west%20end`).flush({ url: 'x' });
    });
  });
});
