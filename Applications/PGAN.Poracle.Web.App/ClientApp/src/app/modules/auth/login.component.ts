import { Component, DestroyRef, inject, signal, OnInit, ElementRef, ViewChild, NgZone } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, Router } from '@angular/router';

import { AuthService } from '../../core/services/auth.service';

// Extend Window to allow the Telegram callback
declare global {
  interface Window {
    onTelegramAuth: (user: Record<string, string>) => void;
  }
}

@Component({
  imports: [MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule],
  selector: 'app-login',
  standalone: true,
  styleUrl: './login.component.scss',
  templateUrl: './login.component.html',
})
export class LoginComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly ngZone = inject(NgZone);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private telegramBotUsername = '';

  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);
  @ViewChild('telegramContainer') telegramContainer?: ElementRef<HTMLDivElement>;

  protected readonly telegramEnabled = signal(false);

  loginWithDiscord(): void {
    this.loading.set(true);
    this.error.set(null);
    // Delegate to AuthService which fetches the OAuth URL from the API
    this.auth.loginWithDiscord();
  }

  ngOnInit(): void {
    // Show error from query params (e.g. redirected from failed OAuth)
    const errorCode = this.route.snapshot.queryParamMap.get('error');
    if (errorCode) {
      const messages: Record<string, string> = {
        discord_user_fetch_failed: 'Could not retrieve your Discord profile. Please try again.',
        missing_code: 'Discord authentication was cancelled or failed.',
        missing_required_role: 'You do not have the required Discord role to access this site.',
        not_in_guild: 'You must be a member of the Discord server to access this site.',
        role_check_failed: 'Unable to verify your Discord roles. Please try again later.',
        token_exchange_failed: 'Discord authentication failed. Please try again.',
        user_not_registered: 'Your account is not registered with Poracle. Please register using the bot first.',
      };
      this.error.set(messages[errorCode] || `Login failed: ${errorCode}`);
    }

    // If already logged in, redirect
    if (this.auth.isLoggedIn()) {
      this.router.navigate(['/dashboard']);
      return;
    }

    // Check if Telegram auth is enabled
    this.auth
      .getTelegramConfig()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          // Telegram config not available, just show Discord
        },
        next: config => {
          this.telegramEnabled.set(config.enabled);
          this.telegramBotUsername = config.botUsername;
          if (config.enabled) {
            // Need to wait for view to init before loading widget
            setTimeout(() => this.loadTelegramWidget(), 0);
          }
        },
      });
  }

  private handleTelegramAuth(telegramData: Record<string, string>): void {
    this.loading.set(true);
    this.error.set(null);

    this.auth
      .loginWithTelegram(telegramData)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: err => {
          this.loading.set(false);
          this.error.set(err.error?.error || 'Telegram authentication failed. Please try again.');
        },
        next: () => this.router.navigate(['/dashboard']),
      });
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
}
