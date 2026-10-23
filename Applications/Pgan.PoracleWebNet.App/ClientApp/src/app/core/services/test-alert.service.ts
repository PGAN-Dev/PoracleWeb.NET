import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslateService } from '@ngx-translate/core';
import { EMPTY, catchError, finalize, tap } from 'rxjs';

import { AuthService } from './auth.service';
import { ConfigService } from './config.service';
import { isPersonType } from '../../shared/utils/human-kind';

const COOLDOWN_MS = 15_000;

@Injectable({ providedIn: 'root' })
export class TestAlertService {
  private readonly auth = inject(AuthService);
  private readonly config = inject(ConfigService);
  /** Map of "type:uid" → timestamp when cooldown expires */
  private readonly cooldowns = signal<Map<string, number>>(new Map());
  private readonly http = inject(HttpClient);

  /** Set of "type:uid" keys currently in-flight */
  private readonly sending = signal<Set<string>>(new Set());

  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  isCoolingDown(type: string, uid: number): boolean {
    const key = `${type}:${uid}`;
    const expires = this.cooldowns().get(key);
    if (!expires) return false;
    return Date.now() < expires;
  }

  isSending(type: string, uid: number): boolean {
    return this.sending().has(`${type}:${uid}`);
  }

  sendTestAlert(type: string, uid: number): void {
    const key = `${type}:${uid}`;

    if (this.isCoolingDown(type, uid) || this.isSending(type, uid)) {
      return;
    }

    // Mark as sending
    const sendingSet = new Set(this.sending());
    sendingSet.add(key);
    this.sending.set(sendingSet);

    this.http
      .post<{ status: string; message: string }>(`${this.config.apiHost}/api/test-alert/${type}/${uid}`, {})
      .pipe(
        tap(() => {
          this.snackBar.open(this.successMessage(), 'OK', { duration: 4000 });
          this.startCooldown(key);
        }),
        catchError(err => {
          // 501 is returned for alarm types that have no upstream /api/test surface
          // (currently: nest). The backend ships a human-readable reason on the response
          // body — surface it so the user knows why the test didn't go out, rather than
          // seeing a generic retry message and burning rate-limit quota.
          //
          // The error interceptor stays silent for this route, so this is the only message the user
          // gets and it has to cover every status -- including the feature-disabled 403 that used to
          // arrive as the interceptor's toast.
          const serverMessage = err?.error?.error;
          // 502: PoracleNG answered and said why it couldn't deliver ("message delivery not
          // configured ..."), which tells the user a retry won't help. Shown with its reason, longer.
          const refused = err.status === 502 && typeof serverMessage === 'string' && serverMessage.length > 0;
          const message = refused
            ? this.translate.instant('TEST_ALERT.REFUSED', { reason: serverMessage })
            : err.status === 429
              ? this.translate.instant('TEST_ALERT.RATE_LIMITED')
              : err.status === 404
                ? this.translate.instant('TEST_ALERT.NOT_FOUND')
                : err.status === 501
                  ? (serverMessage ?? this.translate.instant('TEST_ALERT.UNSUPPORTED'))
                  : err.status === 403 && err?.error?.disableKey
                    ? this.translate.instant('ERROR.FEATURE_DISABLED')
                    : this.translate.instant('TEST_ALERT.FAILED');
          this.snackBar.open(message, 'OK', { duration: refused ? 8000 : 4000 });
          // Start the cooldown on unsupported-type errors so the user can't spam-click
          // the button and waste rate-limit quota on a known no-op.
          if (err.status === 501) {
            this.startCooldown(key);
          }
          return EMPTY;
        }),
        finalize(() => this.clearSending(key)),
      )
      .subscribe();
  }

  private clearSending(key: string): void {
    const set = new Set(this.sending());
    set.delete(key);
    this.sending.set(set);
  }

  private startCooldown(key: string): void {
    const map = new Map(this.cooldowns());
    map.set(key, Date.now() + COOLDOWN_MS);
    this.cooldowns.set(map);

    // Clean up expired entry after cooldown period
    setTimeout(() => {
      const current = new Map(this.cooldowns());
      current.delete(key);
      this.cooldowns.set(current);
    }, COOLDOWN_MS);
  }

  /** A destination's alert posts where the destination is, so "Check your DMs" would point the wrong way. */
  private successMessage(): string {
    const user = this.auth.user();
    if (this.auth.isImpersonating() && user && !isPersonType(user.type)) {
      return this.translate.instant('TEST_ALERT.SUCCESS_DESTINATION', { name: user.username });
    }
    return this.translate.instant('TEST_ALERT.SUCCESS');
  }
}
