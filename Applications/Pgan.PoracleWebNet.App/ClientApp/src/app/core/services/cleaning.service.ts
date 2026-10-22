import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';

export type CleanAlarmType =
  'eggs' | 'fortchanges' | 'gyms' | 'invasions' | 'lures' | 'maxbattles' | 'monsters' | 'nests' | 'quests' | 'raids';

/** How many of a type's alarms have auto-delete on, out of how many there are. */
export interface CleanCount {
  clean: number;
  total: number;
}

/**
 * One flag per type, on only when every alarm of it is clean, plus `counts`, which a server older than
 * this field doesn't send.
 */
export type CleanStatus = Record<string, boolean | Record<string, CleanCount> | undefined> & {
  counts?: Record<string, CleanCount>;
};

@Injectable({ providedIn: 'root' })
export class CleaningService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  getStatus(): Observable<CleanStatus> {
    return this.http.get<CleanStatus>(`${this.config.apiHost}/api/cleaning/status`);
  }

  toggleAll(enabled: boolean): Observable<{ updated: number }> {
    const flag = enabled ? 1 : 0;
    return this.http.put<{ updated: number }>(`${this.config.apiHost}/api/cleaning/all/${flag}`, {});
  }

  toggleClean(type: CleanAlarmType, enabled: boolean): Observable<{ updated: number }> {
    const flag = enabled ? 1 : 0;
    return this.http.put<{ updated: number }>(`${this.config.apiHost}/api/cleaning/${type}/${flag}`, {});
  }
}
