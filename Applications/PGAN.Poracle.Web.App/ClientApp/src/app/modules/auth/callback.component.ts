import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { AuthService } from '../../core/services/auth.service';

@Component({
  imports: [MatProgressSpinnerModule, MatButtonModule, MatIconModule, RouterLink],
  selector: 'app-callback',
  standalone: true,
  styleUrl: './callback.component.scss',
  templateUrl: './callback.component.html',
})
export class CallbackComponent implements OnInit {
  private readonly auth = inject(AuthService);
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
      const messages: Record<string, string> = {
        discord_user_fetch_failed: 'Failed to retrieve Discord user information.',
        missing_code: 'No authorization code received from Discord.',
        token_exchange_failed: 'Failed to exchange authorization code with Discord.',
        user_not_registered: 'Your account is not registered in Poracle.',
      };
      this.error.set(messages[errorParam] || `Authentication error: ${errorParam}`);
    } else if (token) {
      this.auth.handleTokenFromCallback(token);
    } else {
      this.error.set('No authentication token received.');
    }
  }
}
