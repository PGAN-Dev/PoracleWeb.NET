import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';
import { DistanceUpdateResult } from '../../shared/utils/distance-update';
import { Quest, QuestCreate, QuestUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class QuestService {
  private readonly config = inject(ConfigService);

  private readonly http = inject(HttpClient);

  create(quest: QuestCreate): Observable<Quest> {
    return this.http.post<Quest>(`${this.config.apiHost}/api/quests`, quest);
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

  update(uid: number, quest: QuestUpdate): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/quests/${uid}`, quest);
  }

  updateBulkDistance(uids: number[], distance: number): Observable<DistanceUpdateResult> {
    return this.http.put<DistanceUpdateResult>(`${this.config.apiHost}/api/quests/distance/bulk`, {
      uids,
      distance,
    });
  }
}
