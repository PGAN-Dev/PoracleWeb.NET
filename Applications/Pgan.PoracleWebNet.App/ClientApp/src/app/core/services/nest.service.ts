import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';
import { Nest, NestCreate, NestUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class NestService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  create(nest: NestCreate): Observable<Nest> {
    return this.http.post<Nest>(`${this.config.apiHost}/api/nests`, nest);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/nests/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/nests`);
  }

  getAll(): Observable<Nest[]> {
    return this.http.get<Nest[]>(`${this.config.apiHost}/api/nests`);
  }

  update(uid: number, nest: NestUpdate): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/nests/${uid}`, nest);
  }

  updateAllDistance(distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/nests/distance`, distance);
  }

  updateBulkDistance(uids: number[], distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/nests/distance/bulk`, {
      uids,
      distance,
    });
  }
}
