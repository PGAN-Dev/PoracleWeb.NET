import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, Subject, catchError, finalize, map, shareReplay, tap, throwError } from 'rxjs';

import { ConfigService } from './config.service';

const TOKEN_KEY = 'poracle_token';
const REFRESH_KEY = 'poracle_refresh_token';
const EXPIRES_KEY = 'poracle_token_expires_at';

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

  /** Clears the refresh token + expiry (the main JWT is owned by AuthService). */
  clear(): void {
    localStorage.removeItem(REFRESH_KEY);
    localStorage.removeItem(EXPIRES_KEY);
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

    if (refreshToken) {
      localStorage.setItem(REFRESH_KEY, refreshToken);
    }

    const expiresAt = expiresInSeconds ? Date.now() + expiresInSeconds * 1000 : this.decodeExpiry(token);
    if (expiresAt) {
      localStorage.setItem(EXPIRES_KEY, String(expiresAt));
    }
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
