import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal, computed } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, ReplaySubject, tap, firstValueFrom } from 'rxjs';

import { AlertLanguageService } from './alert-language.service';
import { ConfigService } from './config.service';
import { SettingsService } from './settings.service';
import { TokenStoreService } from './token-store.service';
import { UserInfo, LoginResponse, TelegramConfig, AuthProviders } from '../models';

const TOKEN_KEY = 'poracle_token';
const ADMIN_TOKEN_KEY = 'poracle_admin_token';

/**
 * The `#error=account_disabled&support_url=...` fragment LoginComponent reads back with
 * `new URLSearchParams(location.hash)` -- one `encodeURIComponent`-equivalent pass via
 * `URLSearchParams.toString()`, so it decodes with exactly one pass on the other end. Exported as its
 * own function, rather than inlined, so that one-encode contract can be pinned in a unit test without
 * going anywhere near `window.location` (jsdom no-ops that assignment) or the Router (this deliberately
 * bypasses it -- see the call site).
 */
export function disabledAccountFragment(supportUrl: string | null): string {
  const fragment = new URLSearchParams({ error: 'account_disabled' });
  if (supportUrl) {
    fragment.set('support_url', supportUrl);
  }
  return fragment.toString();
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  /**
   * Set from the most recent `/api/auth/me` 401 that carried `code: "account_disabled"`. Read once, by
   * {@link handleTokenFromCallback} right after it awaits {@link loadCurrentUser}, to tell "this login
   * found a disabled account" apart from any other reason the user object came back null. `support_url`
   * travels here rather than through the normal settings fetch because that read is signed-in only
   * (`SettingsController.UserVisibleKeys`), and the one person who needs it here is not. See #911.
   */
  private readonly _disabledAccountInfo = signal<{ supportUrl: string | null } | null>(null);
  private readonly _isImpersonating = signal(!!localStorage.getItem(ADMIN_TOKEN_KEY));
  private readonly _profileResynced = signal(false);
  private readonly alertLanguage = inject(AlertLanguageService);
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

    // A 401 under impersonation drops back to the admin's own token instead of ending the session;
    // pick the admin's user back up so the banner and nav match who the token now names. See #706.
    this.tokenStore.impersonationEnded$.subscribe(() => {
      this._isImpersonating.set(false);
      void this.loadCurrentUser();
    });

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

    // A disabled account's /me 401 is silent (SILENT_URL_PATTERNS) and fires while isAuthCallbackRoute()
    // is still true, so neither toasts nor ends the session -- the token survived, and the dashboard this
    // app was about to navigate to would have sent its own requests with it, 401ing in turn, now outside
    // the callback route, with the generic "session expired" toast and no word of why. The disabled
    // explanation only ever existed behind sign-in, so this is the one path to show it to the person who
    // needs it. See #911.
    const disabled = this._disabledAccountInfo();
    if (disabled) {
      this.clearSession();
      // A real navigation, not router.navigate({ fragment }) -- confirmed live that the Router's own
      // fragment serialization re-escapes a literal '%' (so an already-percent-encoded support_url came
      // out double-encoded, e.g. 'https%253A%252F%252F...', and the link LoginComponent renders was
      // broken). Every other /login#error=... redirect in this app is already a real navigation, built
      // by the backend the same way (AuthController's Redirect($"{frontendUrl}/login#error=...")); this
      // is the one case built client-side.
      window.location.href = `/login#${disabledAccountFragment(disabled.supportUrl)}`;
      return;
    }

    // Load site settings now that we have a valid token — the initial loadOnce()
    // in App.ngOnInit() fires before the token is stored, so settings (including
    // custom_title) fail silently and never reload.
    this.settingsService.loadOnce().subscribe();
    // Same reason, for the same reason: App.ngOnInit skips this while signed out (#775), so without
    // it here a login completed inside one page session would never reconcile the alert language.
    this.alertLanguage.load();
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
    const sentWith = localStorage.getItem(TOKEN_KEY);
    return new Promise(resolve => {
      this.http.get<UserInfo>(`${this.config.apiHost}/api/auth/me`).subscribe({
        error: err => {
          // A 401 for a token that is no longer the session's says nothing about the session there is now.
          // A revoked impersonation's /me can land after the restored token's own /me has loaded the
          // account, and forgetting the user then drew the signed-out shell around a valid token.
          if (localStorage.getItem(TOKEN_KEY) !== sentWith) {
            resolve(null);
            return;
          }
          if (err.status === 401) {
            // Only the user object. The interceptor owns what happens to the tokens on a 401 -- either
            // ending the session, which empties this via sessionCleared$, or the impersonation fallback
            // that installs the admin's own token. Removing poracle_token here as well deleted the token
            // that fallback had just restored, one line after it was written. See #706, #616.
            this.currentUser.set(null);
            this._disabledAccountInfo.set(err.error?.code === 'account_disabled' ? { supportUrl: err.error?.supportUrl ?? null } : null);
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

    if (options?.sso) {
      // The user stays: the browser is leaving for the provider, and forgetting the user first swapped the
      // shell's layout on the page being left, which rebuilt it and sent its loads with no token. The
      // tokens are already gone, so nothing can authenticate in the meantime.
      window.location.href = `${this.config.apiHost}/api/auth/oidc/logout`;
      return;
    }

    // The user goes after the page does, not before. The shell has a router outlet in its signed-in
    // layout and another in its signed-out one, so clearing the user first swapped layouts while the
    // router was still on the page being left -- and the new outlet built that page again. From the
    // dashboard that meant every load it makes, plus the quiet-period list, sent without a token: a
    // burst of 401s and a "session expired" toast for each. The tokens are already gone above, so
    // nothing in between can make an authenticated request.
    void Promise.resolve(this.router.navigate(['/login'], { queryParams: { loggedout: 1 } })).finally(() => this.currentUser.set(null));
  }

  /** Store a new JWT token (e.g. after profile switch). */
  setToken(token: string): void {
    localStorage.setItem(TOKEN_KEY, token);
  }

  /**
   * Restore the admin's original token. Resolves whether there was one to restore; when there was not,
   * the session has been signed out and the caller must not send anything on its behalf.
   */
  async stopImpersonating(): Promise<boolean> {
    const adminToken = localStorage.getItem(ADMIN_TOKEN_KEY);
    if (!adminToken) {
      // Nothing to go back to -- the admin token was discarded with the rest of the session. Silently
      // returning left a visible button that did nothing at all. See #627.
      this.logout();
      return false;
    }

    localStorage.setItem(TOKEN_KEY, adminToken);
    localStorage.removeItem(ADMIN_TOKEN_KEY);
    this._isImpersonating.set(false);
    await this.loadCurrentUser();
    this.router.navigate(['/admin']);
    return true;
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
