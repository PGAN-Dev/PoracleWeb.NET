import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, ReplaySubject, tap, firstValueFrom } from 'rxjs';
import { ConfigService } from './config.service';
import { UserInfo, LoginResponse, TelegramConfig } from '../models';

const TOKEN_KEY = 'poracle_token';
const ADMIN_TOKEN_KEY = 'poracle_admin_token';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly router = inject(Router);

  private readonly currentUser = signal<UserInfo | null>(null);
  private readonly _isImpersonating = signal(!!localStorage.getItem(ADMIN_TOKEN_KEY));
  private readonly userLoaded$ = new ReplaySubject<UserInfo | null>(1);

  readonly user = this.currentUser.asReadonly();
  readonly isLoggedIn = computed(() => !!this.currentUser());
  readonly isAdmin = computed(() => this.currentUser()?.isAdmin ?? false);
  readonly isImpersonating = this._isImpersonating.asReadonly();
  readonly managedWebhooks = computed(() => this.currentUser()?.managedWebhooks ?? []);
  readonly hasManagedWebhooks = computed(() => (this.currentUser()?.managedWebhooks?.length ?? 0) > 0);

  constructor() {
    const token = localStorage.getItem(TOKEN_KEY);
    if (token) {
      this.loadCurrentUser();
    } else {
      this.userLoaded$.next(null);
    }
  }

  /** Returns a promise that resolves once the user has been loaded (or failed). */
  waitForUser(): Promise<UserInfo | null> {
    return firstValueFrom(this.userLoaded$);
  }

  loginWithDiscord(): void {
    window.location.href = `${this.config.apiHost}/api/auth/discord/login`;
  }

  handleTokenFromCallback(token: string): void {
    localStorage.setItem(TOKEN_KEY, token);
    this.loadCurrentUser();
    this.router.navigate(['/dashboard']);
  }

  loginWithTelegram(telegramData: Record<string, string>): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${this.config.apiHost}/api/auth/telegram/verify`, telegramData)
      .pipe(tap((res) => this.handleAuthResponse(res)));
  }

  getTelegramConfig(): Observable<TelegramConfig> {
    return this.http.get<TelegramConfig>(`${this.config.apiHost}/api/auth/telegram/config`);
  }

  loadCurrentUser(): Promise<UserInfo | null> {
    return new Promise((resolve) => {
      this.http.get<UserInfo>(`${this.config.apiHost}/api/auth/me`).subscribe({
        next: (user) => {
          this.currentUser.set(user);
          this.userLoaded$.next(user);
          resolve(user);
        },
        error: (err) => {
          if (err.status === 401) {
            localStorage.removeItem(TOKEN_KEY);
            this.currentUser.set(null);
          }
          this.userLoaded$.next(null);
          resolve(null);
        },
      });
    });
  }

  logout(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(ADMIN_TOKEN_KEY);
    this._isImpersonating.set(false);
    this.currentUser.set(null);
    this.router.navigate(['/login']);
  }

  toggleAlerts(): Observable<{ enabled: boolean }> {
    return this.http.post<{ enabled: boolean }>(`${this.config.apiHost}/api/auth/alerts/toggle`, {});
  }

  getToken(): string | null {
    return localStorage.getItem(TOKEN_KEY);
  }

  isAuthenticated(): boolean {
    return !!this.getToken();
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

  private handleAuthResponse(res: LoginResponse): void {
    localStorage.setItem(TOKEN_KEY, res.token);
    this.currentUser.set(res.user);
    this.userLoaded$.next(res.user);
  }
}
