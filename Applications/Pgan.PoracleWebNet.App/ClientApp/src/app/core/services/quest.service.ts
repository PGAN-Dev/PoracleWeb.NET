import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, of } from 'rxjs';

import { ConfigService } from './config.service';
import { Quest, QuestCreate, QuestUpdate } from '../models';

interface QuestCapabilityResponse {
  pokecoins: boolean;
}

@Injectable({ providedIn: 'root' })
export class QuestService {
  private capabilityLoaded = false;
  private readonly config = inject(ConfigService);

  private readonly http = inject(HttpClient);

  /**
   * Whether this deployment's PoracleNG accepts pokecoin quest rewards (`reward_type: 8`).
   *
   * False until the server says otherwise, and false again on any fault: an old PoracleNG answers the
   * write with 400 "Unrecognised reward_type value", so a tab offered optimistically would be a control
   * nobody could use. Presentation only -- the API refuses the write independently.
   */
  readonly pokecoinsSupported = signal(false);

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

  /** Reads the capability once per session. Cheap, and the answer only changes on a server upgrade. */
  loadPokecoinCapability(): void {
    if (this.capabilityLoaded) return;
    this.capabilityLoaded = true;

    this.http
      .get<QuestCapabilityResponse>(`${this.config.apiHost}/api/quests/capability`)
      .pipe(catchError(() => of({ pokecoins: false })))
      .subscribe(res => this.pokecoinsSupported.set(res.pokecoins));
  }

  update(uid: number, quest: QuestUpdate): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/quests/${uid}`, quest);
  }

  updateAllDistance(distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/quests/distance`, distance);
  }

  updateBulkDistance(uids: number[], distance: number): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/quests/distance/bulk`, {
      uids,
      distance,
    });
  }
}
