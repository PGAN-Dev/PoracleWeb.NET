import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { ConfigService } from './config.service';
import { PoracleConfig, PwebSetting } from '../models';

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  /** Cached site settings as key→value map, loaded once at app init */
  readonly siteSettings = signal<Record<string, string>>({});
  private loaded = false;

  /** Returns true if a feature is disabled via pweb_settings */
  isDisabled(key: string): boolean {
    return this.siteSettings()[key]?.toLowerCase() === 'true';
  }

  getConfig(): Observable<PoracleConfig> {
    return this.http.get<PoracleConfig>(`${this.config.apiHost}/api/settings/config`);
  }

  getAll(): Observable<PwebSetting[]> {
    return this.http.get<PwebSetting[]>(`${this.config.apiHost}/api/settings`).pipe(
      tap((settings) => {
        if (!this.loaded) {
          const map: Record<string, string> = {};
          for (const s of settings) if (s.setting) map[s.setting] = s.value ?? '';
          this.siteSettings.set(map);
          this.loaded = true;
        }
      }),
    );
  }

  /** Load settings once (idempotent) */
  loadOnce(): Observable<PwebSetting[]> {
    if (this.loaded) return new Observable((sub) => { sub.next([]); sub.complete(); });
    return this.getAll();
  }

  update(key: string, value: string): Observable<PwebSetting> {
    return this.http.put<PwebSetting>(`${this.config.apiHost}/api/settings/${encodeURIComponent(key)}`, {
      setting: key,
      value,
    });
  }
}
