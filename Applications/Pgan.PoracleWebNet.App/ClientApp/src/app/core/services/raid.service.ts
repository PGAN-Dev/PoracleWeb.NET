import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';
import { Raid, RaidCreate, RaidUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class RaidService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  create(raid: RaidCreate): Observable<Raid> {
    return this.http.post<Raid>(`${this.config.apiHost}/api/raids`, raid);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/raids/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/raids`);
  }

  getAll(): Observable<Raid[]> {
    return this.http.get<Raid[]>(`${this.config.apiHost}/api/raids`);
  }

  update(uid: number, raid: RaidUpdate): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/raids/${uid}`, raid);
  }

  updateAllDistance(distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/raids/distance`, distance);
  }

  updateBulkDistance(uids: number[], distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/raids/distance/bulk`, {
      uids,
      distance,
    });
  }
}
