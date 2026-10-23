import { signal } from '@angular/core';

import { AdminListState } from './admin-list-state';
import { AdminUser } from '../../core/models';

function row(id: string, overrides: Partial<AdminUser> = {}): AdminUser {
  return {
    id,
    name: id,
    adminDisable: 0,
    avatarUrl: null,
    currentProfileNo: 1,
    disabledDate: null,
    enabled: 1,
    kind: 'destination',
    language: null,
    lastChecked: null,
    notes: null,
    type: 'webhook',
    ...overrides,
  } as AdminUser;
}

const sortValue = (r: AdminUser, column: string): string | number => (column === 'id' ? r.id : '');

describe('AdminListState', () => {
  beforeEach(() => sessionStorage.clear());

  it('filters by type and keeps the choice per prefix', () => {
    const rows = signal([row('a', { type: 'webhook' }), row('b', { type: 'discord:channel' })]);
    const one = new AdminListState(rows, 'one_', sortValue);
    one.typeFilter.set('webhook');
    expect(one.filtered().map(r => r.id)).toEqual(['a']);
    expect(sessionStorage.getItem('one_type')).toBe('webhook');

    expect(new AdminListState(rows, 'one_', sortValue).typeFilter()).toBe('webhook');
    expect(new AdminListState(rows, 'two_', sortValue).typeFilter()).toBe('all');
  });

  it('searches notes as well as name and id', () => {
    const rows = signal([row('a', { notes: '"night shift"' }), row('b', { name: 'Zed' }), row('c')]);
    const s = new AdminListState(rows, 'p_', sortValue);
    s.searchTerm.set('night');
    expect(s.filtered().map(r => r.id)).toEqual(['a']);
    s.searchTerm.set('zed');
    expect(s.filtered().map(r => r.id)).toEqual(['b']);
  });

  it('survives sessionStorage throwing', () => {
    const get = jest.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const set = jest.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    try {
      const s = new AdminListState(signal([row('a')]), 'p_', sortValue);
      expect(s.pageSize()).toBe(25);
      expect(s.pageIndex()).toBe(0);
      expect(() => s.setPage(2, 50)).not.toThrow();
      expect(() => s.typeFilter.set('webhook')).not.toThrow();
      expect(s.pageSize()).toBe(50);
    } finally {
      get.mockRestore();
      set.mockRestore();
    }
  });

  it('typesPresent lists only types in the rows', () => {
    const rows = signal([row('a', { type: 'webhook' }), row('b', { type: 'webhook' }), row('c', { type: 'telegram:group' })]);
    expect(new AdminListState(rows, 'p_', sortValue).typesPresent()).toEqual(['telegram:group', 'webhook']);
  });

  it('filters by status, sorts and pages', () => {
    const rows = signal([row('b', { enabled: 0 }), row('a'), row('c', { adminDisable: 1 })]);
    const s = new AdminListState(rows, 'p_', sortValue);
    s.statusFilter.set('stopped');
    expect(s.filtered().map(r => r.id)).toEqual(['b']);
    s.statusFilter.set('all');
    s.sortActive.set('id');
    s.sortDirection.set('asc');
    expect(s.filtered().map(r => r.id)).toEqual(['a', 'b', 'c']);
    s.pageSize.set(2);
    s.pageIndex.set(1);
    expect(s.page().map(r => r.id)).toEqual(['c']);
  });
});
