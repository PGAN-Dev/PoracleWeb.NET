import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';
import { DistanceUpdateResult } from '../../shared/utils/distance-update';
import { Invasion, InvasionCreate, InvasionUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class InvasionService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  create(invasion: InvasionCreate): Observable<Invasion> {
    return this.http.post<Invasion>(`${this.config.apiHost}/api/invasions`, invasion);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/invasions/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/invasions`);
  }

  getAll(): Observable<Invasion[]> {
    return this.http.get<Invasion[]>(`${this.config.apiHost}/api/invasions`);
  }

  /**
   * `kind` is the kind of the card the person opened. Invasions and Pokéstop events are numbered separately, so the
   * server needs it to know which rule the uid names. Leave it out for an older server's behaviour. See #1062.
   */
  update(uid: number, invasion: InvasionUpdate, kind?: 'event' | 'invasion'): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/invasions/${uid}`, invasion, kind ? { params: { kind } } : undefined);
  }

  updateBulkDistance(uids: number[], distance: number): Observable<DistanceUpdateResult> {
    return this.http.put<DistanceUpdateResult>(`${this.config.apiHost}/api/invasions/distance/bulk`, {
      uids,
      distance,
    });
  }
}
