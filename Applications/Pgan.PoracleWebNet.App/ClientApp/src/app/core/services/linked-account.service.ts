import { HttpClient } from '@angular/common/http';
import { DestroyRef, Injectable, effect, inject, signal, untracked } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { AuthService } from './auth.service';
import { ConfigService } from './config.service';
import { SettingsService } from './settings.service';
import { TokenStoreService } from './token-store.service';
import { LinkSide, LinkStatus } from '../models';

/** A focus refresh is skipped when the last read is newer than this. */
const FOCUS_REFRESH_MIN_GAP_MS = 60_000;

/**
 * The signed-in person's link between their Discord and Telegram accounts, held as a signal.
 *
 * It reads once when a user arrives and again on window focus (at most once a minute), because the other
 * side's state changes out of band: PoracleNG pauses a human the moment it stops being registered. With
 * `disable_account_linking` on, every route here answers 403, so nothing is asked.
 */
@Injectable({ providedIn: 'root' })
export class LinkedAccountService {
  private readonly auth = inject(AuthService);
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);
  private lastRefreshAt = 0;
  private readonly settings = inject(SettingsService);
  private readonly statusSignal = signal<LinkStatus | null>(null);
  private readonly tokenStore = inject(TokenStoreService);

  /** Null until the first read, and again after sign-out. `{ linked: false }` is a read that found no link. */
  readonly status = this.statusSignal.asReadonly();

  constructor() {
    effect(() => {
      const user = this.auth.user();
      untracked(() => {
        if (user) {
          void this.refresh();
        } else {
          this.statusSignal.set(null);
        }
      });
    });

    const onFocus = (): void => {
      if (!this.tokenStore.getAccessToken() || this.disabled()) return;
      if (Date.now() - this.lastRefreshAt < FOCUS_REFRESH_MIN_GAP_MS) return;
      void this.refresh();
    };
    if (typeof window !== 'undefined') window.addEventListener('focus', onFocus);
    inject(DestroyRef).onDestroy(() => {
      if (typeof window !== 'undefined') window.removeEventListener('focus', onFocus);
    });
  }

  async linkTelegram(data: Record<string, string>): Promise<void> {
    await firstValueFrom(this.http.post(`${this.config.apiHost}/api/account-link/telegram`, data));
    await this.refresh();
  }

  /** A seam for the one place this service leaves the app. */
  protected navigate(url: string): void {
    window.location.href = url;
  }

  /** Reads the status. A failed read keeps what is shown: the page words its own errors. */
  async refresh(): Promise<void> {
    if (this.disabled()) return;
    this.lastRefreshAt = Date.now();
    try {
      this.statusSignal.set(await firstValueFrom(this.http.get<LinkStatus>(`${this.config.apiHost}/api/account-link/status`)));
    } catch {
      // Silent on purpose; the next focus tries again.
    }
  }

  async setDefault(side: LinkSide): Promise<void> {
    await firstValueFrom(this.http.put<void>(`${this.config.apiHost}/api/account-link/default`, { destination: side }));
    await this.refresh();
  }

  /** The start call sets the sealed link cookie, so it must carry cookies; the browser then goes to Discord. */
  async startDiscordLink(): Promise<void> {
    const { url } = await firstValueFrom(
      this.http.post<{ url: string }>(`${this.config.apiHost}/api/account-link/discord/start`, {}, { withCredentials: true }),
    );
    this.navigate(url);
  }

  async unlink(): Promise<void> {
    await firstValueFrom(this.http.delete<void>(`${this.config.apiHost}/api/account-link`));
    await this.refresh();
  }

  private disabled(): boolean {
    return this.settings.isDisabled('disable_account_linking') || this.auth.isImpersonating();
  }
}
