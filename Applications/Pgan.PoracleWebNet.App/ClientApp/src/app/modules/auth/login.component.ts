import { Component, computed, DestroyRef, inject, signal, OnInit, ElementRef, ViewChild, NgZone } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';

import { AuthService } from '../../core/services/auth.service';
import { SettingsService } from '../../core/services/settings.service';

// Extend Window to allow the Telegram callback
declare global {
  interface Window {
    onTelegramAuth: (user: Record<string, string>) => void;
  }
}

@Component({
  imports: [MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule, TranslateModule],
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
  private readonly settingsService = inject(SettingsService);

  private telegramBotUsername = '';

  /**
   * Whether Discord login is enabled (PoracleWeb.NET site setting `enable_discord`).
   * This controls button visibility on the login page. The backend AuthController also
   * enforces this as defense-in-depth. Safe default: enabled when setting is absent.
   *
   * Note: This is a PoracleWeb.NET-only setting stored in `poracle_web.site_settings`.
   * It does NOT affect PoracleNG's Discord integration or webhook delivery.
   */
  protected readonly discordEnabled = computed(() => {
    const val = this.settingsService.siteSettings()['enable_discord'];
    return val?.toLowerCase() !== 'false';
  });

  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);

  protected readonly signupUrl = computed(() => {
    return this.settingsService.siteSettings()['signup_url'] || null;
  });

  protected readonly siteTitle = computed(() => this.settingsService.siteSettings()['custom_title'] || '');
  @ViewChild('telegramContainer') telegramContainer?: ElementRef<HTMLDivElement>;

  /**
   * Whether Telegram login is enabled. Driven by the `/api/auth/telegram/config` endpoint,
   * which combines two independent settings:
   *   1. `Telegram:Enabled` in appsettings.json (PoracleNG server config, requires restart)
   *   2. `enable_telegram` site setting in `poracle_web.site_settings` (PoracleWeb.NET, runtime toggle)
   * Both must be truthy for Telegram login to be available.
   *
   * Note: Neither setting affects PoracleNG's Telegram bot or DM delivery — only login.
   */
  protected readonly telegramEnabled = signal(false);

  loginWithDiscord(): void {
    this.loading.set(true);
    this.error.set(null);
    // Delegate to AuthService which fetches the OAuth URL from the API
    this.auth.loginWithDiscord();
  }

  ngOnInit(): void {
    this.settingsService.loadPublic().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();

    // Show error from URL fragment (e.g. /login#error=missing_required_role)
    const fragment = window.location.hash?.substring(1) ?? '';
    const fragmentParams = new URLSearchParams(fragment);
    const errorCode = fragmentParams.get('error');
    if (errorCode) {
      const errorKeys: Record<string, string> = {
        discord_disabled: 'AUTH.ERR_DISCORD_DISABLED',
        discord_user_fetch_failed: 'AUTH.ERR_DISCORD_FETCH',
        missing_code: 'AUTH.ERR_MISSING_CODE',
        missing_required_role: 'AUTH.ERR_MISSING_ROLE',
        not_in_guild: 'AUTH.ERR_NOT_IN_GUILD',
        role_check_failed: 'AUTH.ERR_ROLE_CHECK_FAILED',
        token_exchange_failed: 'AUTH.ERR_TOKEN_EXCHANGE',
        user_not_registered: 'AUTH.ERR_NOT_REGISTERED',
      };
      this.error.set(errorKeys[errorCode] || errorCode);
      // Clear any stale token without navigating (logout() would redirect away)
      localStorage.removeItem('poracle_token');
      localStorage.removeItem('poracle_admin_token');
    }

    // If already logged in and no error, redirect
    if (!errorCode && this.auth.isLoggedIn()) {
      this.router.navigate(['/dashboard']);
      return;
    }

    // Check if Telegram auth is enabled (combines PoracleNG config + PoracleWeb.NET site setting)
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
          this.error.set('AUTH.ERR_TELEGRAM_FAILED');
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
