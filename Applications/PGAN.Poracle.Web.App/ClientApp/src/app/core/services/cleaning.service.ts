import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ConfigService } from './config.service';

export type CleanAlarmType = 'monsters' | 'raids' | 'eggs' | 'quests' | 'invasions' | 'lures' | 'nests' | 'gyms';

@Injectable({ providedIn: 'root' })
export class CleaningService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  cleanAll(): Observable<void> {
    return this.http.delete<void>(`${this.config.apiHost}/api/cleaning/all`);
  }

  toggleClean(type: CleanAlarmType, enabled: boolean): Observable<{ updated: number }> {
    const flag = enabled ? 1 : 0;
    return this.http.put<{ updated: number }>(`${this.config.apiHost}/api/cleaning/${type}/${flag}`, {});
  }
}
