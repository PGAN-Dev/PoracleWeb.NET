import { Injectable, signal, WritableSignal } from '@angular/core';

import { ANY_LEVEL_VALUE, isBuiltInLevel } from '../models/raid-level.models';

const STORAGE_PREFIX = 'poracle.custom-levels';

/** Maximum number of custom levels to persist per palette. Excess values are dropped LRU-style. */
const MAX_ENTRIES = 20;

/**
 * Per-key palette of custom raid/egg/boss levels — anything outside the standard
 * tiers (1-5), the special tiers (6/Mega, 7/Elite), and the 9000 "Any" sentinel.
 *
 * Keyed by `paletteKey` (`raid` / `egg` / `boss`) so a custom level added on the
 * raid picker doesn't leak into the egg or boss palettes. Each key persists to
 * its own localStorage slot: `poracle.custom-levels.{key}`.
 */
@Injectable({ providedIn: 'root' })
export class CustomLevelStore {
  private readonly palettes = new Map<string, WritableSignal<number[]>>();

  /** Add a value to the palette. Returns false if rejected (built-in or invalid). */
  add(key: string, value: number): boolean {
    if (!Number.isInteger(value) || value < 1) return false;
    if (isBuiltInLevel(value)) return false;
    const sig = this.signalFor(key);
    sig.update(current => {
      if (current.includes(value)) return current;
      const next = [...current, value];
      return next.length > MAX_ENTRIES ? next.slice(next.length - MAX_ENTRIES) : next;
    });
    this.persist(key);
    return true;
  }

  /** Remove a value from the palette. */
  remove(key: string, value: number): void {
    const sig = this.signalFor(key);
    sig.update(current => current.filter(v => v !== value));
    this.persist(key);
  }

  /**
   * Seed the palette with values found on existing alarms when the dialog opens.
   * Silently filters out built-ins and invalid values; deduplicates with the
   * existing palette.
   */
  seedFrom(key: string, values: Iterable<number>): void {
    let changed = false;
    const sig = this.signalFor(key);
    sig.update(current => {
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
    if (changed) this.persist(key);
  }

  /** Current values for a palette, reactively. Reads inside a computed track the underlying signal. */
  values(key: string): readonly number[] {
    return this.signalFor(key)();
  }

  private load(key: string): number[] {
    try {
      const raw = localStorage.getItem(`${STORAGE_PREFIX}.${key}`);
      if (!raw) return [];
      const parsed = JSON.parse(raw);
      if (!Array.isArray(parsed)) return [];
      return parsed.filter(v => Number.isInteger(v) && v >= 1 && v !== ANY_LEVEL_VALUE && !isBuiltInLevel(v)).slice(0, MAX_ENTRIES);
    } catch {
      return [];
    }
  }

  private persist(key: string): void {
    try {
      localStorage.setItem(`${STORAGE_PREFIX}.${key}`, JSON.stringify(this.signalFor(key)()));
    } catch {
      // Quota exceeded or storage disabled — in-memory signal still works.
    }
  }

  private signalFor(key: string): WritableSignal<number[]> {
    let sig = this.palettes.get(key);
    if (!sig) {
      sig = signal<number[]>(this.load(key));
      this.palettes.set(key, sig);
    }
    return sig;
  }
}
