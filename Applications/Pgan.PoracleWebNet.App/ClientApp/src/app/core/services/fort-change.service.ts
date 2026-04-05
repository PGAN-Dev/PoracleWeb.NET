import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';
import { FortChange, FortChangeCreate, FortChangeUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class FortChangeService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  create(fortChange: FortChangeCreate): Observable<FortChange> {
    return this.http.post<FortChange>(`${this.config.apiHost}/api/fort-changes`, fortChange);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/fort-changes/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/fort-changes`);
  }

  getAll(): Observable<FortChange[]> {
    return this.http.get<FortChange[]>(`${this.config.apiHost}/api/fort-changes`);
  }

  update(uid: number, fortChange: FortChangeUpdate): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/fort-changes/${uid}`, fortChange);
  }

  updateAllDistance(distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/fort-changes/distance`, distance);
  }

  updateBulkDistance(uids: number[], distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/fort-changes/distance/bulk`, {
      uids,
      distance,
    });
  }
}
