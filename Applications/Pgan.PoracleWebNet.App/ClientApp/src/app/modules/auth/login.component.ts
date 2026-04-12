import { Component, computed, DestroyRef, ElementRef, inject, NgZone, OnInit, signal, ViewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { catchError, forkJoin, of, timeout } from 'rxjs';

import { AuthProviders } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { SettingsService } from '../../core/services/settings.service';

/** Timeout for provider config and public settings fetches on the login page. */
const LOGIN_FETCH_TIMEOUT_MS = 10_000;

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
  private telegramWidgetLoaded = false;

  /** Whether the providers config has finished loading (success or failure). */
  protected readonly configLoaded = signal(false);

  /** Whether Discord is configured in the server's .env / appsettings. */
  protected readonly discordConfigured = signal(false);

  /**
   * Whether Discord login is enabled by the admin (site setting `enable_discord`).
   * When false but configured, the button still renders but the backend will reject
   * non-admin users after authentication.
   */
  protected readonly discordEnabledByAdmin = signal(true);

  /** Computed: can the user actually click the Discord button? Configured + admin-enabled. */
  protected readonly discordActive = computed(() => this.discordConfigured() && this.discordEnabledByAdmin());

  /** Computed: should the Discord button be shown at all? Only if configured in .env. */
  protected readonly discordVisible = computed(() => this.discordConfigured());

  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);

  protected readonly signupUrl = computed(() => {
    return this.settingsService.siteSettings()['signup_url'] || null;
  });

  protected readonly siteTitle = computed(() => this.settingsService.siteSettings()['custom_title'] || '');

  /** Whether Telegram is configured in the server's .env / appsettings. */
  protected readonly telegramConfigured = signal(false);

  /**
   * Whether Telegram login is enabled by the admin (site setting `enable_telegram`).
   * When false but configured, the widget still renders but the backend will reject
   * non-admin users after authentication.
   */
  protected readonly telegramEnabledByAdmin = signal(true);

  /** Computed: can the user actually use Telegram login without admin rejection? */
  protected readonly telegramActive = computed(() => this.telegramConfigured() && this.telegramEnabledByAdmin());

  /** Computed: should the Telegram section be shown at all? Only if configured in .env. */
  protected readonly telegramVisible = computed(() => this.telegramConfigured());

  /**
   * ViewChild setter — fires automatically when the #telegramContainer element enters
   * the DOM (i.e., after Angular renders the @if (telegramVisible()) block). This avoids
   * the unreliable setTimeout + detectChanges pattern for elements inside @if blocks.
   */
  @ViewChild('telegramContainer') set telegramContainerRef(el: ElementRef<HTMLDivElement> | undefined) {
    if (el && this.telegramBotUsername && !this.telegramWidgetLoaded) {
      this.telegramWidgetLoaded = true;
      this.loadTelegramWidget(el.nativeElement);
    }
  }

  loginWithDiscord(): void {
    this.loading.set(true);
    this.error.set(null);
    // Delegate to AuthService which fetches the OAuth URL from the API
    this.auth.loginWithDiscord();
  }

  ngOnInit(): void {
    // Load public site settings (custom_title, signup_url) and provider config in parallel.
    // Both calls use a 10s timeout and fallback to defaults on error so the login page
    // never gets stuck in an unrecoverable state.
    forkJoin({
      providers: this.auth.getProviders().pipe(
        timeout(LOGIN_FETCH_TIMEOUT_MS),
        catchError(() => of(null)),
      ),
      settings: this.settingsService.loadPublic().pipe(
        timeout(LOGIN_FETCH_TIMEOUT_MS),
        catchError(() => of([])),
      ),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(({ providers }) => {
        if (providers) {
          this.applyProviders(providers);
        } else {
          // API unreachable — default to showing Discord (safe default) so user isn't locked out
          this.discordConfigured.set(true);
          this.discordEnabledByAdmin.set(true);
        }
        this.configLoaded.set(true);
      });

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
        telegram_disabled: 'AUTH.ERR_TELEGRAM_DISABLED',
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
  }

  private applyProviders(providers: AuthProviders): void {
    // Discord
    this.discordConfigured.set(providers.discord.configured);
    this.discordEnabledByAdmin.set(providers.discord.enabledByAdmin);

    // Telegram — set botUsername before signals so the ViewChild setter has it
    // when Angular renders the @if block and triggers the setter.
    this.telegramBotUsername = providers.telegram.botUsername;
    this.telegramConfigured.set(providers.telegram.configured);
    this.telegramEnabledByAdmin.set(providers.telegram.enabledByAdmin);
  }

  private handleTelegramAuth(telegramData: Record<string, string>): void {
    this.loading.set(true);
    this.error.set(null);

    this.auth
      .loginWithTelegram(telegramData)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.loading.set(false);
          this.error.set('AUTH.ERR_TELEGRAM_FAILED');
        },
        next: () => this.router.navigate(['/dashboard']),
      });
  }

  private loadTelegramWidget(container: HTMLDivElement): void {
    if (!this.telegramBotUsername) return;

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

    container.appendChild(script);
  }
}
