import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { AlarmMoveService, MoveEdit } from './alarm-move.service';
import { ConfigService } from './config.service';
import type { BatchCreateResponse } from '../../shared/utils/add-outcome';
import { DistanceUpdateResult } from '../../shared/utils/distance-update';
import { Nest, NestCreate, NestUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class NestService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);
  private readonly moves = inject(AlarmMoveService);

  create(nest: NestCreate): Observable<Nest> {
    return this.http.post<Nest>(`${this.config.apiHost}/api/nests`, nest);
  }

  /** Several alarms in one request: one dry run and one commit upstream instead of a create each (#1090). */
  createBatch(nests: NestCreate[]): Observable<BatchCreateResponse<Nest>> {
    return this.http.post<BatchCreateResponse<Nest>>(`${this.config.apiHost}/api/nests/batch`, nests);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/nests/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/nests`);
  }

  getAll(): Observable<Nest[]> {
    return this.http.get<Nest[]>(`${this.config.apiHost}/api/nests`);
  }

  update(uid: number, nest: NestUpdate): Observable<MoveEdit | void> {
    return this.moves.put('nests', uid, nest, () => this.http.put<void>(`${this.config.apiHost}/api/nests/${uid}`, nest));
  }

  updateBulkDistance(uids: number[], distance: number): Observable<DistanceUpdateResult> {
    return this.http.put<DistanceUpdateResult>(`${this.config.apiHost}/api/nests/distance/bulk`, {
      uids,
      distance,
    });
  }
}
