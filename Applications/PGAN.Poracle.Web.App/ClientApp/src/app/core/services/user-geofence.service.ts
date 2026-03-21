import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';
import { GeofenceRegion, UserGeofence, UserGeofenceCreate } from '../models';

@Injectable({ providedIn: 'root' })
export class UserGeofenceService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  createGeofence(data: UserGeofenceCreate): Observable<UserGeofence> {
    return this.http.post<UserGeofence>(`${this.config.apiHost}/api/geofences/custom`, data);
  }

  deleteGeofence(id: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/geofences/custom/${id}`);
  }

  getCustomGeofences(): Observable<UserGeofence[]> {
    return this.http.get<UserGeofence[]>(`${this.config.apiHost}/api/geofences/custom`);
  }

  getRegions(): Observable<GeofenceRegion[]> {
    return this.http.get<GeofenceRegion[]>(`${this.config.apiHost}/api/geofences/regions`);
  }

  submitForReview(kojiName: string): Observable<UserGeofence> {
    return this.http.post<UserGeofence>(`${this.config.apiHost}/api/geofences/custom/${encodeURIComponent(kojiName)}/submit`, {});
  }
}
