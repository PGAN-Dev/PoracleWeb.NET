import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ConfigService } from './config.service';
import { Gym, GymCreate, GymUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class GymService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  getAll(): Observable<Gym[]> {
    return this.http.get<Gym[]>(`${this.config.apiHost}/api/gyms`);
  }

  create(gym: GymCreate): Observable<Gym> {
    return this.http.post<Gym>(`${this.config.apiHost}/api/gyms`, gym);
  }

  update(uid: number, gym: GymUpdate): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/gyms/${uid}`, gym);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/gyms/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/gyms`);
  }
}
