import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ConfigService } from './config.service';
import { Egg, EggCreate, EggUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class EggService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  getAll(): Observable<Egg[]> {
    return this.http.get<Egg[]>(`${this.config.apiHost}/api/eggs`);
  }

  create(egg: EggCreate): Observable<Egg> {
    return this.http.post<Egg>(`${this.config.apiHost}/api/eggs`, egg);
  }

  update(uid: number, egg: EggUpdate): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/eggs/${uid}`, egg);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/eggs/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/eggs`);
  }

  updateAllDistance(distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/eggs/distance`, distance);
  }
}
