import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';
import { Lure, LureCreate, LureUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class LureService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  create(lure: LureCreate): Observable<Lure> {
    return this.http.post<Lure>(`${this.config.apiHost}/api/lures`, lure);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/lures/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/lures`);
  }

  getAll(): Observable<Lure[]> {
    return this.http.get<Lure[]>(`${this.config.apiHost}/api/lures`);
  }

  update(uid: number, lure: LureUpdate): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/lures/${uid}`, lure);
  }

  updateAllDistance(distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/lures/distance`, distance);
  }

  updateBulkDistance(uids: number[], distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/lures/distance/bulk`, {
      uids,
      distance,
    });
  }
}
