import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';
import { GeoJsonImportResult, GeofenceRegion, UserGeofence, UserGeofenceCreate } from '../models';

@Injectable({ providedIn: 'root' })
export class UserGeofenceService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  activateGeofence(id: number): Observable<void> {
    return this.http.post<void>(`${this.config.apiHost}/api/geofences/custom/${id}/activate`, {});
  }

  createGeofence(data: UserGeofenceCreate): Observable<UserGeofence> {
    return this.http.post<UserGeofence>(`${this.config.apiHost}/api/geofences/custom`, data);
  }

  deactivateGeofence(id: number): Observable<void> {
    return this.http.post<void>(`${this.config.apiHost}/api/geofences/custom/${id}/deactivate`, {});
  }

  deleteGeofence(id: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/geofences/custom/${id}`);
  }

  exportGeoJson(): Observable<Blob> {
    return this.http.get(`${this.config.apiHost}/api/geofences/export/geojson`, {
      responseType: 'blob',
    });
  }

  getCustomGeofences(): Observable<UserGeofence[]> {
    return this.http.get<UserGeofence[]>(`${this.config.apiHost}/api/geofences/custom`);
  }

  getRegions(): Observable<GeofenceRegion[]> {
    return this.http.get<GeofenceRegion[]>(`${this.config.apiHost}/api/geofences/regions`);
  }

  importGeoJson(file: File): Observable<GeoJsonImportResult> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<GeoJsonImportResult>(`${this.config.apiHost}/api/geofences/import/geojson`, formData);
  }

  submitForReview(kojiName: string): Observable<UserGeofence> {
    return this.http.post<UserGeofence>(`${this.config.apiHost}/api/geofences/custom/${encodeURIComponent(kojiName)}/submit`, {});
  }
}
