import { HttpClient } from '@angular/common/http';
import { DestroyRef, Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { AuthService } from './auth.service';
import { ConfigService } from './config.service';
import { SettingsService } from './settings.service';
import { TokenStoreService } from './token-store.service';
import { DriftField, DriftItem, LinkSide, LinkStatus } from '../models';

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

  /**
   * Whether an alarm dialog may ask where to deliver: linked, and both sides have an account to deliver to.
   * Anyone else sees no "Deliver to" anywhere, and the API is called exactly as before.
   */
  readonly canChoose = computed(() => {
    const status = this.statusSignal();
    const sides = status?.sides ?? [];
    return !!status?.linked && sides.length === 2 && sides.every(s => s.exists);
  });

  /** What the two accounts disagree on. Empty when linked and in step, and when the server couldn't compare them. */
  readonly drift = computed<DriftItem[]>(() => this.statusSignal()?.drift ?? []);

  /** Null until the first read, and again after sign-out. `{ linked: false }` is a read that found no link. */
  readonly status = this.statusSignal.asReadonly();

  constructor() {
    // Reads the settings signals too: on a reload the user arrives while settings are still loading, and
    // `disable_account_linking` would read as off. Nothing is asked until they are in, and a later change
    // to the switch is picked up.
    effect(() => {
      const user = this.auth.user();
      const allowed = this.allowed();
      untracked(() => {
        if (!user) {
          this.statusSignal.set(null);
        } else if (allowed) {
          void this.refresh();
        }
      });
    });

    const onFocus = (): void => {
      if (!this.tokenStore.getAccessToken() || !this.allowed()) return;
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
    if (!this.allowed()) return;
    this.lastRefreshAt = Date.now();
    try {
      this.statusSignal.set(await firstValueFrom(this.http.get<LinkStatus>(`${this.config.apiHost}/api/account-link/status`)));
    } catch {
      // Silent on purpose; the next focus tries again.
    }
  }

  /**
   * Makes the other account match the one the person keeps. `expected` is the item's fingerprint, so a pair
   * that changed since the banner was drawn answers 409 instead of being overwritten. Either way the status
   * is read again, which is what makes a stale banner go away.
   */
  async resolve(field: DriftField, keep: LinkSide, expected: string): Promise<void> {
    try {
      await firstValueFrom(this.http.post<void>(`${this.config.apiHost}/api/account-link/resolve`, { expected, field, keep }));
    } finally {
      await this.refresh();
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

  /** Settings are in, linking is on, and nobody is being viewed as. Anything else would be a request the server refuses. */
  private allowed(): boolean {
    return this.settings.settingsLoaded() && !this.settings.isDisabled('disable_account_linking') && !this.auth.isImpersonating();
  }
}
