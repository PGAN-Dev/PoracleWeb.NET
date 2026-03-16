import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ConfigService } from './config.service';
import { Raid, RaidCreate, RaidUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class RaidService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  getAll(): Observable<Raid[]> {
    return this.http.get<Raid[]>(`${this.config.apiHost}/api/raids`);
  }

  create(raid: RaidCreate): Observable<Raid> {
    return this.http.post<Raid>(`${this.config.apiHost}/api/raids`, raid);
  }

  update(uid: number, raid: RaidUpdate): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/raids/${uid}`, raid);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/raids/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/raids`);
  }
}
