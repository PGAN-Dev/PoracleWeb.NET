import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ConfigService } from './config.service';
import { Monster, MonsterCreate, MonsterUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class MonsterService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  getAll(): Observable<Monster[]> {
    return this.http.get<Monster[]>(`${this.config.apiHost}/api/monsters`);
  }

  create(monster: MonsterCreate): Observable<Monster> {
    return this.http.post<Monster>(`${this.config.apiHost}/api/monsters`, monster);
  }

  update(uid: number, monster: MonsterUpdate): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/monsters/${uid}`, monster);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/monsters/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/monsters`);
  }

  updateAllDistance(distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/monsters/distance`, { distance });
  }
}
