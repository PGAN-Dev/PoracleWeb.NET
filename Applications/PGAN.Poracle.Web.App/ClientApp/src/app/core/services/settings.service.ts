import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ConfigService } from './config.service';
import { PoracleConfig, PwebSetting } from '../models';

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  getConfig(): Observable<PoracleConfig> {
    return this.http.get<PoracleConfig>(`${this.config.apiHost}/api/settings/config`);
  }

  getAll(): Observable<PwebSetting[]> {
    return this.http.get<PwebSetting[]>(`${this.config.apiHost}/api/settings`);
  }

  update(key: string, value: string): Observable<PwebSetting> {
    return this.http.put<PwebSetting>(`${this.config.apiHost}/api/settings/${encodeURIComponent(key)}`, {
      setting: key,
      value,
    });
  }
}
