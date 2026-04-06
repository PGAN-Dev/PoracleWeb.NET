import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';

export type CleanAlarmType =
  | 'eggs'
  | 'fortchanges'
  | 'gyms'
  | 'invasions'
  | 'lures'
  | 'maxbattles'
  | 'monsters'
  | 'nests'
  | 'quests'
  | 'raids';

@Injectable({ providedIn: 'root' })
export class CleaningService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  getStatus(): Observable<Record<string, boolean>> {
    return this.http.get<Record<string, boolean>>(`${this.config.apiHost}/api/cleaning/status`);
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
