import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, of, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';

import { ConfigService } from './config.service';
import { SettingsService } from './settings.service';
import { AreaWeatherResult, Location, GeocodingResult, ReverseGeocodingResult, WeatherData } from '../models';

/**
 * What a gated read fails with instead of a request. Every caller of {@link LocationService.getLocation}
 * and `PlacesService.load` already has an error arm for the 403 this used to be, so an error keeps them
 * on the path they already handle -- without the 403 reaching the error interceptor, which toasts it
 * before any caller's catchError can swallow it.
 */
export class LocationFeatureDisabledError extends Error {
  constructor(readonly disableKey: string) {
    super(`${disableKey} is on`);
  }
}

@Injectable({ providedIn: 'root' })
export class LocationService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);
  private readonly settings = inject(SettingsService);

  geocode(query: string): Observable<GeocodingResult[]> {
    if (!query || query.trim().length === 0 || this.geocodingDisabled()) return of([]);
    return this.http
      .get<GeocodingResult[]>(`${this.config.apiHost}/api/location/geocode?q=${encodeURIComponent(query)}`)
      .pipe(catchError(() => of([])));
  }

  /**
   * True when address search and reverse lookups are off. `disable_location` counts too, because it
   * gates the whole of LocationController, the geocode actions included.
   */
  geocodingDisabled(): boolean {
    return this.locationDisabled() || this.settings.isDisabled('disable_nominatim');
  }

  getAreaWeather(locations: { name: string; lat: number; lon: number }[]): Observable<AreaWeatherResult[]> {
    if (locations.length === 0 || this.locationDisabled()) return of([]);
    return this.http
      .post<AreaWeatherResult[]>(`${this.config.apiHost}/api/location/weather/areas`, { locations })
      .pipe(catchError(() => of([])));
  }

  getDistanceMapUrl(lat: number, lon: number, distance: number): Observable<{ url: string } | null> {
    if (this.locationDisabled()) return of(null);
    return this.http
      .get<{ url: string }>(`${this.config.apiHost}/api/location/distancemap?lat=${lat}&lon=${lon}&distance=${distance}`)
      .pipe(catchError(() => of(null)));
  }

  /** Not behind `disable_location`: the alert language has its own controller. */
  getLanguage(): Observable<{ language: string | null }> {
    return this.http
      .get<{ language: string | null }>(`${this.config.apiHost}/api/location/language`)
      .pipe(catchError(() => of({ language: null })));
  }

  getLocation(): Observable<Location> {
    if (this.locationDisabled()) return throwError(() => new LocationFeatureDisabledError('disable_location'));
    return this.http.get<Location>(`${this.config.apiHost}/api/location`);
  }

  getStaticMapUrl(lat: number, lon: number): Observable<{ url: string } | null> {
    if (this.locationDisabled()) return of(null);
    return this.http
      .get<{ url: string }>(`${this.config.apiHost}/api/location/staticmap?lat=${lat}&lon=${lon}`)
      .pipe(catchError(() => of(null)));
  }

  getWeather(): Observable<WeatherData | null> {
    if (this.locationDisabled()) return of(null);
    return this.http.get<WeatherData>(`${this.config.apiHost}/api/location/weather`).pipe(catchError(() => of(null)));
  }

  /** True when `disable_location` is on, which 403s every route on LocationController. */
  locationDisabled(): boolean {
    return this.settings.isDisabled('disable_location');
  }

  reverseGeocode(lat: number, lon: number): Observable<ReverseGeocodingResult | null> {
    if (this.geocodingDisabled()) return of(null);
    return this.http
      .get<ReverseGeocodingResult>(`${this.config.apiHost}/api/location/reverse?lat=${lat}&lon=${lon}`)
      .pipe(catchError(() => of(null)));
  }

  setLanguage(locale: string): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/location/language`, { language: locale });
  }

  setLocation(location: Location): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/location`, location);
  }
}
