import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ConfigService } from './config.service';
import { Nest, NestCreate, NestUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class NestService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  getAll(): Observable<Nest[]> {
    return this.http.get<Nest[]>(`${this.config.apiHost}/api/nests`);
  }

  create(nest: NestCreate): Observable<Nest> {
    return this.http.post<Nest>(`${this.config.apiHost}/api/nests`, nest);
  }

  update(uid: number, nest: NestUpdate): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/nests/${uid}`, nest);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/nests/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/nests`);
  }
}
