import {
  Component,
  DestroyRef,
  inject,
  signal,
  OnInit,
  AfterViewInit,
  ElementRef,
  ViewChild,
  NgZone,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../core/services/auth.service';

// Extend Window to allow the Telegram callback
declare global {
  interface Window {
    onTelegramAuth: (user: Record<string, string>) => void;
  }
}

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    <div class="login-page">
      <div class="login-bg-pattern"></div>
      <div class="login-container">
        <div class="login-brand">
          <div class="brand-icon">
            <mat-icon>catching_pokemon</mat-icon>
          </div>
          <h1 class="brand-title">PoGO Alerts Network</h1>
          <p class="brand-description">DM Alerts Configuration</p>
        </div>

        <mat-card class="login-card">
          <mat-card-content>
            <h2 class="login-heading">Sign In</h2>
            <p class="login-subheading">Sign in to manage your Pokemon GO notification alarms.</p>

            <div class="login-buttons">
              <button
                class="auth-btn discord-btn"
                (click)="loginWithDiscord()"
                [disabled]="loading()"
              >
                <svg class="auth-icon" viewBox="0 0 24 24" width="24" height="24">
                  <path fill="currentColor" d="M20.317 4.37a19.791 19.791 0 0 0-4.885-1.515.074.074 0 0 0-.079.037c-.21.375-.444.864-.608 1.25a18.27 18.27 0 0 0-5.487 0 12.64 12.64 0 0 0-.617-1.25.077.077 0 0 0-.079-.037A19.736 19.736 0 0 0 3.677 4.37a.07.07 0 0 0-.032.027C.533 9.046-.32 13.58.099 18.057a.082.082 0 0 0 .031.057 19.9 19.9 0 0 0 5.993 3.03.078.078 0 0 0 .084-.028 14.09 14.09 0 0 0 1.226-1.994.076.076 0 0 0-.041-.106 13.107 13.107 0 0 1-1.872-.892.077.077 0 0 1-.008-.128 10.2 10.2 0 0 0 .372-.292.074.074 0 0 1 .077-.01c3.928 1.793 8.18 1.793 12.062 0a.074.074 0 0 1 .078.01c.12.098.246.198.373.292a.077.077 0 0 1-.006.127 12.299 12.299 0 0 1-1.873.892.077.077 0 0 0-.041.107c.36.698.772 1.362 1.225 1.993a.076.076 0 0 0 .084.028 19.839 19.839 0 0 0 6.002-3.03.077.077 0 0 0 .032-.054c.5-5.177-.838-9.674-3.549-13.66a.061.061 0 0 0-.031-.03zM8.02 15.33c-1.183 0-2.157-1.085-2.157-2.419 0-1.333.956-2.419 2.157-2.419 1.21 0 2.176 1.096 2.157 2.42 0 1.333-.956 2.418-2.157 2.418zm7.975 0c-1.183 0-2.157-1.085-2.157-2.419 0-1.333.955-2.419 2.157-2.419 1.21 0 2.176 1.096 2.157 2.42 0 1.333-.946 2.418-2.157 2.418z"/>
                </svg>
                Sign in with Discord
              </button>

              @if (telegramEnabled()) {
                <div class="divider">
                  <span>or</span>
                </div>
                <div #telegramContainer class="telegram-widget"></div>
              }

              @if (loading()) {
                <div class="loading">
                  <mat-spinner diameter="32"></mat-spinner>
                  <span>Authenticating...</span>
                </div>
              }

              @if (error()) {
                <div class="error-message">
                  <mat-icon>error_outline</mat-icon>
                  <span>{{ error() }}</span>
                </div>
              }
            </div>
          </mat-card-content>
        </mat-card>

        <p class="login-footer">Manage alarms for Pokemon, Raids, Quests, and more</p>
      </div>
    </div>
  `,
  styles: [
    `
      .login-page {
        position: relative;
        display: flex;
        justify-content: center;
        align-items: center;
        min-height: 100vh;
        overflow: hidden;
        background: linear-gradient(135deg, #1a237e 0%, #1565c0 40%, #0288d1 70%, #00bcd4 100%);
      }

      .login-bg-pattern {
        position: absolute;
        inset: 0;
        opacity: 0.06;
        background-image:
          radial-gradient(circle at 20% 30%, #fff 2px, transparent 2px),
          radial-gradient(circle at 80% 70%, #fff 1.5px, transparent 1.5px),
          radial-gradient(circle at 50% 10%, #fff 3px, transparent 3px),
          radial-gradient(circle at 10% 80%, #fff 2px, transparent 2px),
          radial-gradient(circle at 90% 20%, #fff 1px, transparent 1px),
          radial-gradient(circle at 40% 60%, #fff 2.5px, transparent 2.5px),
          radial-gradient(circle at 70% 40%, #fff 1.5px, transparent 1.5px);
        background-size: 200px 200px;
        animation: drift 20s linear infinite;
      }

      @keyframes drift {
        0% { transform: translate(0, 0); }
        100% { transform: translate(200px, 200px); }
      }

      .login-container {
        position: relative;
        z-index: 1;
        display: flex;
        flex-direction: column;
        align-items: center;
        padding: 24px;
        width: 100%;
        max-width: 440px;
      }

      .login-brand {
        text-align: center;
        margin-bottom: 32px;
        color: #fff;
      }

      .brand-icon {
        width: 72px;
        height: 72px;
        margin: 0 auto 16px;
        border-radius: 50%;
        background: rgba(255, 255, 255, 0.15);
        backdrop-filter: blur(8px);
        display: flex;
        align-items: center;
        justify-content: center;
        border: 2px solid rgba(255, 255, 255, 0.3);
      }

      .brand-icon mat-icon {
        font-size: 36px;
        width: 36px;
        height: 36px;
        color: #fff;
      }

      .brand-title {
        font-size: 32px;
        font-weight: 300;
        margin: 0 0 8px;
        letter-spacing: 1px;
      }

      .brand-description {
        font-size: 15px;
        margin: 0;
        opacity: 0.85;
      }

      .login-card {
        width: 100%;
        border-radius: 16px !important;
        box-shadow: 0 12px 40px rgba(0, 0, 0, 0.25) !important;
      }

      .login-heading {
        font-size: 22px;
        font-weight: 500;
        margin: 8px 0 4px;
        text-align: center;
      }

      .login-subheading {
        font-size: 14px;
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
        text-align: center;
        margin: 0 0 20px;
      }

      .login-buttons {
        display: flex;
        flex-direction: column;
        gap: 16px;
      }

      .auth-btn {
        display: flex;
        align-items: center;
        justify-content: center;
        gap: 12px;
        height: 48px;
        border: none;
        border-radius: 8px;
        font-size: 15px;
        font-weight: 500;
        cursor: pointer;
        transition: transform 0.15s, box-shadow 0.15s, filter 0.15s;
        width: 100%;
      }

      .auth-btn:hover:not(:disabled) {
        transform: translateY(-1px);
        box-shadow: 0 4px 16px rgba(0, 0, 0, 0.2);
      }

      .auth-btn:active:not(:disabled) {
        transform: translateY(0);
      }

      .auth-btn:disabled {
        opacity: 0.6;
        cursor: not-allowed;
      }

      .auth-icon {
        flex-shrink: 0;
      }

      .discord-btn {
        background-color: #5865F2;
        color: #fff;
      }

      .discord-btn:hover:not(:disabled) {
        background-color: #4752c4;
      }

      .divider {
        display: flex;
        align-items: center;
        text-align: center;
        gap: 12px;
      }
      .divider::before,
      .divider::after {
        content: '';
        flex: 1;
        border-bottom: 1px solid var(--divider, rgba(0, 0, 0, 0.12));
      }
      .divider span {
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
        font-size: 13px;
        text-transform: uppercase;
        letter-spacing: 0.5px;
      }

      .telegram-widget {
        display: flex;
        justify-content: center;
        min-height: 40px;
      }

      .loading {
        display: flex;
        align-items: center;
        justify-content: center;
        gap: 12px;
        padding: 8px;
      }

      .error-message {
        display: flex;
        align-items: center;
        justify-content: center;
        gap: 8px;
        color: #c62828;
        text-align: center;
        padding: 12px;
        border-radius: 8px;
        background-color: rgba(198, 40, 40, 0.08);
        font-size: 14px;
      }

      .error-message mat-icon {
        font-size: 20px;
        width: 20px;
        height: 20px;
        flex-shrink: 0;
      }

      .login-footer {
        margin-top: 24px;
        color: rgba(255, 255, 255, 0.6);
        font-size: 13px;
        text-align: center;
      }

      @media (max-width: 480px) {
        .login-container {
          padding: 16px;
        }
        .brand-title {
          font-size: 26px;
        }
      }
    `,
  ],
})
export class LoginComponent implements OnInit, AfterViewInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly ngZone = inject(NgZone);

  @ViewChild('telegramContainer') telegramContainer?: ElementRef<HTMLDivElement>;

  protected readonly telegramEnabled = signal(false);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  private telegramBotUsername = '';

  ngOnInit(): void {
    // If already logged in, redirect
    if (this.auth.isLoggedIn()) {
      this.router.navigate(['/dashboard']);
      return;
    }

    // Check if Telegram auth is enabled
    this.auth.getTelegramConfig().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (config) => {
        this.telegramEnabled.set(config.enabled);
        this.telegramBotUsername = config.botUsername;
        if (config.enabled) {
          // Need to wait for view to init before loading widget
          setTimeout(() => this.loadTelegramWidget(), 0);
        }
      },
      error: () => {
        // Telegram config not available, just show Discord
      },
    });
  }

  ngAfterViewInit(): void {
    // Widget will be loaded after telegram config is fetched
  }

  loginWithDiscord(): void {
    this.loading.set(true);
    this.error.set(null);
    // Delegate to AuthService which fetches the OAuth URL from the API
    this.auth.loginWithDiscord();
  }

  private loadTelegramWidget(): void {
    if (!this.telegramContainer?.nativeElement || !this.telegramBotUsername) return;

    // Set up global callback for Telegram widget
    window.onTelegramAuth = (user: Record<string, string>) => {
      this.ngZone.run(() => this.handleTelegramAuth(user));
    };

    const script = document.createElement('script');
    script.src = 'https://telegram.org/js/telegram-widget.js?22';
    script.setAttribute('data-telegram-login', this.telegramBotUsername);
    script.setAttribute('data-size', 'large');
    script.setAttribute('data-onauth', 'onTelegramAuth(user)');
    script.setAttribute('data-request-access', 'write');
    script.async = true;

    this.telegramContainer.nativeElement.appendChild(script);
  }

  private handleTelegramAuth(telegramData: Record<string, string>): void {
    this.loading.set(true);
    this.error.set(null);

    this.auth.loginWithTelegram(telegramData).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.router.navigate(['/dashboard']),
      error: (err) => {
        this.loading.set(false);
        this.error.set(err.error?.error || 'Telegram authentication failed. Please try again.');
      },
    });
  }
}
