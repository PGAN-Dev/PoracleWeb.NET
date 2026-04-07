import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { ProfileOverview } from '../models';
import { ConfigService } from './config.service';

@Injectable({ providedIn: 'root' })
export class ProfileOverviewService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  duplicateProfile(profileNo: number, name: string): Observable<{ alarmsCopied: number; newProfileNo: number; token: string }> {
    return this.http.post<{ alarmsCopied: number; newProfileNo: number; token: string }>(
      `${this.config.apiHost}/api/profile-overview/duplicate/${profileNo}`,
      { name },
    );
  }

  getOverview(): Observable<ProfileOverview> {
    return this.http.get<ProfileOverview>(`${this.config.apiHost}/api/profile-overview`);
  }

  importProfile(backup: {
    alarms: Record<string, unknown[]>;
    profileName: string;
    version: number;
  }): Observable<{ alarmsCopied: number; newProfileNo: number; token: string }> {
    return this.http.post<{ alarmsCopied: number; newProfileNo: number; token: string }>(
      `${this.config.apiHost}/api/profile-overview/import`,
      backup,
    );
  }
}
