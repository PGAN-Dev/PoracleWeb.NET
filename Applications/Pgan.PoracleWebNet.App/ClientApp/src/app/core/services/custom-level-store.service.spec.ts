import { TestBed } from '@angular/core/testing';

import { CustomLevelStore } from './custom-level-store.service';

const STORAGE_KEY = 'poracle.custom-raid-levels';

describe('CustomLevelStore', () => {
  beforeEach(() => {
    localStorage.removeItem(STORAGE_KEY);
    TestBed.resetTestingModule();
  });

  function makeStore(): CustomLevelStore {
    TestBed.configureTestingModule({});
    return TestBed.inject(CustomLevelStore);
  }

  it('starts empty when localStorage has nothing', () => {
    const store = makeStore();
    expect(store.values()).toEqual([]);
  });

  it('adds a custom level and persists it', () => {
    const store = makeStore();
    expect(store.add(42)).toBe(true);
    expect(store.values()).toEqual([42]);
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY)!)).toEqual([42]);
  });

  it('rejects built-in levels (1-7 and 9000)', () => {
    const store = makeStore();
    for (const level of [1, 2, 3, 4, 5, 6, 7, 9000]) {
      expect(store.add(level)).toBe(false);
    }
    expect(store.values()).toEqual([]);
  });

  it('rejects zero, negatives, and non-integers', () => {
    const store = makeStore();
    expect(store.add(0)).toBe(false);
    expect(store.add(-3)).toBe(false);
    expect(store.add(7.5)).toBe(false);
    expect(store.add(Number.NaN)).toBe(false);
    expect(store.values()).toEqual([]);
  });

  it('deduplicates on add', () => {
    const store = makeStore();
    store.add(42);
    store.add(42);
    expect(store.values()).toEqual([42]);
  });

  it('removes a value', () => {
    const store = makeStore();
    store.add(42);
    store.add(100);
    store.remove(42);
    expect(store.values()).toEqual([100]);
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY)!)).toEqual([100]);
  });

  it('seeds custom values from existing alarms, skipping built-ins', () => {
    const store = makeStore();
    store.seedFrom([1, 7, 42, 9000, 99]); // 1, 7, 9000 are built-in
    expect(store.values()).toEqual([42, 99]);
  });

  it('loads persisted values on construction', () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify([8, 42, 99]));
    const store = makeStore();
    expect(store.values()).toEqual([8, 42, 99]);
  });

  it('drops invalid entries from persisted state on load', () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify([8, 'oops', -1, 9000, 42]));
    const store = makeStore();
    expect(store.values()).toEqual([8, 42]);
  });

  it('caps persisted entries at the LRU max', () => {
    const store = makeStore();
    for (let i = 100; i < 130; i++) {
      store.add(i);
    }
    // 30 unique entries added, cap is 20 — first 10 should be dropped
    expect(store.values().length).toBe(20);
    expect(store.values()[0]).toBe(110);
    expect(store.values()[19]).toBe(129);
  });

  it('survives malformed JSON in localStorage', () => {
    localStorage.setItem(STORAGE_KEY, 'not-json-at-all');
    const store = makeStore();
    expect(store.values()).toEqual([]);
  });
});
