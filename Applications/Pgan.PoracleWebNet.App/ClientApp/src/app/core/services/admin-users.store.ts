import { computed, effect, inject, Injectable, signal, untracked } from '@angular/core';
import { finalize, Observable, of, shareReplay, tap } from 'rxjs';

import { AdminService } from './admin.service';
import { AuthService } from './auth.service';
import { AdminUser } from '../models';

/**
 * The admin user list, shared by Admin > Users and Admin > Destinations.
 *
 * Both pages filter the same `GET /api/admin/users` answer (about 2,700 rows in production). Opening a
 * page shows the cached rows at once and checks them in the background, so the second page doesn't wait
 * and a change made outside this tab still shows up. A write that changes rows ({@link invalidate}) and
 * Refresh (`load(true)`) fetch again. Signing out, signing in as someone else, and starting or stopping
 * "View as" drop the list, so it is never shown to another account and never outlives a session spent
 * acting as one. See #1038.
 *
 * Only the most recently started request may write the list. Two writes a second apart start two
 * refetches; without the generation check the older answer could land last and undo the newer change.
 */
@Injectable({ providedIn: 'root' })
export class AdminUsersStore {
  private readonly admin = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly cache = signal<{ key: string; users: AdminUser[] } | null>(null);

  /** Bumped by every request started and every identity change; a response from an older one is dropped. */
  private generation = 0;

  /** Who the cached list belongs to. A sign-out, another sign-in or "View as" changes it. */
  private readonly identity = computed(() => `${this.auth.user()?.id ?? ''}|${this.auth.isImpersonating() ? 'as' : 'self'}`);

  private inFlight: { key: string; request: Observable<AdminUser[]> } | null = null;

  /** The cached rows, or none when they were fetched for somebody else. */
  readonly users = computed(() => {
    const cache = this.cache();
    return cache && cache.key === this.identity() ? cache.users : [];
  });

  constructor() {
    // Any change of who is signed in, including "View as" and back, drops the list outright. Keying alone
    // would hide it while impersonating and then show the old rows again on return.
    effect(() => {
      const key = this.identity();
      untracked(() => {
        if (this.cache() && this.cache()?.key !== key) this.drop();
      });
    });
  }

  /** Refetches in the background after a write that changed rows. */
  invalidate(): void {
    this.load(true).subscribe({ error: () => undefined });
  }

  /**
   * The list. With a cached copy and no `force`, answers with it at once and checks it in the background,
   * sharing a check already running. Otherwise fetches, sharing a fetch already running unless `force`
   * asks for a new one. A failed fetch keeps nothing, so the next caller tries again.
   */
  load(force = false): Observable<AdminUser[]> {
    const key = this.identity();
    const cache = this.cache();
    if (cache && cache.key !== key) this.drop();

    if (!force && this.inFlight?.key === key) {
      return this.cache()?.key === key ? of(this.cache()!.users) : this.inFlight.request;
    }
    if (!force && this.cache()?.key === key) {
      this.fetch(key).subscribe({ error: () => undefined });
      return of(this.cache()!.users);
    }
    return this.fetch(key);
  }

  /** Applies a row change the server has confirmed, so the page shows it before any refetch answers. */
  patch(id: string, changes: Partial<AdminUser>): void {
    this.cache.update(cache => cache && { ...cache, users: cache.users.map(u => (u.id === id ? { ...u, ...changes } : u)) });
  }

  remove(id: string): void {
    this.cache.update(cache => cache && { ...cache, users: cache.users.filter(u => u.id !== id) });
  }

  private drop(): void {
    this.cache.set(null);
    this.inFlight = null;
    this.generation++;
  }

  private fetch(key: string): Observable<AdminUser[]> {
    const generation = ++this.generation;
    const request = this.admin.getUsers().pipe(
      tap(users => {
        if (generation === this.generation) this.cache.set({ key, users });
      }),
      finalize(() => {
        if (this.inFlight?.request === request) this.inFlight = null;
      }),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    this.inFlight = { key, request };
    return request;
  }
}
