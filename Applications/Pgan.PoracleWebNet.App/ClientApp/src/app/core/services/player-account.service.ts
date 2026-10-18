import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslateService } from '@ngx-translate/core';
import { Observable, catchError, map, of, tap, throwError } from 'rxjs';

import { ConfigService } from './config.service';
import { PlayerAccount } from '../models';

/**
 * The game accounts linked to a human, for personal shiny alerts.
 *
 * Without `userId` these are the signed-in trainer's own accounts; with it they are that human's, over
 * the admin routes. Accounts are addressed by the opaque `id` the server derives: the player id is never
 * answered, and the only way one is sent is an administrator's `add`. Player ids are case-sensitive, so
 * that one is never trimmed or lowercased.
 *
 * `accounts` is null until a load succeeds. That is deliberate: a failed read and an empty list are
 * different facts, and a caller that rendered "No game accounts linked yet" over a failed read would
 * tell a trainer their link had vanished.
 *
 * The service words its own failures (like MuteService), and its URLs are in the error interceptor's
 * silent list so a failure is toasted once.
 */
@Injectable({ providedIn: 'root' })
export class PlayerAccountService {
  private readonly accountsSignal = signal<PlayerAccount[] | null>(null);
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);
  private readonly loadFailedSignal = signal(false);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly accounts = this.accountsSignal.asReadonly();
  readonly loadFailed = this.loadFailedSignal.asReadonly();

  /** Links an account by player id (the administrator's route). The answer carries only the account's handle. */
  add(playerId: string, label: string, userId?: string): Observable<PlayerAccount> {
    return this.http.post<PlayerAccount>(this.collectionUrl(userId), { label: label || null, playerId }).pipe(
      tap(created => this.append(created)),
      catchError((err: HttpErrorResponse) => this.fail(err)),
    );
  }

  /** Reads the list. Answers null (and sets `loadFailed`) when it could not be read. */
  load(userId?: string): Observable<PlayerAccount[] | null> {
    return this.http.get<PlayerAccount[]>(this.collectionUrl(userId)).pipe(
      tap(accounts => {
        this.accountsSignal.set(accounts);
        this.loadFailedSignal.set(false);
      }),
      catchError(() => {
        this.accountsSignal.set(null);
        this.loadFailedSignal.set(true);
        return of(null);
      }),
    );
  }

  relabel(id: string, label: string, userId?: string): Observable<void> {
    return this.http.put<void>(this.itemUrl(id, userId), { label }).pipe(
      tap(() => {
        const current = this.accountsSignal();
        if (current) this.accountsSignal.set(current.map(a => (a.id === id ? { ...a, label: label || null } : a)));
      }),
      map(() => undefined),
      catchError((err: HttpErrorResponse) => this.fail(err)),
    );
  }

  /** Unlinks an account. Answers the sealed token `restore` takes to undo it, for a few minutes. */
  remove(id: string, userId?: string): Observable<string> {
    return this.http.delete<{ undoToken: string }>(this.itemUrl(id, userId)).pipe(
      tap(() => {
        const current = this.accountsSignal();
        if (current) this.accountsSignal.set(current.filter(a => a.id !== id));
      }),
      map(body => body.undoToken),
      catchError((err: HttpErrorResponse) => this.fail(err)),
    );
  }

  /** Drops the held list, so the next dialog never flashes the previous person's accounts. */
  reset(): void {
    this.accountsSignal.set(null);
    this.loadFailedSignal.set(false);
  }

  /** Puts back the account an unlink removed, from the token that unlink answered with. */
  restore(undoToken: string, userId?: string): Observable<PlayerAccount> {
    const url = userId
      ? `${this.config.apiHost}/api/admin/accounts/restore?id=${encodeURIComponent(userId)}`
      : `${this.config.apiHost}/api/accounts/restore`;
    return this.http.post<PlayerAccount>(url, { undoToken }).pipe(
      tap(restored => this.append(restored)),
      catchError((err: HttpErrorResponse) => this.fail(err)),
    );
  }

  private append(account: PlayerAccount): void {
    const current = this.accountsSignal();
    if (current) this.accountsSignal.set([...current.filter(a => a.id !== account.id), account]);
  }

  private collectionUrl(userId?: string): string {
    return userId ? `${this.config.apiHost}/api/admin/accounts?id=${encodeURIComponent(userId)}` : `${this.config.apiHost}/api/accounts`;
  }

  private fail(err: HttpErrorResponse): Observable<never> {
    this.snackBar.open(this.failureMessage(err), 'OK', { duration: 4000 });
    return throwError(() => err);
  }

  private failureMessage(err: HttpErrorResponse): string {
    const body = err.error as null | { code?: string; error?: string };
    // Worded here, not passed through: PoracleNG's 409 is lowercase English ("player id already linked") and
    // its 422 is a schema regex. The dialog checks what it can before sending, so these are races, not the norm.
    if (err.status === 409) return this.translate.instant('GAME_ACCOUNTS.ERROR_CONFLICT');
    if (err.status === 422) return this.translate.instant('GAME_ACCOUNTS.ERROR_INVALID');
    if (err.status === 400 && body?.code === 'undo_expired') return this.translate.instant('GAME_ACCOUNTS.UNDO_EXPIRED');
    if (err.status === 403 && body?.code === 'self_service_disabled') return this.translate.instant('GAME_ACCOUNTS.SELF_SERVICE_OFF');
    if (err.status === 429) return this.translate.instant('GAME_ACCOUNTS.RATE_LIMITED');
    return this.translate.instant('GAME_ACCOUNTS.FAILED');
  }

  private itemUrl(id: string, userId?: string): string {
    const path = `${this.config.apiHost}/api/${userId ? 'admin/' : ''}accounts/${encodeURIComponent(id)}`;
    return userId ? `${path}?id=${encodeURIComponent(userId)}` : path;
  }
}
