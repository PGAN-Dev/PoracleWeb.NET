import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';
import { Invasion, InvasionCreate, InvasionUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class InvasionService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  create(invasion: InvasionCreate): Observable<Invasion> {
    return this.http.post<Invasion>(`${this.config.apiHost}/api/invasions`, invasion);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/invasions/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/invasions`);
  }

  getAll(): Observable<Invasion[]> {
    return this.http.get<Invasion[]>(`${this.config.apiHost}/api/invasions`);
  }

  update(uid: number, invasion: InvasionUpdate): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/invasions/${uid}`, invasion);
  }

  updateAllDistance(distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/invasions/distance`, distance);
  }

  updateBulkDistance(uids: number[], distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/invasions/distance/bulk`, {
      uids,
      distance,
    });
  }
}
