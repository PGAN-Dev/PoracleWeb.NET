import { Injectable, signal } from '@angular/core';

import { ANY_LEVEL_VALUE, isBuiltInLevel } from '../models/raid-level.models';

const STORAGE_KEY = 'poracle.custom-raid-levels';

/** Maximum number of custom levels to persist. Excess values are dropped LRU-style. */
const MAX_ENTRIES = 20;

/**
 * Per-user palette of custom raid/egg levels — anything outside the standard
 * tiers (1-5), the special tiers (6/Mega, 7/Elite), and the 9000 "Any" sentinel.
 *
 * Backed by localStorage so a user's "always alarm on level 8" preference
 * survives across dialog opens. Reactive via a signal so the selector component
 * can re-render on add/remove without an event subscription.
 *
 * The store is permissive about ingest (it'll accept anything `isBuiltInLevel`
 * rejects) and strict about output (only positive non-built-in integers).
 */
@Injectable({ providedIn: 'root' })
export class CustomLevelStore {
  private readonly _values = signal<number[]>(this.load());
  readonly values = this._values.asReadonly();

  /** Add a value to the palette. Returns false if rejected (built-in or invalid). */
  add(value: number): boolean {
    if (!Number.isInteger(value) || value < 1) return false;
    if (isBuiltInLevel(value)) return false;
    this._values.update(current => {
      if (current.includes(value)) return current;
      const next = [...current, value];
      return next.length > MAX_ENTRIES ? next.slice(next.length - MAX_ENTRIES) : next;
    });
    this.persist();
    return true;
  }

  /** Remove a value from the palette. */
  remove(value: number): void {
    this._values.update(current => current.filter(v => v !== value));
    this.persist();
  }

  /**
   * Seed the palette with values found on existing alarms when the dialog opens.
   * Silently filters out built-ins and invalid values; deduplicates with the
   * existing palette.
   */
  seedFrom(values: Iterable<number>): void {
    let changed = false;
    this._values.update(current => {
      const seen = new Set(current);
      const next = [...current];
      for (const v of values) {
        if (!Number.isInteger(v) || v < 1 || isBuiltInLevel(v) || seen.has(v)) continue;
        seen.add(v);
        next.push(v);
        changed = true;
      }
      return next.length > MAX_ENTRIES ? next.slice(next.length - MAX_ENTRIES) : next;
    });
    if (changed) this.persist();
  }

  private load(): number[] {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return [];
      const parsed = JSON.parse(raw);
      if (!Array.isArray(parsed)) return [];
      return parsed.filter(v => Number.isInteger(v) && v >= 1 && v !== ANY_LEVEL_VALUE && !isBuiltInLevel(v)).slice(0, MAX_ENTRIES);
    } catch {
      return [];
    }
  }

  private persist(): void {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(this._values()));
    } catch {
      // Quota exceeded or storage disabled — the in-memory signal still works
      // for the current session; we just lose persistence.
    }
  }
}
