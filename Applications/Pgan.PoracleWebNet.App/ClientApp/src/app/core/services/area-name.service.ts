import { Injectable, Injector, effect, inject, signal } from '@angular/core';

import { AuthService } from './auth.service';
import { UserGeofenceService } from './user-geofence.service';
import { titleCaseArea } from '../../shared/utils/alarm-scope';
import { UserGeofence } from '../models';

/** After a failed read, how long before a chip asks again. */
const RETRY_AFTER_MS = 30_000;

/**
 * What to call an area the reader sees in a chip, a list or a summary.
 *
 * Stored area names are keys: lowercase, and for a drawn fence suffixed on a collision ("lake eola loop 2").
 * The person named it "Lake Eola Loop". Admin areas have no better name than their key, so they keep the
 * title-cased key. Display only: every request, mute value and comparison keeps the stored key.
 *
 * The caller's own fences are read on first use and again whenever the signed-in account changes (login,
 * logout, impersonation start and stop), so one account's names are never shown to another. The geofence
 * page feeds creates and renames in through `learn`. A failed read is not kept: the next use retries.
 */
@Injectable({ providedIn: 'root' })
export class AreaNameService {
  private attemptedAt = 0;
  private readonly injector = inject(Injector);
  private readonly loaded = signal<Map<string, string>>(new Map());
  private ok = false;
  private owner: null | string | undefined = undefined;
  private reading = false;
  /** Bumped on a session change so a read that was in flight for the old account is dropped. */
  private session = 0;

  constructor() {
    try {
      const auth = this.injector.get(AuthService);
      effect(() => this.sessionChanged(auth.user()?.id ?? null), { injector: this.injector });
    } catch {
      // No auth in this context: the names are read once, on first use.
    }
  }

  /** Forgets a fence that no longer exists. */
  forget(kojiName: string): void {
    const next = new Map(this.loaded());
    next.delete(kojiName.toLowerCase());
    this.loaded.set(next);
  }

  /** The name to show for a stored area name. */
  label(area: string): string {
    this.load();
    return this.loaded().get(area.toLowerCase()) ?? titleCaseArea(area);
  }

  /** Feeds fences a host has already read or just saved, so they needn't be read again. */
  learn(fences: Pick<UserGeofence, 'displayName' | 'kojiName'>[]): void {
    const next = new Map(this.loaded());
    for (const fence of fences) {
      if (fence.displayName && fence.kojiName) next.set(fence.kojiName.toLowerCase(), fence.displayName);
    }
    this.loaded.set(next);
  }

  private load(): void {
    if (this.ok || this.reading) return;
    if (this.attemptedAt > 0 && Date.now() - this.attemptedAt < RETRY_AFTER_MS) return;

    const session = this.session;
    this.reading = true;
    this.attemptedAt = Date.now();

    try {
      this.injector
        .get(UserGeofenceService)
        .getCustomGeofenceNames()
        .subscribe({
          error: () => {
            if (session === this.session) this.reading = false;
          },
          next: own => {
            if (session !== this.session) return;
            this.reading = false;
            this.ok = true;
            this.learn(own ?? []);
          },
        });
    } catch {
      // No HTTP in this context: keep the stored names.
      this.reading = false;
    }
  }

  private sessionChanged(id: null | string): void {
    if (this.owner === id) return;
    const first = this.owner === undefined;
    this.owner = id;
    if (first) return;

    this.session++;
    this.ok = false;
    this.reading = false;
    this.attemptedAt = 0;
    this.loaded.set(new Map());
  }
}
