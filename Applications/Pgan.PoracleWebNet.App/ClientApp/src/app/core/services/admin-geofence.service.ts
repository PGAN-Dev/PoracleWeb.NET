import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';
import { GeofenceAlarmsChoice, GeofenceDeleteImpact, UserGeofence } from '../models';

@Injectable({ providedIn: 'root' })
export class AdminGeofenceService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  /** `alarms` decides what happens to the owner's alarms limited to this fence alone; the server's default is `delete`. */
  adminDelete(id: number, alarms?: GeofenceAlarmsChoice): Observable<void> {
    const params = alarms ? new HttpParams().set('alarms', alarms) : undefined;
    return this.http.delete<void>(`${this.config.apiHost}/api/admin/geofences/${id}`, { params });
  }

  approveSubmission(id: number, data: { groupName?: string; parentId?: number; promotedName?: string }): Observable<UserGeofence> {
    return this.http.post<UserGeofence>(`${this.config.apiHost}/api/admin/geofences/submissions/${id}/approve`, data);
  }

  getAll(): Observable<UserGeofence[]> {
    return this.http.get<UserGeofence[]>(`${this.config.apiHost}/api/admin/geofences/all`);
  }

  /** How many of the owner's alarms would widen if this fence were deleted. Writes nothing. */
  getDeleteImpact(id: number): Observable<GeofenceDeleteImpact> {
    return this.http.get<GeofenceDeleteImpact>(`${this.config.apiHost}/api/admin/geofences/${id}/delete-impact`);
  }

  getSubmissions(): Observable<UserGeofence[]> {
    return this.http.get<UserGeofence[]>(`${this.config.apiHost}/api/admin/geofences/submissions`);
  }

  rejectSubmission(id: number, data: { reviewNotes: string }): Observable<UserGeofence> {
    return this.http.post<UserGeofence>(`${this.config.apiHost}/api/admin/geofences/submissions/${id}/reject`, data);
  }
}
