import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, of, map, catchError, shareReplay } from 'rxjs';

import { ConfigService } from './config.service';
import { AreaDefinition, GeofenceData } from '../models';

@Injectable({ providedIn: 'root' })
export class AreaService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  private mapUrlCache = new Map<string, string>();

  /** The shared polygon read and when it was made, so every dialog opened within a few minutes reuses it. */
  private polygons: null | { at: number; request: Observable<GeofenceData[]> } = null;

  getAvailable(): Observable<AreaDefinition[]> {
    return this.http.get<AreaDefinition[]>(`${this.config.apiHost}/api/areas/available`);
  }

  getGeofencePolygons(): Observable<GeofenceData[]> {
    return this.http.get<{ status: string; geofence: GeofenceData[] }>(`${this.config.apiHost}/api/areas/geofence`).pipe(
      map(res => res.geofence || []),
      catchError(() => of([])),
    );
  }

  /**
   * The same polygons, read once and shared for five minutes. The Delivery tab's map in every alarm
   * dialog shades areas from this, and the feed can run to hundreds of fences, so opening ten dialogs
   * should not read it ten times. Five minutes because a fence drawn on another page should not stay
   * missing for the rest of the session.
   */
  getGeofencePolygonsShared(): Observable<GeofenceData[]> {
    const now = Date.now();
    if (!this.polygons || now - this.polygons.at > 5 * 60_000) {
      const request = this.http.get<{ status: string; geofence: GeofenceData[] }>(`${this.config.apiHost}/api/areas/geofence`).pipe(
        map(res => res.geofence || []),
        // A failed read isn't kept: the next dialog asks again rather than shading nothing for five minutes.
        catchError(() => {
          if (this.polygons?.request === request) this.polygons = null;
          return of<GeofenceData[]>([]);
        }),
        shareReplay(1),
      );
      this.polygons = { at: now, request };
    }
    return this.polygons.request;
  }

  getMapUrl(areaName: string): Observable<string | null> {
    const cached = this.mapUrlCache.get(areaName);
    if (cached) return of(cached);

    return this.http.get<{ url: string }>(`${this.config.apiHost}/api/areas/map/${encodeURIComponent(areaName)}`).pipe(
      map(res => {
        this.mapUrlCache.set(areaName, res.url);
        return res.url;
      }),
      catchError(() => of(null)),
    );
  }

  getSelected(): Observable<string[]> {
    return this.http.get<string[]>(`${this.config.apiHost}/api/areas`);
  }

  /**
   * Saves the profile's areas and answers with the list PoracleNG holds afterwards, read back after the write.
   * A name it has no fence for is dropped there, not refused, so the answer can be shorter than what was sent.
   */
  update(areas: string[]): Observable<string[]> {
    return this.http.put<string[]>(`${this.config.apiHost}/api/areas`, { areas });
  }
}
