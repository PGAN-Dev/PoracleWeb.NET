import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { AlarmMoveService, MoveEdit } from './alarm-move.service';
import { ConfigService } from './config.service';
import type { BatchCreateResponse } from '../../shared/utils/add-outcome';
import { DistanceUpdateResult } from '../../shared/utils/distance-update';
import { Quest, QuestCreate, QuestUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class QuestService {
  private readonly config = inject(ConfigService);

  private readonly http = inject(HttpClient);
  private readonly moves = inject(AlarmMoveService);

  create(quest: QuestCreate): Observable<Quest> {
    return this.http.post<Quest>(`${this.config.apiHost}/api/quests`, quest);
  }

  /** Several alarms in one request: one dry run and one commit upstream instead of a create each (#1090). */
  createBatch(quests: QuestCreate[]): Observable<BatchCreateResponse<Quest>> {
    return this.http.post<BatchCreateResponse<Quest>>(`${this.config.apiHost}/api/quests/batch`, quests);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/quests/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/quests`);
  }

  getAll(): Observable<Quest[]> {
    return this.http.get<Quest[]>(`${this.config.apiHost}/api/quests`);
  }

  update(uid: number, quest: QuestUpdate): Observable<MoveEdit | void> {
    return this.moves.put('quests', uid, quest, () => this.http.put<void>(`${this.config.apiHost}/api/quests/${uid}`, quest));
  }

  updateBulkDistance(uids: number[], distance: number): Observable<DistanceUpdateResult> {
    return this.http.put<DistanceUpdateResult>(`${this.config.apiHost}/api/quests/distance/bulk`, {
      uids,
      distance,
    });
  }
}
