import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { catchError, forkJoin, of, timeout } from 'rxjs';

import { AuthProviders } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { SettingsService } from '../../core/services/settings.service';
import { clearTelegramReturn, readTelegramReturn } from '../../shared/utils/telegram-return';
import { TelegramLoginButtonComponent } from '../../shared/components/telegram-login-button/telegram-login-button.component';

/** Timeout for provider config and public settings fetches on the login page. */
const LOGIN_FETCH_TIMEOUT_MS = 10_000;

@Component({
  imports: [MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule, TelegramLoginButtonComponent, TranslatePipe],
  selector: 'app-login',
  standalone: true,
  styleUrl: './login.component.scss',
  templateUrl: './login.component.html',
})
export class LoginComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly settingsService = inject(SettingsService);

  /** Replaces the generic one-line error message with the richer disabled-account block. */
  protected readonly accountDisabled = signal(false);

  /** Whether the providers config has finished loading (success or failure). */
  protected readonly configLoaded = signal(false);

  /** Set alongside `accountDisabled()`; the link the disabled-account message offers, if one is set. */
  protected readonly disabledSupportUrl = signal<string | null>(null);

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

  /** Whether a generic external OIDC provider is configured in the server's .env / appsettings. */
  protected readonly oidcConfigured = signal(false);

  /** Whether OIDC login is enabled by the admin (site setting `enable_oidc`). */
  protected readonly oidcEnabledByAdmin = signal(true);

  /** Computed: can the user actually use OIDC login without admin rejection? */
  protected readonly oidcActive = computed(() => this.oidcConfigured() && this.oidcEnabledByAdmin());

  /** Display name of the configured OIDC provider, shown on the button. */
  protected readonly oidcProviderName = signal('');

  /** Computed: should the OIDC button be shown at all? Only if configured in .env. */
  protected readonly oidcVisible = computed(() => this.oidcConfigured());

  /** True when arriving via logout (?loggedout=1): show the signed-out panel, no auto-redirect. */
  protected readonly signedOut = signal(false);

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

  /** Where Telegram sends the browser back to after a sign-in. */
  protected readonly telegramAuthUrl = `${window.location.origin}/login`;

  /** The bot the login widget is built for. Set before `telegramConfigured`, so the widget never renders without it. */
  protected readonly telegramBotUsername = signal('');

  /** Computed: should the Telegram section be shown at all? Only if configured in .env. */
  protected readonly telegramVisible = computed(() => this.telegramConfigured());

  protected handleTelegramAuth(telegramData: Record<string, string>): void {
    this.loading.set(true);
    this.error.set(null);

    this.auth
      .loginWithTelegram(telegramData)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: err => {
          this.loading.set(false);
          this.error.set(err?.error?.code === 'destination_account' ? 'AUTH.ERR_DESTINATION_ACCOUNT' : 'AUTH.ERR_TELEGRAM_FAILED');
        },
        next: () => this.router.navigate(['/dashboard']),
      });
  }

  loginWithDiscord(): void {
    this.loading.set(true);
    this.error.set(null);
    // Delegate to AuthService which fetches the OAuth URL from the API
    this.auth.loginWithDiscord();
  }

  loginWithOidc(): void {
    this.loading.set(true);
    this.error.set(null);
    this.auth.loginWithOidc();
  }

  ngOnInit(): void {
    // Telegram's redirect sign-in returns here with the signed fields in the query.
    const telegramReturn = readTelegramReturn(new URLSearchParams(window.location.search));
    if (telegramReturn) {
      clearTelegramReturn();
      this.handleTelegramAuth(telegramReturn);
    }

    // Parse any auth error from the URL fragment (e.g. /login#error=missing_required_role)
    // up front, so the provider-config handler below can decide whether to auto-redirect
    // to an external OIDC provider.
    const fragment = window.location.hash?.substring(1) ?? '';
    const fragmentParams = new URLSearchParams(fragment);
    const errorCode = fragmentParams.get('error');

    // /login?loggedout=1 (set by logout / the OIDC end-session return) shows the signed-out
    // panel and SUPPRESSES the OIDC auto-redirect, so the user isn't bounced straight back
    // into the provider and silently re-logged in.
    const loggedOut = this.route.snapshot.queryParamMap.get('loggedout') === '1';
    this.signedOut.set(loggedOut);

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

        // When a generic external OIDC provider is the active sign-in method, skip the
        // local login page and send the user straight to the provider. Guarded so we
        // never loop: not after an auth-error bounce and not when already logged in.
        if (this.oidcActive() && !errorCode && !loggedOut && !this.auth.isLoggedIn()) {
          this.auth.loginWithOidc();
          return;
        }

        this.configLoaded.set(true);
      });

    // Show error from URL fragment (e.g. /login#error=missing_required_role)
    if (errorCode === 'account_disabled') {
      // Not one of errorKeys below: it renders its own richer block (BANNER.DISABLED_ACCOUNT, already
      // translated everywhere as part of #910) rather than a one-line message, since this is the one
      // place that explanation reaches someone who has never been able to sign in to see it. See #911.
      this.accountDisabled.set(true);
      this.disabledSupportUrl.set(fragmentParams.get('support_url'));
      localStorage.removeItem('poracle_token');
      localStorage.removeItem('poracle_admin_token');
    } else if (errorCode) {
      const errorKeys: Record<string, string> = {
        oidc_disabled: 'AUTH.ERR_OIDC_DISABLED',
        oidc_no_identity: 'AUTH.ERR_OIDC_NO_IDENTITY',
        oidc_token_exchange_failed: 'AUTH.ERR_OIDC_TOKEN_EXCHANGE',
        oidc_userinfo_failed: 'AUTH.ERR_OIDC_USERINFO',
        destination_account: 'AUTH.ERR_DESTINATION_ACCOUNT',
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
      // An unknown code is shown as a sentence, never as itself: "weird_code" told the reader nothing.
      this.error.set(Object.hasOwn(errorKeys, errorCode) ? errorKeys[errorCode] : 'AUTH.ERR_UNKNOWN');
      // Clear any stale token without navigating (logout() would redirect away)
      localStorage.removeItem('poracle_token');
      localStorage.removeItem('poracle_admin_token');
    }

    // Keyed on the token, not on a currentUser that outlived it. After a 401 the user object was still
    // set, so login sent the user to /dashboard, authGuard found no token and sent them back, and the
    // OIDC auto-redirect below never ran. See #628.
    if (!errorCode && this.auth.isAuthenticated()) {
      this.router.navigate(['/dashboard']);
      return;
    }
  }

  /**
   * Sign in again from the signed-out panel. In OIDC mode this re-initiates the provider
   * flow (full credentials if single logout ended the provider session, silent otherwise);
   * in local mode it simply reveals the Discord/Telegram buttons.
   */
  signInAgain(): void {
    this.signedOut.set(false);
    if (this.oidcActive()) {
      this.loginWithOidc();
    }
  }

  private applyProviders(providers: AuthProviders): void {
    // Discord
    this.discordConfigured.set(providers.discord.configured);
    this.discordEnabledByAdmin.set(providers.discord.enabledByAdmin);

    // Telegram — set botUsername before signals so the ViewChild setter has it
    // when Angular renders the @if block and triggers the setter.
    this.telegramBotUsername.set(providers.telegram.botUsername);
    this.telegramConfigured.set(providers.telegram.configured);
    this.telegramEnabledByAdmin.set(providers.telegram.enabledByAdmin);

    // OIDC (generic external SSO provider) — optional; older API responses omit it.
    if (providers.oidc) {
      this.oidcConfigured.set(providers.oidc.configured);
      this.oidcEnabledByAdmin.set(providers.oidc.enabledByAdmin);
      this.oidcProviderName.set(providers.oidc.providerName);
    }
  }
}
