import { computed, inject, Injectable, signal } from '@angular/core';
import { finalize, Observable, of, shareReplay, tap } from 'rxjs';

import { AdminService } from './admin.service';
import { AuthService } from './auth.service';
import { AdminUser } from '../models';

/**
 * The admin user list, fetched once and shared by Admin > Users and Admin > Destinations.
 *
 * Both pages filter the same `GET /api/admin/users` answer (about 2,700 rows in production), and each
 * used to fetch it on every visit. The list is kept until a row changes ({@link invalidate}), someone
 * presses Refresh (`load(true)`), or the signed-in account changes. It is keyed on who is signed in and
 * whether they are impersonating, so another account, or a session acting as one, never sees it.
 * See #1038.
 */
@Injectable({ providedIn: 'root' })
export class AdminUsersStore {
  private readonly admin = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly cache = signal<{ key: string; users: AdminUser[] } | null>(null);

  /** Who the cached list belongs to. A sign-out, another sign-in or an impersonation changes it. */
  private readonly identity = computed(() => `${this.auth.user()?.id ?? ''}|${this.auth.isImpersonating() ? 'as' : 'self'}`);

  private inFlight: { key: string; request: Observable<AdminUser[]> } | null = null;

  /** The cached rows, or none when they were fetched for somebody else. */
  readonly users = computed(() => {
    const cache = this.cache();
    return cache && cache.key === this.identity() ? cache.users : [];
  });

  /** Refetches in the background after a write that changed rows. */
  invalidate(): void {
    this.load(true).subscribe({ error: () => undefined });
  }

  /**
   * The list, from the cache when it is there and `force` is not set. Two pages asking at once share one
   * request. A failed fetch keeps nothing, so the next caller tries again.
   */
  load(force = false): Observable<AdminUser[]> {
    const key = this.identity();
    const cache = this.cache();
    if (!force && cache?.key === key) return of(cache.users);
    if (!force && this.inFlight?.key === key) return this.inFlight.request;

    const request = this.admin.getUsers().pipe(
      tap(users => this.cache.set({ key, users })),
      finalize(() => {
        if (this.inFlight?.request === request) this.inFlight = null;
      }),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    this.inFlight = { key, request };
    return request;
  }

  /** Applies a row change the server has confirmed, so the page shows it before any refetch answers. */
  patch(id: string, changes: Partial<AdminUser>): void {
    this.cache.update(cache => cache && { ...cache, users: cache.users.map(u => (u.id === id ? { ...u, ...changes } : u)) });
  }

  remove(id: string): void {
    this.cache.update(cache => cache && { ...cache, users: cache.users.filter(u => u.id !== id) });
  }
}
