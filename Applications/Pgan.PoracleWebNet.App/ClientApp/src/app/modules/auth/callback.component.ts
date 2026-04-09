import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';

import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';

@Component({
  imports: [MatProgressSpinnerModule, MatButtonModule, MatIconModule, RouterLink, TranslateModule],
  selector: 'app-callback',
  standalone: true,
  styleUrl: './callback.component.scss',
  templateUrl: './callback.component.html',
})
export class CallbackComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly i18n = inject(I18nService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    // The API redirects here with #token=JWT (fragment) or ?error=... (query param)
    const fragment = this.route.snapshot.fragment || '';
    const fragmentParams = new URLSearchParams(fragment);
    const token = fragmentParams.get('token');
    const errorParam = this.route.snapshot.queryParamMap.get('error');

    if (errorParam) {
      const messageKeys: Record<string, string> = {
        discord_user_fetch_failed: 'AUTH.ERR_DISCORD_FETCH',
        missing_code: 'AUTH.ERR_MISSING_CODE',
        token_exchange_failed: 'AUTH.ERR_TOKEN_EXCHANGE',
        user_not_registered: 'AUTH.ERR_NOT_REGISTERED',
      };
      const key = messageKeys[errorParam];
      this.error.set(key ? this.i18n.instant(key) : this.i18n.instant('AUTH.ERR_GENERIC', { error: errorParam }));
    } else if (token) {
      this.auth.handleTokenFromCallback(token);
    } else {
      this.error.set(this.i18n.instant('AUTH.ERR_NO_TOKEN'));
    }
  }
}
