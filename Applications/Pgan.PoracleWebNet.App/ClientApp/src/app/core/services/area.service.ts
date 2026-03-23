import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, of, map, catchError } from 'rxjs';

import { ConfigService } from './config.service';
import { AreaDefinition, GeofenceData } from '../models';

@Injectable({ providedIn: 'root' })
export class AreaService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  private mapUrlCache = new Map<string, string>();

  getAvailable(): Observable<AreaDefinition[]> {
    return this.http.get<AreaDefinition[]>(`${this.config.apiHost}/api/areas/available`);
  }

  getGeofencePolygons(): Observable<GeofenceData[]> {
    return this.http.get<{ status: string; geofence: GeofenceData[] }>(`${this.config.apiHost}/api/areas/geofence`).pipe(
      map(res => res.geofence || []),
      catchError(() => of([])),
    );
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

  getProfileAreas(): Observable<string[]> {
    return this.http.get<string[]>(`${this.config.apiHost}/api/areas/profile`);
  }

  getSelected(): Observable<string[]> {
    return this.http.get<string[]>(`${this.config.apiHost}/api/areas`);
  }

  update(areas: string[]): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/areas`, { areas });
  }
}
