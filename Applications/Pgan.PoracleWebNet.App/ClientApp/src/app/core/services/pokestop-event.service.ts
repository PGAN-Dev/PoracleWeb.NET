import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';
import { DistanceUpdateResult } from '../../shared/utils/distance-update';
import { PokestopEvent, PokestopEventCreate, PokestopEventUpdate } from '../models';

/**
 * Showcase, Kecleon and Gold Stop alarms.
 *
 * Every successful write re-keys the row upstream, so callers must reload rather than patch a list
 * in place: a uid held across a save is stale immediately.
 */
@Injectable({ providedIn: 'root' })
export class PokestopEventService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  create(event: PokestopEventCreate): Observable<PokestopEvent> {
    return this.http.post<PokestopEvent>(`${this.config.apiHost}/api/pokestop-events`, event);
  }

  delete(uid: number): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/pokestop-events/${uid}`);
  }

  deleteAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/pokestop-events`);
  }

  getAll(): Observable<PokestopEvent[]> {
    return this.http.get<PokestopEvent[]>(`${this.config.apiHost}/api/pokestop-events`);
  }

  update(uid: number, event: PokestopEventUpdate): Observable<void> {
    return this.http.put<void>(`${this.config.apiHost}/api/pokestop-events/${uid}`, event);
  }

  updateBulkDistance(uids: number[], distance: number): Observable<DistanceUpdateResult> {
    return this.http.put<DistanceUpdateResult>(`${this.config.apiHost}/api/pokestop-events/distance/bulk`, {
      uids,
      distance,
    });
  }
}
