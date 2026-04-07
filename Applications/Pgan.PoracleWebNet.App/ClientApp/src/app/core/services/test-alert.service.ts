import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { EMPTY, catchError, tap } from 'rxjs';

import { ConfigService } from './config.service';

const COOLDOWN_MS = 15_000;

@Injectable({ providedIn: 'root' })
export class TestAlertService {
  private readonly config = inject(ConfigService);
  /** Map of "type:uid" → timestamp when cooldown expires */
  private readonly cooldowns = signal<Map<string, number>>(new Map());
  private readonly http = inject(HttpClient);

  /** Set of "type:uid" keys currently in-flight */
  private readonly sending = signal<Set<string>>(new Set());

  private readonly snackBar = inject(MatSnackBar);

  isCoolingDown(type: string, uid: number): boolean {
    const key = `${type}:${uid}`;
    const expires = this.cooldowns().get(key);
    if (!expires) return false;
    if (Date.now() >= expires) {
      // Expired — clean up lazily
      const map = new Map(this.cooldowns());
      map.delete(key);
      this.cooldowns.set(map);
      return false;
    }
    return true;
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
          this.snackBar.open('Test alert sent! Check your DMs.', 'OK', { duration: 4000 });
          this.startCooldown(key);
          this.clearSending(key);
        }),
        catchError(err => {
          const message =
            err.status === 429
              ? 'Too many test alerts. Please wait a moment.'
              : err.status === 404
                ? 'Alarm not found — it may have been deleted.'
                : 'Failed to send test alert. Try again later.';
          this.snackBar.open(message, 'OK', { duration: 4000 });
          this.clearSending(key);
          return EMPTY;
        }),
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
  }
}
