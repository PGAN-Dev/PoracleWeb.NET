import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, Subject, catchError, finalize, map, shareReplay, tap, throwError } from 'rxjs';

import { ConfigService } from './config.service';

const TOKEN_KEY = 'poracle_token';
const REFRESH_KEY = 'poracle_refresh_token';
const EXPIRES_KEY = 'poracle_token_expires_at';
// Owned by AuthService, cleared here so the 401 path can end a session without constructing it.
const ADMIN_TOKEN_KEY = 'poracle_admin_token';

/** Refresh proactively this many ms before the access token's `exp`. */
const EXPIRY_SKEW_MS = 60_000;

interface RefreshResponse {
  expiresIn: number;
  refreshToken: string;
  token: string;
}

/**
 * Single source of truth for the OIDC silent-refresh tokens (the short-lived JWT, the opaque
 * refresh token, and the JWT expiry) plus the refresh call itself. The refresh is single-flighted
 * via `shareReplay` so concurrent 401s collapse to one network round-trip. Only relevant when the
 * user logged in through an OIDC provider with refresh enabled; otherwise no refresh token is
 * stored and every consumer falls back to the plain "401 → logout" path.
 */
@Injectable({ providedIn: 'root' })
export class TokenStoreService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);

  private refreshInFlight$: Observable<string> | null = null;

  /** Emits when a refresh definitively fails — AuthService subscribes and logs the user out. */
  readonly forceLogout$ = new Subject<void>();

  /** Emits when a 401 dropped an impersonation session back to the admin's own token. */
  readonly impersonationEnded$ = new Subject<void>();

  /** Emits when the session was discarded from under the app — AuthService resets its own state. */
  readonly sessionCleared$ = new Subject<void>();

  /** Clears the refresh token + expiry (the main JWT is owned by AuthService). */
  clear(): void {
    localStorage.removeItem(REFRESH_KEY);
    localStorage.removeItem(EXPIRES_KEY);
  }

  /**
   * Discards every key a session consists of, and tells AuthService to forget the user.
   */
  /* The 401 path used to remove poracle_token by hand, leaving the admin impersonation token, the
   * refresh token and the expiry behind, and leaving AuthService still holding a user. It lives here
   * rather than on AuthService because an interceptor that injects AuthService constructs it, and
   * constructing it fires a /api/auth/me request. See #616, #627, #628. */
  clearAll(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(ADMIN_TOKEN_KEY);
    this.clear();
    this.sessionCleared$.next();
  }

  getAccessToken(): string | null {
    return localStorage.getItem(TOKEN_KEY);
  }

  getRefreshToken(): string | null {
    return localStorage.getItem(REFRESH_KEY);
  }

  hasRefreshToken(): boolean {
    return !!this.getRefreshToken()?.trim();
  }

  /** True when the access token is within the skew window of expiry (or already expired). */
  isExpiringSoon(): boolean {
    const stored = Number(localStorage.getItem(EXPIRES_KEY));
    const expiresAt = stored || this.decodeExpiry(this.getAccessToken() ?? '');
    return !!expiresAt && expiresAt - Date.now() < EXPIRY_SKEW_MS;
  }

  /**
   * Refreshes the session against the API, rotating both the JWT and the opaque refresh token.
   * Concurrent callers share one in-flight request. On failure it clears tokens and emits
   * `forceLogout$`, then rethrows.
   */
  refresh(): Observable<string> {
    if (this.refreshInFlight$) {
      return this.refreshInFlight$;
    }

    const refreshToken = this.getRefreshToken();
    if (!refreshToken) {
      this.forceLogout$.next();
      return throwError(() => new Error('no_refresh_token'));
    }

    this.refreshInFlight$ = this.http.post<RefreshResponse>(`${this.config.apiHost}/api/auth/oidc/refresh`, { refreshToken }).pipe(
      tap(res => this.storeTokens(res.token, res.refreshToken, res.expiresIn)),
      map(res => res.token),
      catchError(err => {
        this.clear();
        this.forceLogout$.next();
        return throwError(() => err);
      }),
      finalize(() => {
        this.refreshInFlight$ = null;
      }),
      shareReplay(1),
    );

    return this.refreshInFlight$;
  }

  /** Best-effort server-side revoke of the current session family (logout). Fire-and-forget. */
  revoke(): void {
    const refreshToken = this.getRefreshToken();
    if (!refreshToken) {
      return;
    }

    this.http.post(`${this.config.apiHost}/api/auth/oidc/refresh/revoke`, { refreshToken }).subscribe({
      error: () => {
        /* logout must proceed regardless */
      },
    });
  }

  /** Persists the JWT, the opaque refresh token (when present), and the computed expiry. */
  storeTokens(token: string, refreshToken: string | null, expiresInSeconds?: number): void {
    localStorage.setItem(TOKEN_KEY, token);

    // Cleared, not left alone, when the new login has no refresh token of its own. A Discord or
    // Telegram login on a browser that previously held an OIDC session used to inherit that session's
    // refresh token: the refresh interceptor then saw hasRefreshToken() true, posted the stale token,
    // and replaced the JWT with one minted for the previous user. See #625.
    if (refreshToken) {
      localStorage.setItem(REFRESH_KEY, refreshToken);
    } else {
      localStorage.removeItem(REFRESH_KEY);
      localStorage.removeItem(EXPIRES_KEY);
    }

    const expiresAt = expiresInSeconds ? Date.now() + expiresInSeconds * 1000 : this.decodeExpiry(token);
    if (expiresAt) {
      localStorage.setItem(EXPIRES_KEY, String(expiresAt));
    }
  }

  /**
   * Puts the stashed admin token back as the active one, if there is one. Returns whether it did.
   */
  /* A 401 belongs to whoever the token names, and while inspecting an account that is the inspected
   * user, not the admin holding the session. clearAll() treats every 401 as the end of the session and
   * discards poracle_admin_token with the rest, so one blocked or deleted account signed the admin out
   * of their own session with nothing to return to -- inspecting exactly the accounts an admin most
   * needs to inspect. Falling back is self-limiting: if the restored admin token is itself dead, the
   * next 401 finds no stash and clears normally. See #706, #616. */
  tryRestoreAdminSession(): boolean {
    const adminToken = localStorage.getItem(ADMIN_TOKEN_KEY);
    if (!adminToken) {
      return false;
    }

    localStorage.setItem(TOKEN_KEY, adminToken);
    localStorage.removeItem(ADMIN_TOKEN_KEY);
    this.impersonationEnded$.next();
    return true;
  }

  private decodeExpiry(token: string): number | null {
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      return typeof payload.exp === 'number' ? payload.exp * 1000 : null;
    } catch {
      return null;
    }
  }
}
