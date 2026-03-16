import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, ReplaySubject, tap, firstValueFrom } from 'rxjs';
import { ConfigService } from './config.service';
import { UserInfo, LoginResponse, TelegramConfig } from '../models';

const TOKEN_KEY = 'poracle_token';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private readonly router = inject(Router);

  private readonly currentUser = signal<UserInfo | null>(null);
  private readonly userLoaded$ = new ReplaySubject<UserInfo | null>(1);

  readonly user = this.currentUser.asReadonly();
  readonly isLoggedIn = computed(() => !!this.currentUser());
  readonly isAdmin = computed(() => this.currentUser()?.isAdmin ?? false);

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
    this.http
      .get<{ url: string }>(`${this.config.apiHost}/api/auth/discord/login`)
      .subscribe({
        next: (res) => (window.location.href = res.url),
        error: (err) => console.error('Discord login failed:', err),
      });
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

  loadCurrentUser(): void {
    this.http.get<UserInfo>(`${this.config.apiHost}/api/auth/me`).subscribe({
      next: (user) => {
        this.currentUser.set(user);
        this.userLoaded$.next(user);
      },
      error: (err) => {
        if (err.status === 401) {
          localStorage.removeItem(TOKEN_KEY);
          this.currentUser.set(null);
        }
        this.userLoaded$.next(null);
      },
    });
  }

  logout(): void {
    localStorage.removeItem(TOKEN_KEY);
    this.currentUser.set(null);
    this.router.navigate(['/login']);
  }

  getToken(): string | null {
    return localStorage.getItem(TOKEN_KEY);
  }

  isAuthenticated(): boolean {
    return !!this.getToken();
  }

  private handleAuthResponse(res: LoginResponse): void {
    localStorage.setItem(TOKEN_KEY, res.token);
    this.currentUser.set(res.user);
    this.userLoaded$.next(res.user);
  }
}
