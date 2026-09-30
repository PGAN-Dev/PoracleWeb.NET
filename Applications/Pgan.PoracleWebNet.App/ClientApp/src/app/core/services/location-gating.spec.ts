import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { LocationService } from './location.service';
import { PlacesService } from './places.service';
import { SettingsService } from './settings.service';

const API = 'http://test-api';

/**
 * `disable_location` gates the whole of LocationController and `disable_nominatim` its two geocode
 * actions. A request the server will refuse still reaches the error interceptor, which toasts the 403
 * before any caller's catchError can swallow it -- so the dashboard and the Areas page opened with a red
 * toast on every load. The service is the one place every caller goes through, so it is where the
 * request is not made.
 */
describe('LocationService and PlacesService feature gating', () => {
  let httpMock: HttpTestingController;
  let location: LocationService;
  let places: PlacesService;
  const settings = signal<Record<string, string>>({});

  beforeEach(() => {
    settings.set({});
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: API } },
        { provide: SettingsService, useValue: { isDisabled: (key: string) => settings()[key] === 'true' } },
      ],
    });
    location = TestBed.inject(LocationService);
    places = TestBed.inject(PlacesService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  describe('with nothing disabled', () => {
    it('still reverse geocodes', () => {
      let address: string | undefined;
      location.reverseGeocode(1, 2).subscribe(r => (address = r?.display_name));
      httpMock.expectOne(`${API}/api/location/reverse?lat=1&lon=2`).flush({ display_name: 'Somewhere' });
      expect(address).toBe('Somewhere');
    });

    it('still geocodes, reads the pin and loads places', () => {
      location.geocode('main').subscribe();
      location.getLocation().subscribe();
      places.load().subscribe();
      httpMock.expectOne(`${API}/api/location/geocode?q=main`).flush([]);
      httpMock.expectOne(`${API}/api/location`).flush({ latitude: 1, longitude: 2 });
      httpMock.expectOne(`${API}/api/location/places`).flush({ named: [], default: null });
    });
  });

  describe('with disable_nominatim on', () => {
    beforeEach(() => settings.set({ disable_nominatim: 'true' }));

    it('answers a reverse geocode with null and sends nothing', () => {
      let result: unknown = 'unset';
      location.reverseGeocode(1, 2).subscribe(r => (result = r));
      httpMock.expectNone(() => true);
      expect(result).toBeNull();
    });

    it('answers a forward geocode with no results and sends nothing', () => {
      let result: unknown;
      location.geocode('main street').subscribe(r => (result = r));
      httpMock.expectNone(() => true);
      expect(result).toEqual([]);
    });

    it('leaves the rest of the location API alone', () => {
      location.getLocation().subscribe();
      location.getStaticMapUrl(1, 2).subscribe();
      httpMock.expectOne(`${API}/api/location`).flush({ latitude: 1, longitude: 2 });
      httpMock.expectOne(`${API}/api/location/staticmap?lat=1&lon=2`).flush({ url: 'x' });
    });
  });

  describe('with disable_location on', () => {
    beforeEach(() => settings.set({ disable_location: 'true' }));

    it('sends no location request of any kind', () => {
      location.reverseGeocode(1, 2).subscribe();
      location.geocode('main').subscribe();
      location.getStaticMapUrl(1, 2).subscribe();
      location.getDistanceMapUrl(1, 2, 3).subscribe();
      location.getWeather().subscribe();
      location.getAreaWeather([{ name: 'a', lat: 1, lon: 2 }]).subscribe();
      location.getLocation().subscribe({ error: () => undefined });
      places.load().subscribe({ error: () => undefined });
      httpMock.expectNone(() => true);
    });

    /** Every caller already has an error arm for the 403 this used to be, so an error is what it gets. */
    it('fails getLocation and places.load without a request', () => {
      const failed: string[] = [];
      location.getLocation().subscribe({ error: () => failed.push('location') });
      places.load().subscribe({ error: () => failed.push('places') });
      expect(failed).toEqual(['location', 'places']);
    });

    it('still reads the alert language, which is not behind the location gate', () => {
      location.getLanguage().subscribe();
      httpMock.expectOne(`${API}/api/location/language`).flush({ language: 'en' });
    });
  });
});
