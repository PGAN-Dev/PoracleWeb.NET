import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';
import { Gym, GymCreate, GymUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class GymService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  create(gym: GymCreate): Observable<Gym> {
    return this.http.post<Gym>(`${this.config.apiHost}/api/gyms`, gym);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/gyms/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/gyms`);
  }

  getAll(): Observable<Gym[]> {
    return this.http.get<Gym[]>(`${this.config.apiHost}/api/gyms`);
  }

  update(uid: number, gym: GymUpdate): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/gyms/${uid}`, gym);
  }

  updateAllDistance(distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/gyms/distance`, distance);
  }

  updateBulkDistance(uids: number[], distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/gyms/distance/bulk`, {
      uids,
      distance,
    });
  }
}
