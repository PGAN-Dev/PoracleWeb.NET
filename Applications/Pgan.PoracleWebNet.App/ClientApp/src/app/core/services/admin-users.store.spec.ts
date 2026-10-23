import { signal, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';

import { AdminUsersStore } from './admin-users.store';
import { AdminService } from './admin.service';
import { AuthService } from './auth.service';
import { AdminUser } from '../models';

function row(id: string, overrides: Partial<AdminUser> = {}): AdminUser {
  return {
    id,
    name: id,
    adminDisable: 0,
    avatarUrl: null,
    currentProfileNo: 1,
    disabledDate: null,
    enabled: 1,
    kind: 'person',
    notes: null,
    type: 'discord:user',
    ...overrides,
  } as AdminUser;
}

/**
 * Users and Destinations read the same ~2,700-row list. One fetch serves both pages until something
 * changes a row, someone asks for a refresh, or the signed-in account changes. See #1038.
 */
describe('AdminUsersStore', () => {
  let getUsers: jest.Mock;
  let user: WritableSignal<{ id: string } | null>;
  let impersonating: WritableSignal<boolean>;
  let store: AdminUsersStore;

  beforeEach(() => {
    getUsers = jest.fn(() => of([row('a'), row('b')]));
    user = signal<{ id: string } | null>({ id: 'admin1' });
    impersonating = signal(false);
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: AdminService, useValue: { getUsers } },
        { provide: AuthService, useValue: { isImpersonating: impersonating, user } },
      ],
    });
    store = TestBed.inject(AdminUsersStore);
  });

  it('fetches once and serves the second page from the cache', () => {
    store.load().subscribe();
    store.load().subscribe();

    expect(getUsers).toHaveBeenCalledTimes(1);
    expect(store.users().map(u => u.id)).toEqual(['a', 'b']);
  });

  it('shares one request between two pages that ask before it answers', () => {
    const answer = new Subject<AdminUser[]>();
    getUsers.mockReturnValue(answer);
    const seen: number[] = [];

    store.load().subscribe(users => seen.push(users.length));
    store.load().subscribe(users => seen.push(users.length));
    answer.next([row('a')]);
    answer.complete();

    expect(getUsers).toHaveBeenCalledTimes(1);
    expect(seen).toEqual([1, 1]);
  });

  it('refetches on an explicit refresh', () => {
    store.load().subscribe();
    store.load(true).subscribe();

    expect(getUsers).toHaveBeenCalledTimes(2);
  });

  it('refetches once after a mutation', () => {
    store.load().subscribe();
    getUsers.mockReturnValue(of([row('a')]));

    store.invalidate();

    expect(getUsers).toHaveBeenCalledTimes(2);
    expect(store.users().map(u => u.id)).toEqual(['a']);
  });

  it('applies a row change straight away, before the refetch answers', () => {
    store.load().subscribe();

    store.patch('a', { enabled: 0 });
    store.remove('b');

    expect(store.users()).toEqual([expect.objectContaining({ id: 'a', enabled: 0 })]);
  });

  it('shows nothing once signed out, and fetches afresh for whoever signs in next', () => {
    store.load().subscribe();

    user.set(null);
    expect(store.users()).toEqual([]);

    user.set({ id: 'admin2' });
    expect(store.users()).toEqual([]);
    store.load().subscribe();
    expect(getUsers).toHaveBeenCalledTimes(2);
  });

  it('does not carry the list into an impersonation session', () => {
    store.load().subscribe();

    impersonating.set(true);

    expect(store.users()).toEqual([]);
  });

  it('keeps nothing from a failed fetch, so the next page tries again', () => {
    const failing = new Subject<AdminUser[]>();
    getUsers.mockReturnValueOnce(failing).mockReturnValue(of([row('a')]));

    store.load().subscribe({ error: () => undefined });
    failing.error(new Error('503'));
    store.load().subscribe();

    expect(getUsers).toHaveBeenCalledTimes(2);
    expect(store.users().map(u => u.id)).toEqual(['a']);
  });
});
