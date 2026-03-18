import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ConfigService } from './config.service';
import { Lure, LureCreate, LureUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class LureService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  getAll(): Observable<Lure[]> {
    return this.http.get<Lure[]>(`${this.config.apiHost}/api/lures`);
  }

  create(lure: LureCreate): Observable<Lure> {
    return this.http.post<Lure>(`${this.config.apiHost}/api/lures`, lure);
  }

  update(uid: number, lure: LureUpdate): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/lures/${uid}`, lure);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/lures/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/lures`);
  }

  updateAllDistance(distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/lures/distance`, distance);
  }
}
