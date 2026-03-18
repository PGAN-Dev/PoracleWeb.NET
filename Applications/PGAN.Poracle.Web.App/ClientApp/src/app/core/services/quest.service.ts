import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ConfigService } from './config.service';
import { Quest, QuestCreate, QuestUpdate } from '../models';

@Injectable({ providedIn: 'root' })
export class QuestService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  getAll(): Observable<Quest[]> {
    return this.http.get<Quest[]>(`${this.config.apiHost}/api/quests`);
  }

  create(quest: QuestCreate): Observable<Quest> {
    return this.http.post<Quest>(`${this.config.apiHost}/api/quests`, quest);
  }

  update(uid: number, quest: QuestUpdate): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/quests/${uid}`, quest);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/quests/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/quests`);
  }

  updateAllDistance(distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/quests/distance`, distance);
  }
}
