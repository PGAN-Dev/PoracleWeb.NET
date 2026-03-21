import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';
import { UserGeofence } from '../models';

@Injectable({ providedIn: 'root' })
export class AdminGeofenceService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  adminDelete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/admin/geofences/${id}`);
  }

  approveSubmission(id: number, data: { promotedName?: string }): Observable<UserGeofence> {
    return this.http.post<UserGeofence>(`${this.config.apiHost}/api/admin/geofences/submissions/${id}/approve`, data);
  }

  getAll(): Observable<UserGeofence[]> {
    return this.http.get<UserGeofence[]>(`${this.config.apiHost}/api/admin/geofences/all`);
  }

  getSubmissions(): Observable<UserGeofence[]> {
    return this.http.get<UserGeofence[]>(`${this.config.apiHost}/api/admin/geofences/submissions`);
  }

  rejectSubmission(id: number, data: { reviewNotes: string }): Observable<UserGeofence> {
    return this.http.post<UserGeofence>(`${this.config.apiHost}/api/admin/geofences/submissions/${id}/reject`, data);
  }
}
