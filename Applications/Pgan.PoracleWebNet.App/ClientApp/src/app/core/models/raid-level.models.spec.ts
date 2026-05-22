import {
  ANY_LEVEL,
  ANY_LEVEL_VALUE,
  isBuiltInLevel,
  makeCustomLevel,
  resolveLevel,
  SPECIAL_LEVELS,
  STANDARD_LEVELS,
} from './raid-level.models';

describe('raid-level.models', () => {
  describe('resolveLevel', () => {
    it('returns the ANY_LEVEL option for the 9000 sentinel', () => {
      expect(resolveLevel(9000)).toEqual(ANY_LEVEL);
      expect(resolveLevel(ANY_LEVEL_VALUE)).toEqual(ANY_LEVEL);
    });

    it('returns the standard option for tiers 1-5', () => {
      for (const level of [1, 2, 3, 4, 5]) {
        const opt = resolveLevel(level);
        expect(opt.value).toBe(level);
        expect(opt.category).toBe('standard');
        expect(opt.labelKey).toBe(`RAIDS.LEVEL.T${level}`);
      }
    });

    it('returns Mega for level 6 and Elite for level 7', () => {
      expect(resolveLevel(6).labelKey).toBe('RAIDS.LEVEL.MEGA');
      expect(resolveLevel(6).category).toBe('special');
      expect(resolveLevel(6).badge).toBe(6);
      expect(resolveLevel(7).labelKey).toBe('RAIDS.LEVEL.ELITE');
      expect(resolveLevel(7).category).toBe('special');
    });

    it('returns a custom option for unrecognized levels', () => {
      const opt = resolveLevel(42);
      expect(opt.value).toBe(42);
      expect(opt.category).toBe('custom');
      expect(opt.badge).toBe(42);
      expect(opt.labelKey).toBe('RAIDS.LEVEL.CUSTOM');
    });

    it('returns custom for level 0 and negative inputs (PoracleNG rejects them but the resolver is permissive)', () => {
      expect(resolveLevel(0).category).toBe('custom');
      expect(resolveLevel(-1).category).toBe('custom');
    });
  });

  describe('isBuiltInLevel', () => {
    it('is true for standard, special, and any', () => {
      expect(isBuiltInLevel(1)).toBe(true);
      expect(isBuiltInLevel(5)).toBe(true);
      expect(isBuiltInLevel(6)).toBe(true);
      expect(isBuiltInLevel(7)).toBe(true);
      expect(isBuiltInLevel(ANY_LEVEL_VALUE)).toBe(true);
    });

    it('is false for custom levels', () => {
      expect(isBuiltInLevel(8)).toBe(false);
      expect(isBuiltInLevel(42)).toBe(false);
      expect(isBuiltInLevel(0)).toBe(false);
    });
  });

  describe('makeCustomLevel', () => {
    it('builds a custom option that round-trips through resolveLevel', () => {
      const custom = makeCustomLevel(66);
      expect(resolveLevel(66)).toEqual(custom);
    });
  });

  describe('STANDARD_LEVELS and SPECIAL_LEVELS', () => {
    it('have non-overlapping values', () => {
      const std = new Set(STANDARD_LEVELS.map(l => l.value));
      const sp = new Set(SPECIAL_LEVELS.map(l => l.value));
      for (const v of std) expect(sp.has(v)).toBe(false);
    });

    it('do not include the ANY sentinel', () => {
      const all = [...STANDARD_LEVELS, ...SPECIAL_LEVELS].map(l => l.value);
      expect(all).not.toContain(ANY_LEVEL_VALUE);
    });
  });
});
