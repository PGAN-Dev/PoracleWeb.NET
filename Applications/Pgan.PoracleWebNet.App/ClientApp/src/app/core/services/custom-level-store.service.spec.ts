import { TestBed } from '@angular/core/testing';

import { CustomLevelStore } from './custom-level-store.service';

const STORAGE_PREFIX = 'poracle.custom-levels';

describe('CustomLevelStore', () => {
  beforeEach(() => {
    // Clear any persisted state from previous tests
    for (const key of Object.keys(localStorage)) {
      if (key.startsWith(STORAGE_PREFIX)) localStorage.removeItem(key);
    }
    TestBed.resetTestingModule();
  });

  function makeStore(): CustomLevelStore {
    TestBed.configureTestingModule({});
    return TestBed.inject(CustomLevelStore);
  }

  it('starts empty when localStorage has nothing', () => {
    const store = makeStore();
    expect(store.values('raid')).toEqual([]);
  });

  it('keeps separate palettes per key', () => {
    const store = makeStore();
    store.add('raid', 42);
    store.add('egg', 99);
    expect(store.values('raid')).toEqual([42]);
    expect(store.values('egg')).toEqual([99]);
    expect(store.values('boss')).toEqual([]);
  });

  it('persists per-key to separate localStorage slots', () => {
    const store = makeStore();
    store.add('raid', 42);
    store.add('egg', 99);
    expect(JSON.parse(localStorage.getItem(`${STORAGE_PREFIX}.raid`)!)).toEqual([42]);
    expect(JSON.parse(localStorage.getItem(`${STORAGE_PREFIX}.egg`)!)).toEqual([99]);
  });

  it('rejects all known levels (1-19 and 9000)', () => {
    const store = makeStore();
    for (let level = 1; level <= 19; level++) {
      expect(store.add('raid', level)).toBe(false);
    }
    expect(store.add('raid', 9000)).toBe(false);
    expect(store.values('raid')).toEqual([]);
  });

  it('rejects zero, negatives, and non-integers', () => {
    const store = makeStore();
    expect(store.add('raid', 0)).toBe(false);
    expect(store.add('raid', -3)).toBe(false);
    expect(store.add('raid', 7.5)).toBe(false);
    expect(store.add('raid', Number.NaN)).toBe(false);
    expect(store.values('raid')).toEqual([]);
  });

  it('deduplicates on add', () => {
    const store = makeStore();
    store.add('raid', 42);
    store.add('raid', 42);
    expect(store.values('raid')).toEqual([42]);
  });

  it('removes a value', () => {
    const store = makeStore();
    store.add('raid', 42);
    store.add('raid', 100);
    store.remove('raid', 42);
    expect(store.values('raid')).toEqual([100]);
    expect(JSON.parse(localStorage.getItem(`${STORAGE_PREFIX}.raid`)!)).toEqual([100]);
  });

  it('seeds custom values from existing alarms, skipping built-ins', () => {
    const store = makeStore();
    store.seedFrom('raid', [1, 7, 42, 9000, 99]);
    expect(store.values('raid')).toEqual([42, 99]);
  });

  it('loads persisted values on first access', () => {
    // 22/42/99 are above the canonical 1-19 known range, so they survive the load filter
    localStorage.setItem(`${STORAGE_PREFIX}.raid`, JSON.stringify([22, 42, 99]));
    const store = makeStore();
    expect(store.values('raid')).toEqual([22, 42, 99]);
  });

  it('drops invalid entries from persisted state on load', () => {
    // 1-19 and 9000 are known levels — filtered out. Strings and negatives also dropped.
    localStorage.setItem(`${STORAGE_PREFIX}.raid`, JSON.stringify([22, 'oops', -1, 9000, 42, 5]));
    const store = makeStore();
    expect(store.values('raid')).toEqual([22, 42]);
  });

  it('caps persisted entries at the LRU max per key', () => {
    const store = makeStore();
    for (let i = 100; i < 130; i++) {
      store.add('raid', i);
    }
    expect(store.values('raid').length).toBe(20);
    expect(store.values('raid')[0]).toBe(110);
    expect(store.values('raid')[19]).toBe(129);
  });

  it('survives malformed JSON in localStorage', () => {
    localStorage.setItem(`${STORAGE_PREFIX}.raid`, 'not-json-at-all');
    const store = makeStore();
    expect(store.values('raid')).toEqual([]);
  });
});
