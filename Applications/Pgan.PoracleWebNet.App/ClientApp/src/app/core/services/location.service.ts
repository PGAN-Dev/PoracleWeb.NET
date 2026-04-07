import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';

import { ConfigService } from './config.service';
import { Location, GeocodingResult, ReverseGeocodingResult, WeatherData } from '../models';

@Injectable({ providedIn: 'root' })
export class LocationService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  geocode(query: string): Observable<GeocodingResult[]> {
    if (!query || query.trim().length === 0) return of([]);
    return this.http
      .get<GeocodingResult[]>(`${this.config.apiHost}/api/location/geocode?q=${encodeURIComponent(query)}`)
      .pipe(catchError(() => of([])));
  }

  getDistanceMapUrl(lat: number, lon: number, distance: number): Observable<{ url: string } | null> {
    return this.http
      .get<{ url: string }>(`${this.config.apiHost}/api/location/distancemap?lat=${lat}&lon=${lon}&distance=${distance}`)
      .pipe(catchError(() => of(null)));
  }

  getLocation(): Observable<Location> {
    return this.http.get<Location>(`${this.config.apiHost}/api/location`);
  }

  getStaticMapUrl(lat: number, lon: number): Observable<{ url: string } | null> {
    return this.http
      .get<{ url: string }>(`${this.config.apiHost}/api/location/staticmap?lat=${lat}&lon=${lon}`)
      .pipe(catchError(() => of(null)));
  }

  getWeather(): Observable<WeatherData | null> {
    return this.http.get<WeatherData>(`${this.config.apiHost}/api/location/weather`).pipe(catchError(() => of(null)));
  }

  reverseGeocode(lat: number, lon: number): Observable<ReverseGeocodingResult | null> {
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
