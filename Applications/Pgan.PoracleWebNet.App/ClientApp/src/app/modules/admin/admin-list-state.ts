import { Signal, WritableSignal, computed, signal } from '@angular/core';

import { AdminUser } from '../../core/models';

export type StatusFilter = 'all' | 'active' | 'stopped' | 'blocked';

/**
 * Normalizes the Poracle `notes` value for display. PoracleJS/NG can leave a quoted-empty
 * sentinel (`""`) or whitespace in the column for users that aren't channels, which should
 * render nothing. Strips a single layer of surrounding quotes so a JSON-quoted note shows clean.
 */
export function notesLabel(notes: string | null): string | null {
  if (!notes) return null;
  let s = notes.trim();
  if (s.length >= 2 && ((s.startsWith('"') && s.endsWith('"')) || (s.startsWith("'") && s.endsWith("'")))) {
    s = s.slice(1, -1).trim();
  }
  return s.length > 0 ? s : null;
}

function readStored(key: string): string | null {
  try {
    return sessionStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeStored(key: string, value: string): void {
  try {
    sessionStorage.setItem(key, value);
  } catch {
    // Storage can be blocked; the choice then lasts for the page only.
  }
}

function readInt(key: string, fallback: number): number {
  const n = parseInt(readStored(key) || String(fallback), 10);
  return Number.isNaN(n) ? fallback : n;
}

/**
 * Search, status and type filters, sorting and paging for the Users and Destinations tables, so a
 * fix to any of them reaches both pages. `storagePrefix` keeps each page's paging and type choice apart.
 */
export class AdminListState<T extends AdminUser> {
  readonly filtered: Signal<T[]>;
  readonly page: Signal<T[]>;
  readonly pageIndex: WritableSignal<number>;
  readonly pageSize: WritableSignal<number>;
  readonly searchTerm = signal('');
  readonly sortActive = signal('');
  readonly sortDirection = signal<'asc' | 'desc' | ''>('');
  readonly statusFilter = signal<StatusFilter>('all');
  readonly typeFilter: WritableSignal<string>;
  readonly typesPresent: Signal<string[]>;

  constructor(
    rows: Signal<T[]>,
    private readonly storagePrefix: string,
    sortValue: (row: T, column: string) => string | number,
  ) {
    this.pageIndex = signal(readInt(`${storagePrefix}pageIndex`, 0));
    this.pageSize = signal(readInt(`${storagePrefix}pageSize`, 25));

    const type = signal(readStored(`${storagePrefix}type`) || 'all');
    const typeKey = `${storagePrefix}type`;
    this.typeFilter = Object.assign(type, {
      set: (value: string) => {
        writeStored(typeKey, value);
        type.update(() => value);
      },
    }) as WritableSignal<string>;

    this.typesPresent = computed(() => [...new Set(rows().map(r => (r.type ?? '').trim().toLowerCase()))].filter(t => t.length > 0).sort());

    this.filtered = computed(() => {
      const term = this.searchTerm().toLowerCase().trim();
      const status = this.statusFilter();
      const kind = this.typeFilter();
      let list = rows();

      if (term) {
        list = list.filter(
          u =>
            u.id.toLowerCase().includes(term) ||
            (u.name || '').toLowerCase().includes(term) ||
            (notesLabel(u.notes) || '').toLowerCase().includes(term),
        );
      }

      if (kind !== 'all') {
        list = list.filter(u => (u.type ?? '').trim().toLowerCase() === kind);
      }

      if (status !== 'all') {
        list = list.filter(u => {
          if (status === 'blocked') return u.adminDisable === 1;
          if (status === 'stopped') return u.adminDisable !== 1 && u.enabled === 0;
          if (status === 'active') return u.adminDisable !== 1 && u.enabled !== 0;
          return true;
        });
      }

      const active = this.sortActive();
      const direction = this.sortDirection();
      if (!active || !direction) return list;

      const dir = direction === 'asc' ? 1 : -1;
      return [...list].sort((a, b) => {
        const valA = sortValue(a, active);
        const valB = sortValue(b, active);
        if (valA < valB) return -1 * dir;
        if (valA > valB) return 1 * dir;
        return 0;
      });
    });

    this.page = computed(() => {
      const start = this.pageIndex() * this.pageSize();
      return this.filtered().slice(start, start + this.pageSize());
    });
  }

  /** Records the paginator's position and remembers it for the session. */
  setPage(pageIndex: number, pageSize: number): void {
    this.pageIndex.set(pageIndex);
    this.pageSize.set(pageSize);
    writeStored(`${this.storagePrefix}pageIndex`, String(pageIndex));
    writeStored(`${this.storagePrefix}pageSize`, String(pageSize));
  }
}
