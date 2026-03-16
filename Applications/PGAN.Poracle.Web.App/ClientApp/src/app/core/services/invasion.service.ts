import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ConfigService } from './config.service';
import { Invasion, InvasionCreate, InvasionUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class InvasionService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  getAll(): Observable<Invasion[]> {
    return this.http.get<Invasion[]>(`${this.config.apiHost}/api/invasions`);
  }

  create(invasion: InvasionCreate): Observable<Invasion> {
    return this.http.post<Invasion>(`${this.config.apiHost}/api/invasions`, invasion);
  }

  update(uid: number, invasion: InvasionUpdate): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/invasions/${uid}`, invasion);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/invasions/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/invasions`);
  }
}
