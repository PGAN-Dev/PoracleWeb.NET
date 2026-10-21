import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';
import {
  GeoJsonImportResult,
  GeofenceAlarmsChoice,
  GeofenceDeleteImpact,
  GeofenceRegion,
  UserGeofence,
  UserGeofenceCreate,
} from '../models';

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

  /** `alarms` decides what happens to alarms limited to this fence alone; the server's default is `delete`. */
  deleteGeofence(id: number, alarms?: GeofenceAlarmsChoice): Observable<void> {
    const params = alarms ? new HttpParams().set('alarms', alarms) : undefined;
    return this.http.delete<void>(`${this.config.apiHost}/api/geofences/custom/${id}`, { params });
  }

  exportGeoJson(): Observable<Blob> {
    return this.http.get(`${this.config.apiHost}/api/geofences/export/geojson`, {
      responseType: 'blob',
    });
  }

  getCustomGeofences(): Observable<UserGeofence[]> {
    return this.http.get<UserGeofence[]>(`${this.config.apiHost}/api/geofences/custom`);
  }

  /** How many alarms would widen if this fence were deleted. Writes nothing. */
  getDeleteImpact(id: number): Observable<GeofenceDeleteImpact> {
    return this.http.get<GeofenceDeleteImpact>(`${this.config.apiHost}/api/geofences/custom/${id}/delete-impact`);
  }

  getRegions(): Observable<GeofenceRegion[]> {
    return this.http.get<GeofenceRegion[]>(`${this.config.apiHost}/api/geofences/regions`);
  }

  importGeoJson(file: File): Observable<GeoJsonImportResult> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<GeoJsonImportResult>(`${this.config.apiHost}/api/geofences/import/geojson`, formData);
  }

  /**
   * Renames a geofence in place. Editing used to be delete-then-recreate, and the recreate re-subscribed
   * only the active profile — silently switching the geofence off for every other profile. See #543.
   */
  renameGeofence(id: number, data: { displayName: string; groupName?: string | null; parentId?: number | null }): Observable<UserGeofence> {
    return this.http.put<UserGeofence>(`${this.config.apiHost}/api/geofences/custom/${id}`, data);
  }

  submitForReview(kojiName: string): Observable<UserGeofence> {
    return this.http.post<UserGeofence>(`${this.config.apiHost}/api/geofences/custom/${encodeURIComponent(kojiName)}/submit`, {});
  }
}
