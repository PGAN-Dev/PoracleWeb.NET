import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal, computed } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, ReplaySubject, tap, firstValueFrom } from 'rxjs';

import { ConfigService } from './config.service';
import { SettingsService } from './settings.service';
import { TokenStoreService } from './token-store.service';
import { UserInfo, LoginResponse, TelegramConfig, AuthProviders } from '../models';

const TOKEN_KEY = 'poracle_token';
const ADMIN_TOKEN_KEY = 'poracle_admin_token';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly _isImpersonating = signal(!!localStorage.getItem(ADMIN_TOKEN_KEY));
  private readonly _profileResynced = signal(false);
  private readonly config = inject(ConfigService);
  private readonly currentUser = signal<UserInfo | null>(null);

  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly settingsService = inject(SettingsService);
  private readonly tokenStore = inject(TokenStoreService);
  private readonly userLoaded$ = new ReplaySubject<UserInfo | null>(1);

  readonly hasManagedWebhooks = computed(() => (this.currentUser()?.managedWebhooks?.length ?? 0) > 0);
  readonly isAdmin = computed(() => this.currentUser()?.isAdmin ?? false);
  readonly isImpersonating = this._isImpersonating.asReadonly();
  readonly isLoggedIn = computed(() => !!this.currentUser());
  readonly managedWebhooks = computed(() => this.currentUser()?.managedWebhooks ?? []);
  readonly profileResynced = this._profileResynced.asReadonly();
  readonly user = this.currentUser.asReadonly();

  constructor() {
    // A definitively failed silent refresh ends the session.
    this.tokenStore.forceLogout$.subscribe(() => this.logout());

    // The 401 path discards the session from under us; without this the app kept rendering the
    // signed-in shell and an impersonation banner around the login page. See #627, #628.
    this.tokenStore.sessionCleared$.subscribe(() => this.clearSession());

    const token = localStorage.getItem(TOKEN_KEY);
    if (token) {
      this.loadCurrentUser();
    } else {
      this.userLoaded$.next(null);
    }
  }

  clearProfileResynced(): void {
    this._profileResynced.set(false);
  }

  /**
   * Discards every trace of the session without navigating.
   */
  /* The 401 path used to remove token keys by hand, which left `currentUser` and `_isImpersonating`
   * set -- so the login page rendered inside the signed-in shell, complete with an impersonation
   * banner whose Stop button did nothing, and bounced back to /dashboard on the next navigation.
   * Deliberately does not navigate: the interceptor preserves the current query params, and going
   * through logout() would append loggedout=1 and suppress the OIDC auto-redirect. See #627, #628. */
  clearSession(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(ADMIN_TOKEN_KEY);
    this._isImpersonating.set(false);
    this.currentUser.set(null);
    this.userLoaded$.next(null);
  }

  getProviders(): Observable<AuthProviders> {
    return this.http.get<AuthProviders>(`${this.config.apiHost}/api/auth/providers`);
  }

  /** @deprecated Use `getProviders()` instead — kept for backward compatibility. */
  getTelegramConfig(): Observable<TelegramConfig> {
    return this.http.get<TelegramConfig>(`${this.config.apiHost}/api/auth/telegram/config`);
  }

  getToken(): string | null {
    return localStorage.getItem(TOKEN_KEY);
  }

  async handleTokenFromCallback(token: string, refreshToken?: string | null): Promise<void> {
    // Stores the JWT plus, for refresh-backed OIDC logins, the opaque refresh token + expiry.
    this.tokenStore.storeTokens(token, refreshToken ?? null);
    await this.loadCurrentUser();
    // Load site settings now that we have a valid token — the initial loadOnce()
    // in App.ngOnInit() fires before the token is stored, so settings (including
    // custom_title) fail silently and never reload.
    this.settingsService.loadOnce().subscribe();
    this.router.navigate(['/dashboard']);
  }

  /** Switch to impersonated user token, saving the admin token for later. */
  impersonate(token: string): void {
    const adminToken = localStorage.getItem(TOKEN_KEY);
    if (adminToken) {
      localStorage.setItem(ADMIN_TOKEN_KEY, adminToken);
    }
    localStorage.setItem(TOKEN_KEY, token);
    this._isImpersonating.set(true);
    this.loadCurrentUser();
    this.router.navigate(['/dashboard']);
  }

  isAuthenticated(): boolean {
    return !!this.getToken();
  }

  loadCurrentUser(): Promise<UserInfo | null> {
    return new Promise(resolve => {
      this.http.get<UserInfo>(`${this.config.apiHost}/api/auth/me`).subscribe({
        error: err => {
          if (err.status === 401) {
            localStorage.removeItem(TOKEN_KEY);
            this.currentUser.set(null);
          }
          this.userLoaded$.next(null);
          resolve(null);
        },
        next: user => {
          // Handle JWT profile resync — when PoracleNG changes the active profile
          // out-of-band (active_hours scheduler, bot commands), the backend detects
          // the mismatch and returns a refreshed token with the correct profileNo.
          if (user.token) {
            this.setToken(user.token);
            this._profileResynced.set(true);
          } else {
            this._profileResynced.set(false);
          }
          this.currentUser.set(user);
          this.userLoaded$.next(user);
          resolve(user);
        },
      });
    });
  }

  loginWithDiscord(): void {
    window.location.href = `${this.config.apiHost}/api/auth/discord/login`;
  }

  loginWithOidc(): void {
    window.location.href = `${this.config.apiHost}/api/auth/oidc/login`;
  }

  loginWithTelegram(telegramData: Record<string, string>): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${this.config.apiHost}/api/auth/telegram/verify`, telegramData)
      .pipe(tap(res => this.handleAuthResponse(res)));
  }

  /**
   * Clears the local session. With `sso: true` it then performs an OIDC RP-initiated
   * (single) logout — bouncing through the API to the provider's end-session endpoint so
   * the provider session is ended too, returning to the signed-out landing. Otherwise it
   * navigates to `/login?loggedout=1`, which shows the signed-out panel and (importantly)
   * suppresses the OIDC auto-redirect so the user isn't silently logged straight back in.
   */
  logout(options?: { sso?: boolean }): void {
    // Revoke the server-side refresh session (fire-and-forget) before discarding local state.
    this.tokenStore.revoke();
    this.tokenStore.clear();
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(ADMIN_TOKEN_KEY);
    this._isImpersonating.set(false);
    this.currentUser.set(null);

    if (options?.sso) {
      window.location.href = `${this.config.apiHost}/api/auth/oidc/logout`;
      return;
    }

    this.router.navigate(['/login'], { queryParams: { loggedout: 1 } });
  }

  /** Store a new JWT token (e.g. after profile switch). */
  setToken(token: string): void {
    localStorage.setItem(TOKEN_KEY, token);
  }

  /** Restore the admin's original token. */
  async stopImpersonating(): Promise<void> {
    const adminToken = localStorage.getItem(ADMIN_TOKEN_KEY);
    if (!adminToken) {
      // Nothing to go back to -- the admin token was discarded with the rest of the session. Silently
      // returning left a visible button that did nothing at all. See #627.
      this.logout();
      return;
    }

    {
      localStorage.setItem(TOKEN_KEY, adminToken);
      localStorage.removeItem(ADMIN_TOKEN_KEY);
      this._isImpersonating.set(false);
      await this.loadCurrentUser();
      this.router.navigate(['/admin']);
    }
  }

  toggleAlerts(): Observable<{ enabled: boolean }> {
    return this.http.post<{ enabled: boolean }>(`${this.config.apiHost}/api/auth/alerts/toggle`, {});
  }

  /** Returns a promise that resolves once the user has been loaded (or failed). */
  waitForUser(): Promise<UserInfo | null> {
    return firstValueFrom(this.userLoaded$);
  }

  private handleAuthResponse(res: LoginResponse): void {
    // Through the store rather than a bare setItem, so a leftover refresh token and expiry from a
    // previous OIDC session are cleared instead of inherited. See #625.
    this.tokenStore.storeTokens(res.token, null);
    this.currentUser.set(res.user);
    this.userLoaded$.next(res.user);
  }
}
