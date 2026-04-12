import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal, computed } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, ReplaySubject, tap, firstValueFrom } from 'rxjs';

import { ConfigService } from './config.service';
import { SettingsService } from './settings.service';
import { UserInfo, LoginResponse, TelegramConfig } from '../models';

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
  private readonly userLoaded$ = new ReplaySubject<UserInfo | null>(1);

  readonly hasManagedWebhooks = computed(() => (this.currentUser()?.managedWebhooks?.length ?? 0) > 0);
  readonly isAdmin = computed(() => this.currentUser()?.isAdmin ?? false);
  readonly isImpersonating = this._isImpersonating.asReadonly();
  readonly isLoggedIn = computed(() => !!this.currentUser());
  readonly managedWebhooks = computed(() => this.currentUser()?.managedWebhooks ?? []);
  readonly profileResynced = this._profileResynced.asReadonly();
  readonly user = this.currentUser.asReadonly();

  constructor() {
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

  getTelegramConfig(): Observable<TelegramConfig> {
    return this.http.get<TelegramConfig>(`${this.config.apiHost}/api/auth/telegram/config`);
  }

  getToken(): string | null {
    return localStorage.getItem(TOKEN_KEY);
  }

  async handleTokenFromCallback(token: string): Promise<void> {
    localStorage.setItem(TOKEN_KEY, token);
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

  loginWithTelegram(telegramData: Record<string, string>): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${this.config.apiHost}/api/auth/telegram/verify`, telegramData)
      .pipe(tap(res => this.handleAuthResponse(res)));
  }

  logout(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(ADMIN_TOKEN_KEY);
    this._isImpersonating.set(false);
    this.currentUser.set(null);
    this.router.navigate(['/login']);
  }

  /** Store a new JWT token (e.g. after profile switch). */
  setToken(token: string): void {
    localStorage.setItem(TOKEN_KEY, token);
  }

  /** Restore the admin's original token. */
  async stopImpersonating(): Promise<void> {
    const adminToken = localStorage.getItem(ADMIN_TOKEN_KEY);
    if (adminToken) {
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
    localStorage.setItem(TOKEN_KEY, res.token);
    this.currentUser.set(res.user);
    this.userLoaded$.next(res.user);
  }
}
