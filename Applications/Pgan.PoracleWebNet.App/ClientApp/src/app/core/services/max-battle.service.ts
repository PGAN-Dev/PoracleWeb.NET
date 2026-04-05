import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';
import { MaxBattle, MaxBattleCreate, MaxBattleUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class MaxBattleService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  create(maxBattle: MaxBattleCreate): Observable<MaxBattle> {
    return this.http.post<MaxBattle>(`${this.config.apiHost}/api/maxbattles`, maxBattle);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/maxbattles/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/maxbattles`);
  }

  getAll(): Observable<MaxBattle[]> {
    return this.http.get<MaxBattle[]>(`${this.config.apiHost}/api/maxbattles`);
  }

  update(uid: number, maxBattle: MaxBattleUpdate): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/maxbattles/${uid}`, maxBattle);
  }

  updateAllDistance(distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/maxbattles/distance`, distance);
  }

  updateBulkDistance(uids: number[], distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/maxbattles/distance/bulk`, {
      uids,
      distance,
    });
  }
}
