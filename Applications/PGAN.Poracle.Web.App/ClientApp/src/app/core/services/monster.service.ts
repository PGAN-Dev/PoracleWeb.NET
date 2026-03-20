import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';
import { Monster, MonsterCreate, MonsterUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class MonsterService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  create(monster: MonsterCreate): Observable<Monster> {
    return this.http.post<Monster>(`${this.config.apiHost}/api/monsters`, monster);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/monsters/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/monsters`);
  }

  getAll(): Observable<Monster[]> {
    return this.http.get<Monster[]>(`${this.config.apiHost}/api/monsters`);
  }

  update(uid: number, monster: MonsterUpdate): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/monsters/${uid}`, monster);
  }

  updateAllDistance(distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/monsters/distance`, { distance });
  }

  updateBulkDistance(uids: number[], distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/monsters/distance/bulk`, {
      uids,
      distance,
    });
  }
}
