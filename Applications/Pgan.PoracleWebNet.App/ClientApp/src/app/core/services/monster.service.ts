import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, of } from 'rxjs';

import { AlarmMoveService, MoveEdit } from './alarm-move.service';
import { ConfigService } from './config.service';
import type { BatchCreateResponse } from '../../shared/utils/add-outcome';
import { DistanceUpdateResult } from '../../shared/utils/distance-update';
import { Monster, MonsterCreate, MonsterUpdate } from '../models';

interface MonsterCapabilityResponse {
  edit: boolean;
}

@Injectable({ providedIn: 'root' })
export class MonsterService {
  private capabilityLoaded = false;
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);
  private readonly moves = inject(AlarmMoveService);

  /**
   * Whether this deployment's PoracleNG honours the pokemon edit-in-place bit
   * (jfberry/PoracleNG#246, merged 2026-10-08, not yet in a tagged release).
   *
   * False until the server says otherwise, and false again on any fault: a server that predates #246
   * stores and echoes the bit but the processor never acts on it, so a toggle offered optimistically
   * would be a control nobody could use. Presentation only -- the capability doesn't change what gets
   * written, only whether the control is shown.
   */
  readonly editSupported = signal(false);

  create(monster: MonsterCreate): Observable<Monster> {
    return this.http.post<Monster>(`${this.config.apiHost}/api/monsters`, monster);
  }

  /** Several alarms in one request: one dry run and one commit upstream instead of a create each (#1090). */
  createBatch(monsters: MonsterCreate[]): Observable<BatchCreateResponse<Monster>> {
    return this.http.post<BatchCreateResponse<Monster>>(`${this.config.apiHost}/api/monsters/batch`, monsters);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/monsters/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/monsters`);
  }

  getAll(): Observable<Monster[]> {
    return this.http.get<Monster[]>(`${this.config.apiHost}/api/monsters`);
  }

  /** Reads the capability once per session. Cheap, and the answer only changes on a server upgrade. */
  loadEditCapability(): void {
    if (this.capabilityLoaded) return;
    this.capabilityLoaded = true;

    this.http
      .get<MonsterCapabilityResponse>(`${this.config.apiHost}/api/monsters/capability`)
      .pipe(catchError(() => of({ edit: false })))
      .subscribe(res => this.editSupported.set(res.edit));
  }

  update(uid: number, monster: MonsterUpdate): Observable<MoveEdit | void> {
    return this.moves.put('monsters', uid, monster, () => this.http.put<void>(`${this.config.apiHost}/api/monsters/${uid}`, monster));
  }

  updateBulkDistance(uids: number[], distance: number): Observable<DistanceUpdateResult> {
    return this.http.put<DistanceUpdateResult>(`${this.config.apiHost}/api/monsters/distance/bulk`, {
      uids,
      distance,
    });
  }
}
