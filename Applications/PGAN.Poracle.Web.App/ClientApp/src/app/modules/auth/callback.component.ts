import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-callback',
  standalone: true,
  imports: [MatProgressSpinnerModule, MatButtonModule, MatIconModule, RouterLink],
  template: `
    @if (error()) {
      <div class="callback-container">
        <div class="callback-error">
          <mat-icon class="error-icon">error_outline</mat-icon>
          <h2>Authentication Failed</h2>
          <p>{{ error() }}</p>
          <a mat-raised-button color="primary" routerLink="/login">
            <mat-icon>arrow_back</mat-icon>
            Back to Login
          </a>
        </div>
      </div>
    } @else {
      <div class="callback-container">
        <mat-spinner diameter="48"></mat-spinner>
        <p>Authenticating...</p>
      </div>
    }
  `,
  styles: [
    `
      .callback-container {
        display: flex;
        flex-direction: column;
        justify-content: center;
        align-items: center;
        min-height: 80vh;
        gap: 16px;
      }
      .callback-error {
        text-align: center;
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: 8px;
      }
      .error-icon {
        font-size: 48px;
        width: 48px;
        height: 48px;
        color: #f44336;
      }
    `,
  ],
})
export class CallbackComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);

  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    // The API redirects here with ?token=JWT after successful Discord OAuth
    const token = this.route.snapshot.queryParamMap.get('token');
    const errorParam = this.route.snapshot.queryParamMap.get('error');

    if (errorParam) {
      const messages: Record<string, string> = {
        missing_code: 'No authorization code received from Discord.',
        token_exchange_failed: 'Failed to exchange authorization code with Discord.',
        discord_user_fetch_failed: 'Failed to retrieve Discord user information.',
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
