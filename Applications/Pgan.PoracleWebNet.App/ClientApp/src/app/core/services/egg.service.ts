import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';
import { Egg, EggCreate, EggUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class EggService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  create(egg: EggCreate): Observable<Egg> {
    return this.http.post<Egg>(`${this.config.apiHost}/api/eggs`, egg);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/eggs/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/eggs`);
  }

  getAll(): Observable<Egg[]> {
    return this.http.get<Egg[]>(`${this.config.apiHost}/api/eggs`);
  }

  update(uid: number, egg: EggUpdate): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/eggs/${uid}`, egg);
  }

  updateAllDistance(distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/eggs/distance`, distance);
  }

  updateBulkDistance(uids: number[], distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/eggs/distance/bulk`, {
      uids,
      distance,
    });
  }
}
