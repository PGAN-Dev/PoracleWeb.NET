import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, forkJoin, map, of, tap } from 'rxjs';

import { ConfigService } from './config.service';
import { DiscordServerConfig, OidcServerConfig, PwebSetting, SiteSetting, TelegramServerConfig } from '../models';

/** Union of old and new setting response shapes */
type AnySettingItem = PwebSetting | SiteSetting;

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  private loaded = false;
  /** Cached site settings as key→value map, loaded once at app init */
  readonly siteSettings = signal<Record<string, string>>({});

  /**
   * The `disable_*` keys the upstream Poracle deployment forces off in its own config, regardless of
   * what the site settings say. Poracle's processor drops those webhooks and its bot refuses the
   * matching commands, so offering the type here would only produce alarms that save and never fire.
   * Empty when Poracle is unreachable or too old to report the flags — the site settings then stay in
   * sole charge. See #769.
   */
  readonly upstreamDisabled = signal<readonly string[]>([]);

  getAll(): Observable<AnySettingItem[]> {
    // Fetched together so a nav item never renders for a type the server will 403. A failure here is
    // not fatal: the settings still load and the server-side gate remains the real enforcement point.
    return forkJoin({
      settings: this.http.get<AnySettingItem[]>(`${this.config.apiHost}/api/settings`),
      upstream: this.http.get<string[]>(`${this.config.apiHost}/api/settings/upstream-disabled`).pipe(catchError(() => of([]))),
    }).pipe(
      tap(({ settings, upstream }) => {
        this.siteSettings.set(this.normalize(settings));
        this.upstreamDisabled.set(upstream);
        this.loaded = true;
      }),
      map(({ settings }) => settings),
    );
  }

  getDiscordConfig(): Observable<DiscordServerConfig> {
    return this.http.get<DiscordServerConfig>(`${this.config.apiHost}/api/settings/discord-config`);
  }

  getOidcConfig(): Observable<OidcServerConfig> {
    return this.http.get<OidcServerConfig>(`${this.config.apiHost}/api/settings/oidc-config`);
  }

  getTelegramConfig(): Observable<TelegramServerConfig> {
    return this.http.get<TelegramServerConfig>(`${this.config.apiHost}/api/settings/telegram-config`);
  }

  /**
   * True when a feature is off — because an admin disabled it here, or because Poracle disabled it
   * upstream. Poracle's flags are a floor, never a way to switch something back on.
   */
  isDisabled(key: string): boolean {
    return this.siteSettings()[key]?.toLowerCase() === 'true' || this.isForcedByPoracle(key);
  }

  /**
   * True when Poracle's own config disables this type, which the admin page cannot override. Kept
   * separate from {@link isDisabled} so that page can explain the switch instead of just showing it
   * off, and so nothing mistakes a forced-off type for an admin decision.
   */
  isForcedByPoracle(key: string): boolean {
    return this.upstreamDisabled().includes(key);
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
