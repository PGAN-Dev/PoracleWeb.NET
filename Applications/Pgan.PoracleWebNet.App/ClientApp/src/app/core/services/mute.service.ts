import { HttpClient } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslateService } from '@ngx-translate/core';
import { Observable, catchError, map, of, tap } from 'rxjs';

import { ConfigService } from './config.service';
import { TokenStoreService } from './token-store.service';

/** The seven scopes PoracleNG can hold. Only four of them are ones this app creates. */
export type MuteScope = 'area' | 'everything' | 'gym' | 'pokemon' | 'pokestop' | 'station' | 'tracking';

/** Scopes with a control in the SPA. The other three are read-only here — see the backend's MuteScopes. */
export const WRITABLE_MUTE_SCOPES: MuteScope[] = ['gym', 'pokemon', 'area', 'station'];

export interface Mute {
  expiresAt: number;
  remainingSecs: number;
  scope: MuteScope;
  /**
   * For a `tracking` mute, the type of the rule it silences (`raid`, `pokemon`, ...): rule uids are
   * numbered per table, so the uid alone does not say which rule. Null everywhere else, and on every
   * server older than jfberry/PoracleNG#328 §6. A lift sends it back exactly when it is present.
   */
  trackingType?: null | string;
  value: null | string;
}

interface MutesResponse {
  mutes: Mute[];
}

/** The durations the quiet sheet offers, in minutes. 60 is PoracleNG's own default. */
export const QUIET_DURATIONS = [15, 30, 60, 240, 480, 1440];

/** How long a fetched list is treated as current, in ms. */
const STALE_AFTER_MS = 20_000;

/**
 * Quiet periods: PoracleNG's mute store, held here as a live signal.
 *
 * Two things about the upstream store shape everything in this file. It is keyed by (scope, value)
 * with no id, so that pair is the key here too. And it lives in the processor's memory, so a restart
 * wipes it with no notification of any kind — which is why nothing here trusts a list for longer than
 * a page view, and why the countdown ticks rather than resolving to a clock time.
 */
@Injectable({ providedIn: 'root' })
export class MuteService {
  private readonly config = inject(ConfigService);
  private readonly http = inject(HttpClient);
  private lastLoadedAt = 0;

  private readonly loadedSignal = signal(false);
  private readonly mutesSignal = signal<Mute[]>([]);
  /** Advances once a second while anything is quiet; the countdowns read it. */
  private readonly nowSignal = signal(Date.now());

  private readonly snackBar = inject(MatSnackBar);
  private ticker: ReturnType<typeof setInterval> | undefined;

  private readonly tokenStore = inject(TokenStoreService);
  private readonly translate = inject(TranslateService);

  /** Active mutes, with anything whose countdown has run out dropped as the clock passes it. */
  readonly mutes = computed(() => {
    const nowSecs = Math.floor(this.nowSignal() / 1000);
    return this.mutesSignal().filter(mute => mute.expiresAt > nowSecs);
  });

  readonly hasMutes = computed(() => this.mutes().length > 0);

  readonly loaded = this.loadedSignal.asReadonly();

  /** Unix seconds of the quiet period that lapses first, or null when nothing is quiet. */
  readonly soonestExpiry = computed(() => {
    const expiries = this.mutes().map(mute => mute.expiresAt);
    return expiries.length > 0 ? Math.min(...expiries) : null;
  });

  constructor() {
    // The store is upstream's memory: a deploy between two page views empties it silently, and coming
    // back to a tab is exactly when a stale countdown would be noticed. Refetch on focus.
    // This service is a root singleton and outlives the session, so the listener outlived Logout too:
    // every focus after signing out asked for a list that could only answer 401. A signed-out focus
    // instead drops the last user's list, so nobody signing in next sees it.
    const onFocus = (): void => {
      if (this.tokenStore.getAccessToken()) {
        this.refresh(true);
      } else {
        this.forget();
      }
    };
    if (typeof window !== 'undefined') window.addEventListener('focus', onFocus);

    inject(DestroyRef).onDestroy(() => {
      this.stopTicking();
      if (typeof window !== 'undefined') window.removeEventListener('focus', onFocus);
    });
  }

  /**
   * A relative countdown, never a clock time. "Quiet until 3:15pm" is a promise the server cannot keep:
   * the mute store is in memory and a restart clears it. A countdown that vanishes early reads as time
   * having passed; a deadline that vanishes early reads as a lie.
   */
  countdownLabel(remainingSecs: number): string {
    if (remainingSecs < 60) return this.translate.instant('QUIET.COUNTDOWN_UNDER_MINUTE');
    const minutes = Math.floor(remainingSecs / 60);
    if (minutes < 60) return this.translate.instant('QUIET.COUNTDOWN_MINUTES', { count: minutes });
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return this.translate.instant('QUIET.COUNTDOWN_HOURS', { count: hours });
    return this.translate.instant('QUIET.COUNTDOWN_DAYS', { count: Math.floor(hours / 24) });
  }

  /**
   * "1 hour", "15 min" - the same words the duration chips carry. One key per offered duration rather
   * than a count interpolation, because "1 hours" is not a phrase in English and the plural rules for
   * it differ across the eleven locales this ships in.
   */
  durationLabel(minutes: number): string {
    const key = `QUIET.DURATION_${minutes}`;
    const label = this.translate.instant(key);
    return label === key ? `${minutes} min` : label;
  }

  /**
   * The stored entry for a subject, whose `value` is the one a delete has to send back. A tracking type,
   * when given, has to match too: raid rule 60 and pokemon rule 60 are two mutes.
   */
  find(scope: MuteScope, value: null | string, trackingType: null | string = null): Mute | undefined {
    return this.mutes().find(mute => sameMute(mute, scope, value, trackingType));
  }

  isQuiet(scope: MuteScope, value: null | string): boolean {
    return this.remainingFor(scope, value) !== null;
  }

  /**
   * Refetches the list. Cheap and called often on purpose: the store is upstream's memory, so a deploy
   * between two page views can empty it without anything telling us.
   */
  load(): Observable<void> {
    return this.http.get<MutesResponse>(`${this.config.apiHost}/api/mutes`).pipe(
      tap(response => {
        this.apply(response);
        this.lastLoadedAt = Date.now();
      }),
      map(() => undefined),
      catchError(() => {
        // A failed read must not blank a list the user is looking at. Leave what we have; the next load
        // will correct it. /api/mutes is on the error interceptor's silent list, so say so here: a chip
        // that quietly shows nothing quiet while PoracleNG is down is worse than an error.
        this.loadedSignal.set(true);
        this.toast(this.translate.instant('QUIET.TOAST_LOAD_FAILED'));
        return of(undefined);
      }),
    );
  }

  /** Quiets a subject, or extends an existing quiet period on it. */
  quiet(scope: MuteScope, value: null | string, minutes: number): Observable<boolean> {
    return this.http
      .post<{ mute: Mute; replaced: boolean }>(`${this.config.apiHost}/api/mutes`, {
        durationMinutes: minutes,
        scope,
        value,
      })
      .pipe(
        tap(response => {
          this.upsert(response.mute);
          this.toast(
            response.replaced
              ? this.translate.instant('QUIET.TOAST_EXTENDED', { duration: this.durationLabel(minutes) })
              : this.translate.instant('QUIET.TOAST_QUIETED', { duration: this.durationLabel(minutes) }),
          );
        }),
        map(response => response.replaced),
        catchError((err: { error?: { error?: string }; status: number }) => {
          this.toast(this.failureMessage(err));
          throw err;
        }),
      );
  }

  /**
   * Loads the list unless it was read a moment ago. Called by every quiet surface as it appears, which
   * makes navigating to a page with chips on it a refetch — the closest thing to a live view of a store
   * that can vanish without telling anyone.
   */
  refresh(force = false): void {
    if (!force && Date.now() - this.lastLoadedAt < STALE_AFTER_MS) return;
    this.lastLoadedAt = Date.now();
    this.load().subscribe({ error: () => undefined });
  }

  /**
   * Seconds left on the quiet period covering this subject, or null when it is not quiet.
   *
   * Area values are compared case-insensitively: PoracleNG canonicalises an area name to the
   * geofence's own casing ("aberdeen" comes back as "Aberdeen") while PoracleWeb.NET stores area names
   * lowercase everywhere, so an exact comparison would never light the chip.
   */
  remainingFor(scope: MuteScope, value: null | string): null | number {
    const mute = this.find(scope, value);
    if (!mute) return null;
    return Math.max(0, mute.expiresAt - Math.floor(this.nowSignal() / 1000));
  }

  /**
   * Lifts one quiet period. Sends back the value the SERVER holds rather than the caller's, because the
   * upstream delete is an exact string match against a canonicalised area name.
   *
   * `trackingType` is the same rule for a bot-made `tracking` mute (jfberry/PoracleNG#328 §6): pass the
   * type the list reported, and it goes out only when there is one, so a server that never reports a
   * type never sees the parameter.
   */
  resume(scope: MuteScope, value: null | string, trackingType: null | string = null): Observable<void> {
    const stored = this.find(scope, value, trackingType);
    const serverValue = stored ? stored.value : value;
    const serverType = stored ? (stored.trackingType ?? null) : trackingType;
    const params: Record<string, string> = { scope };
    if (serverValue !== null && serverValue !== undefined) {
      params['value'] = serverValue;
    }
    if (serverType) {
      params['trackingType'] = serverType;
    }

    return this.http.delete<{ deleted: Mute[] }>(`${this.config.apiHost}/api/mutes`, { params }).pipe(
      tap(() => {
        this.remove(scope, serverValue, serverType);
        this.toast(this.translate.instant('QUIET.TOAST_RESUMED'));
      }),
      map(() => undefined),
      catchError((err: { error?: { error?: string }; status: number }) => {
        // 404 means it had already lapsed — the store expires entries itself. The user asked for it to
        // be gone and it is, so drop it locally and say nothing.
        if (err.status === 404) {
          this.remove(scope, serverValue, serverType);
          return of(undefined);
        }
        this.toast(this.failureMessage(err));
        throw err;
      }),
    );
  }

  /** Lifts every quiet period the account has, including ones set from the Discord bot. */
  resumeAll(): Observable<void> {
    return this.http.delete<{ deleted: Mute[] }>(`${this.config.apiHost}/api/mutes`).pipe(
      tap(() => {
        this.mutesSignal.set([]);
        this.stopTicking();
        this.toast(this.translate.instant('QUIET.TOAST_RESUMED'));
      }),
      map(() => undefined),
      catchError((err: { status: number }) => {
        this.toast(this.translate.instant('QUIET.TOAST_FAILED'));
        throw err;
      }),
    );
  }

  private apply(response: MutesResponse): void {
    this.mutesSignal.set(response.mutes ?? []);
    this.loadedSignal.set(true);
    this.syncTicker();
  }

  private failureMessage(err: { error?: { error?: string }; status: number }): string {
    // huma's 422 is the one refusal with something worth reading in it (an unknown area name), and the
    // API forwards its sentence verbatim.
    if (err.status === 422) return err.error?.error ?? this.translate.instant('QUIET.TOAST_FAILED');
    if (err.status === 429) return this.translate.instant('QUIET.TOAST_RATE_LIMITED');
    return this.translate.instant('QUIET.TOAST_FAILED');
  }

  /** Drops everything held for the signed-in user. */
  private forget(): void {
    this.mutesSignal.set([]);
    this.loadedSignal.set(false);
    this.lastLoadedAt = 0;
    this.stopTicking();
  }

  private remove(scope: MuteScope, value: null | string, trackingType: null | string = null): void {
    this.mutesSignal.set(this.mutesSignal().filter(mute => !sameMute(mute, scope, value, trackingType)));
    this.syncTicker();
  }

  private stopTicking(): void {
    if (this.ticker !== undefined) {
      clearInterval(this.ticker);
      this.ticker = undefined;
    }
    this.nowSignal.set(Date.now());
  }

  /** One interval for the whole app, and only while there is a countdown to move. */
  private syncTicker(): void {
    if (this.mutesSignal().length === 0) {
      this.stopTicking();
      return;
    }

    this.nowSignal.set(Date.now());
    if (this.ticker !== undefined) return;
    this.ticker = setInterval(() => {
      this.nowSignal.set(Date.now());
      if (this.mutes().length === 0) this.stopTicking();
    }, 1000);
  }

  private toast(message: string): void {
    this.snackBar.open(message, 'OK', { duration: 4000 });
  }

  private upsert(mute: Mute): void {
    const rest = this.mutesSignal().filter(existing => !sameMute(existing, mute.scope, mute.value, mute.trackingType ?? null));
    this.mutesSignal.set([...rest, mute]);
    this.nowSignal.set(Date.now());
    this.syncTicker();
  }
}

/**
 * Whether a stored mute is the one a (scope, value, trackingType) triple names. A null type matches any
 * type, which is what a server without typed tracking mutes needs and what the four scopes this app
 * creates always pass.
 */
function sameMute(mute: Mute, scope: MuteScope, value: null | string, trackingType: null | string): boolean {
  if (mute.scope !== scope || !sameValue(mute.value, value)) return false;
  return !trackingType || (mute.trackingType ?? null) === trackingType;
}

/**
 * Compares two mute values. Case-insensitive because area names come back canonicalised to the
 * geofence's casing while everything in PoracleWeb.NET holds them lowercase.
 */
function sameValue(a: null | string | undefined, b: null | string | undefined): boolean {
  if (a === null || a === undefined) return b === null || b === undefined;
  if (b === null || b === undefined) return false;
  return a.toLowerCase() === b.toLowerCase();
}
