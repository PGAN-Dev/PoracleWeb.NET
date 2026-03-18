import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { LocationService } from './location.service';

describe('LocationService', () => {
  let service: LocationService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfigService, useValue: { apiHost: API } }],
    });
    service = TestBed.inject(LocationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  describe('geocode', () => {
    it('should return empty array for empty query', () => {
      service.geocode('').subscribe(results => {
        expect(results).toEqual([]);
      });
      httpMock.expectNone(`${API}/api/location/geocode`);
    });

    it('should return empty array for whitespace-only query', () => {
      service.geocode('   ').subscribe(results => {
        expect(results).toEqual([]);
      });
      httpMock.expectNone(`${API}/api/location/geocode`);
    });

    it('should fetch geocoding results for valid query', () => {
      service.geocode('Main Street').subscribe(results => {
        expect(results).toHaveLength(1);
        expect(results[0].display_name).toBe('Main Street, City');
      });

      const req = httpMock.expectOne(`${API}/api/location/geocode?q=Main%20Street`);
      expect(req.request.method).toBe('GET');
      req.flush([{ display_name: 'Main Street, City', lat: '40.7', lon: '-74.0' }]);
    });

    it('should return empty array on error', () => {
      service.geocode('test').subscribe(results => {
        expect(results).toEqual([]);
      });

      httpMock.expectOne(`${API}/api/location/geocode?q=test`).flush(null, {
        status: 500,
        statusText: 'Error',
      });
    });
  });

  describe('getLocation', () => {
    it('should fetch the current user location', () => {
      service.getLocation().subscribe(loc => {
        expect(loc.latitude).toBe(40.7128);
        expect(loc.longitude).toBe(-74.006);
      });

      httpMock.expectOne(`${API}/api/location`).flush({
        latitude: 40.7128,
        longitude: -74.006,
      });
    });
  });

  describe('setLocation', () => {
    it('should PUT the new location', () => {
      service.setLocation({ latitude: 51.5, longitude: -0.12 }).subscribe();

      const req = httpMock.expectOne(`${API}/api/location`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ latitude: 51.5, longitude: -0.12 });
      req.flush(null);
    });
  });

  describe('setLanguage', () => {
    it('should PUT the language', () => {
      service.setLanguage('de').subscribe();

      const req = httpMock.expectOne(`${API}/api/location/language`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ language: 'de' });
      req.flush(null);
    });
  });

  describe('reverseGeocode', () => {
    it('should fetch reverse geocoding result', () => {
      service.reverseGeocode(40.7, -74.0).subscribe(result => {
        expect(result?.display_name).toBe('123 Main St');
      });

      httpMock.expectOne(`${API}/api/location/reverse?lat=40.7&lon=-74`).flush({
        display_name: '123 Main St',
      });
    });

    it('should return null on error', () => {
      service.reverseGeocode(0, 0).subscribe(result => {
        expect(result).toBeNull();
      });

      httpMock.expectOne(`${API}/api/location/reverse?lat=0&lon=0`).flush(null, {
        status: 500,
        statusText: 'Error',
      });
    });
  });

  describe('getStaticMapUrl', () => {
    it('should return map URL', () => {
      service.getStaticMapUrl(40.7, -74.0).subscribe(result => {
        expect(result?.url).toBe('https://maps.example.com/static.png');
      });

      httpMock.expectOne(`${API}/api/location/staticmap?lat=40.7&lon=-74`).flush({
        url: 'https://maps.example.com/static.png',
      });
    });

    it('should return null on error', () => {
      service.getStaticMapUrl(0, 0).subscribe(result => {
        expect(result).toBeNull();
      });

      httpMock.expectOne(`${API}/api/location/staticmap?lat=0&lon=0`).flush(null, {
        status: 500,
        statusText: 'Error',
      });
    });
  });

  describe('getDistanceMapUrl', () => {
    it('should return distance map URL', () => {
      service.getDistanceMapUrl(40.7, -74.0, 5000).subscribe(result => {
        expect(result?.url).toBe('https://maps.example.com/distance.png');
      });

      httpMock
        .expectOne(`${API}/api/location/distancemap?lat=40.7&lon=-74&distance=5000`)
        .flush({ url: 'https://maps.example.com/distance.png' });
    });

    it('should return null on error', () => {
      service.getDistanceMapUrl(0, 0, 1000).subscribe(result => {
        expect(result).toBeNull();
      });

      httpMock.expectOne(`${API}/api/location/distancemap?lat=0&lon=0&distance=1000`).flush(null, { status: 500, statusText: 'Error' });
    });
  });
});
