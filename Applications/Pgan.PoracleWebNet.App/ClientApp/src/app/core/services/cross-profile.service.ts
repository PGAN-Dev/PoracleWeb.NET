import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { CrossProfileOverview } from '../models';
import { ConfigService } from './config.service';

@Injectable({ providedIn: 'root' })
export class CrossProfileService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  duplicateProfile(profileNo: number, name: string): Observable<{ alarmsCopied: number; newProfileNo: number; token: string }> {
    return this.http.post<{ alarmsCopied: number; newProfileNo: number; token: string }>(
      `${this.config.apiHost}/api/cross-profile/duplicate/${profileNo}`,
      { name },
    );
  }

  getOverview(): Observable<CrossProfileOverview> {
    return this.http.get<CrossProfileOverview>(`${this.config.apiHost}/api/cross-profile`);
  }

  importProfile(backup: {
    alarms: Record<string, unknown[]>;
    profileName: string;
    version: number;
  }): Observable<{ alarmsCopied: number; newProfileNo: number; token: string }> {
    return this.http.post<{ alarmsCopied: number; newProfileNo: number; token: string }>(
      `${this.config.apiHost}/api/cross-profile/import`,
      backup,
    );
  }
}
