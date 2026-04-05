import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

import { ConfigService } from './config.service';
import { DiscordServerConfig, PoracleConfig, PwebSetting, SiteSetting, TelegramServerConfig } from '../models';

/** Union of old and new setting response shapes */
type AnySettingItem = PwebSetting | SiteSetting;

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  private loaded = false;
  /** Cached site settings as key→value map, loaded once at app init */
  readonly siteSettings = signal<Record<string, string>>({});

  getAll(): Observable<AnySettingItem[]> {
    return this.http.get<AnySettingItem[]>(`${this.config.apiHost}/api/settings`).pipe(
      tap(settings => {
        this.siteSettings.set(this.normalize(settings));
        this.loaded = true;
      }),
    );
  }

  getConfig(): Observable<PoracleConfig> {
    return this.http.get<PoracleConfig>(`${this.config.apiHost}/api/settings/config`);
  }

  getDiscordConfig(): Observable<DiscordServerConfig> {
    return this.http.get<DiscordServerConfig>(`${this.config.apiHost}/api/settings/discord-config`);
  }

  getTelegramConfig(): Observable<TelegramServerConfig> {
    return this.http.get<TelegramServerConfig>(`${this.config.apiHost}/api/settings/telegram-config`);
  }

  /** Returns true if a feature is disabled via site settings */
  isDisabled(key: string): boolean {
    return this.siteSettings()[key]?.toLowerCase() === 'true';
  }

  /** Load settings once (idempotent) */
  loadOnce(): Observable<AnySettingItem[]> {
    if (this.loaded)
      return new Observable(sub => {
        sub.next([]);
        sub.complete();
      });
    return this.getAll();
  }

  /** Load public settings (no auth required) — safe to call from login page */
  loadPublic(): Observable<AnySettingItem[]> {
    return this.http.get<AnySettingItem[]>(`${this.config.apiHost}/api/settings/public`).pipe(
      tap(settings => {
        const current = this.siteSettings();
        const map: Record<string, string> = { ...current, ...this.normalize(settings) };
        this.siteSettings.set(map);
      }),
    );
  }

  /** Normalize a mixed array of PwebSetting / SiteSetting into a key→value map */
  normalize(items: AnySettingItem[]): Record<string, string> {
    const map: Record<string, string> = {};
    for (const item of items) {
      const key = 'key' in item ? item.key : item.setting;
      if (key) map[key] = item.value ?? '';
    }
    return map;
  }

  update(key: string, value: string, category?: string): Observable<AnySettingItem> {
    return this.http
      .put<AnySettingItem>(`${this.config.apiHost}/api/settings/${encodeURIComponent(key)}`, {
        category,
        key,
        value,
      })
      .pipe(
        tap(() => {
          // Update the cached signal immediately so UI reflects the change without refresh
          this.siteSettings.update(current => ({ ...current, [key]: value }));
        }),
      );
  }
}
